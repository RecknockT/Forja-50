using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MiIALocal.Core;

public sealed class VisualRuntime(AppPaths paths, HttpClient http) : IAsyncDisposable
{
    private OwnedProcess? worker;
    private ComfyClient? client;
    private GpuInfo? activeGpu;
    private bool promptAssistant;
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool IsReady => worker is { HasExited: false } && client is not null;
    public bool HasProcess => worker is not null;
    public static string[] Arguments(AppPaths paths, GpuInfo gpu, int port) =>
    ["-s", Path.Combine(paths.ComfyRoot, "ComfyUI", "main.py"), "--windows-standalone-build", "--disable-auto-launch",
     "--listen", "127.0.0.1", "--port", port.ToString(), "--cuda-device", gpu.Index.ToString(),
     "--extra-model-paths-config", Path.Combine(paths.VisualRoot, "models.yaml"), "--output-directory", paths.VisualOutputs,
     "--input-directory", paths.VisualInputs, "--user-directory", Path.Combine(paths.VisualRoot, "user"),
     "--disable-all-custom-nodes", "--whitelist-custom-nodes", "forja_guard", "ComfyUI-GGUF",
     "--disable-pinned-memory", "--offline", "--disable-assets", "--cache-none", "--disable-dynamic-vram", "--lowvram", "--reserve-vram", gpu.IsRtx3090 ? "2" : "1.5"];

    public async Task StartAsync(VisualSettings settings, GpuInfo gpu, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            _ = GpuProfiles.For(gpu);
            if (IsReady) return;
            await StopWorkerAsync();
            var provisioner = new VisualProvisioner(paths, new VerifiedDownloader(http));
            if (!settings.HiDreamLicenseAccepted) throw new InvalidOperationException("Revisá y aceptá las licencias de HiDream en Modelos y descargas.");
            await provisioner.VerifyModelsAsync(false, ct, gpu, settings.EnhancePrompt, settings.FullEdit, settings.LargeAssistant); activeGpu = gpu; promptAssistant = settings.EnhancePrompt; provisioner.WriteConfiguration();
            var port = RuntimeSupervisor.FreePort(); var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            worker = OwnedProcess.Start(paths.ComfyPython, Arguments(paths, gpu, port), paths.ComfyRoot, Path.Combine(paths.Logs, "visual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")),
                new Dictionary<string, string> { ["FORJA_VISUAL_TOKEN"] = token, ["PYTHONUTF8"] = "1", ["PYTHONNOUSERSITE"] = "1", ["HF_HUB_OFFLINE"] = "1", ["TRANSFORMERS_OFFLINE"] = "1" });
            await worker.WaitForReadyAsync(http, new($"http://127.0.0.1:{port}/forja/health"), "Bearer " + token, TimeSpan.FromMinutes(3), ct);
            client = new(http, new($"http://127.0.0.1:{port}/"), token, () => worker is { HasExited: false });
            using var health = await client.GetAsync("forja/health", ct);
            if (!health.RootElement.TryGetProperty("forja", out var guard) || !guard.GetBoolean()) throw new IOException("El motor visual no confirmó su aislamiento.");
        }
        catch { await StopWorkerAsync(); throw; }
        finally { gate.Release(); }
    }
    public async Task<string> AnalyzeReferenceAsync(ReferenceAnalysis task, string input, long seed, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!IsReady || client is null || !promptAssistant) throw new InvalidOperationException("Iniciá la IA con Gemma activado para buscar referencias con el asistente.");
            return await client.AnalyzeReferenceAsync(task, input, seed, progress, ct);
        }
        catch (OperationCanceledException) { await StopWorkerAsync(); throw; }
        catch (Exception ex) { await StopWorkerAsync(); throw new InvalidOperationException("Gemma no pudo analizar la referencia. Revisá Diagnóstico y volvé a iniciar la IA. " + ex.Message, ex); }
        finally { gate.Release(); }
    }
    public async Task<PreparedPrompt> PreparePromptAsync(VisualRequest request, string? reference, IProgress<PreparationProgress>? progress, CancellationToken ct, VisualReference? onlineReference = null, string? contextTitle = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            request.Validate();
            if (!request.EnhancePrompt) throw new InvalidOperationException("El asistente de descripción está desactivado.");
            if (!promptAssistant) throw new InvalidOperationException("El perfil cambió. Detené y volvé a iniciar la IA.");
            if (!IsReady || client is null) throw new InvalidOperationException("Iniciá primero la IA del modo visual.");
            var input = StageReference(reference);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var text = await client.PreparePromptAsync(request, input, progress, ct, true, null, contextTitle ?? onlineReference?.Title);
            return new(text, input, elapsed.Elapsed.TotalSeconds);
        }
        catch
        {
            // Terminating our worker also interrupts GPU execution if ComfyUI cannot cancel a kernel promptly.
            await StopWorkerAsync(); throw;
        }
        finally { gate.Release(); }
    }
    public async Task<VisualJob> GenerateAsync(VisualRequest request, string? reference, IProgress<PreparationProgress>? progress, CancellationToken ct, VisualReference? onlineReference = null, PreparedPrompt? prepared = null, string? contextTitle = null)
    {
        await gate.WaitAsync(ct);
        var store = new VisualJobStore(paths); VisualJob? job = null;
        try
        {
            request.Validate();
            if (request.EnhancePrompt != promptAssistant) throw new InvalidOperationException("El perfil cambió. Detené y volvé a iniciar la IA.");
            if (!IsReady || client is null) throw new InvalidOperationException("Iniciá primero la IA del modo visual.");
            if (prepared is not null && !request.EnhancePrompt) prepared = null;
            if (request.Task == VisualTask.Animation && request.AnimationGguf != (activeGpu?.IsRtx3090 == true)) throw new InvalidOperationException("El perfil de animación no coincide con la placa. Volvé a iniciar la IA.");
            if (request.Task == VisualTask.Animation)
                await new VisualProvisioner(paths, new VerifiedDownloader(http)).VerifyModelsAsync(true, ct, activeGpu, promptAssistant, true, true);
            var input = prepared?.StagedReference ?? StageReference(reference);
            job = new() { Request = request, Reference = input, OnlineReference = onlineReference, Status = "Generando" }; store.Save(job);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(45));
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            job.Files = await client.GenerateAsync(request, job.Id, input, paths.VisualOutputs, progress, deadline.Token, true, prepared?.Text, contextTitle ?? onlineReference?.Title);
            job.PreparedPrompt = client.LastPreparedPrompt; job.TotalSeconds = elapsed.Elapsed.TotalSeconds + (prepared?.PreparationSeconds ?? 0);
            job.Status = "Completado"; store.Save(job); return job;
        }
        catch (Exception ex)
        {
            if (job is not null) { job.Status = ex is OperationCanceledException ? "Cancelado" : "Error"; job.Error = ex.Message; store.Save(job); }
            // Terminating our worker also interrupts GPU execution if ComfyUI cannot cancel a kernel promptly.
            await StopWorkerAsync(); throw;
        }
        finally { gate.Release(); }
    }
    private string? StageReference(string? reference)
    {
        if (reference is null) return null;
        var extension = Path.GetExtension(reference).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg") || !File.Exists(reference) || new FileInfo(reference).Length > 32_000_000) throw new InvalidDataException("Elegí una referencia PNG/JPG de hasta 32 MB.");
        Directory.CreateDirectory(paths.VisualInputs);
        var input = Guid.NewGuid().ToString("N") + extension;
        File.Copy(reference, Path.Combine(paths.VisualInputs, input));
        return input;
    }
    public async Task StopAsync()
    {
        await gate.WaitAsync(); try { await StopWorkerAsync(); } finally { gate.Release(); }
    }
    private async Task StopWorkerAsync()
    { client = null; var previous = worker; worker = null; if (previous is not null) await previous.DisposeAsync(); }
    public async ValueTask DisposeAsync() { await StopAsync(); gate.Dispose(); }
}

public sealed class ComfyClient
{
    public string? LastPreparedPrompt { get; private set; }
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly string token;
    private readonly Func<bool>? workerAlive;
    public ComfyClient(HttpClient http, Uri endpoint, string token, Func<bool>? workerAlive = null)
    {
        if (endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1" || endpoint.AbsolutePath != "/" || !string.IsNullOrEmpty(endpoint.UserInfo)) throw new ArgumentException("El motor visual debe ser local.");
        this.http = http; this.endpoint = endpoint; this.token = token; this.workerAlive = workerAlive;
    }
    public async Task<JsonDocument> GetAsync(string route, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, route)); request.Headers.Authorization = new("Bearer", token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await http.SendAsync(request, deadline.Token); response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) { throw new IOException("El motor visual no responde. Detené y volvé a iniciar la IA; tus archivos se conservan.", ex); }
    }
    public async Task<string> AnalyzeReferenceAsync(ReferenceAnalysis task, string input, long seed, IProgress<PreparationProgress>? progress, CancellationToken ct, bool useSocket = true)
    {
        LastPreparedPrompt = null;
        var graph = VisualWorkflows.BuildReferenceAnalysis(task, input, seed);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(5));
        JsonElement result;
        try { result = await RunAsync(graph, progress, deadline.Token, useSocket, task == ReferenceAnalysis.Plan ? "Gemma está identificando qué referencia buscar" : "Gemma está comparando los resultados de Wikipedia"); }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) { throw new IOException("Gemma agotó el tiempo de análisis de referencias.", ex); }
        // Structured planning JSON must never receive caption post-processing or become the final prompt.
        return ReadPreparedPrompt(result) ?? throw new IOException("Gemma no devolvió un análisis de referencia.");
    }
    /// <summary>Run only the assistant stage so its caption can be reviewed before diffusion starts.</summary>
    public async Task<string> PreparePromptAsync(VisualRequest request, string? reference, IProgress<PreparationProgress>? progress, CancellationToken ct, bool useSocket = true, TimeSpan? timeout = null, string? referenceTitle = null)
    {
        LastPreparedPrompt = null;
        var graph = VisualWorkflows.BuildPrepare(request, reference, referenceTitle);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(timeout ?? TimeSpan.FromMinutes(5));
        JsonElement result;
        try { result = await RunAsync(graph, progress, deadline.Token, useSocket, "Preparando la descripción con el asistente en GPU"); }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) { throw new IOException("El asistente agotó el tiempo de preparación. Podés desactivarlo y usar tu descripción original, o reiniciar la IA.", ex); }
        var text = ReadPreparedPrompt(result);
        if (text is null) throw new IOException("El asistente no devolvió una descripción utilizable. Revisá Diagnóstico.");
        text = VisualPrompts.EnsureNamedSubject(text, request.Prompt, referenceTitle);
        LastPreparedPrompt = text;
        return text;
    }
    public async Task<string[]> GenerateAsync(VisualRequest request, string id, string? reference, string outputRoot, IProgress<PreparationProgress>? progress, CancellationToken ct, bool useSocket = true, string? preparedPrompt = null, string? referenceTitle = null)
    {
        LastPreparedPrompt = null;
        var graph = VisualWorkflows.Build(request, id, reference, preparedPrompt, referenceTitle);
        var result = await RunAsync(graph, progress, ct, useSocket, "Generando; la primera carga puede tardar varios minutos");
        LastPreparedPrompt = preparedPrompt ?? (request.Task == VisualTask.Animation ? null : ReadPreparedPrompt(result));
        if (request.Task != VisualTask.Animation && LastPreparedPrompt is null) throw new IOException("HiDream terminó sin registrar la descripción utilizada. Revisá Diagnóstico.");
        return ReadFiles(result, outputRoot, id);
    }
    private async Task<JsonElement> RunAsync(Dictionary<string, object> graph, IProgress<PreparationProgress>? progress, CancellationToken ct, bool useSocket, string submitted)
    {
        // Check the installed node contract before accepting a potentially long generation.
        using var schema = await GetAsync("object_info", ct);
        foreach (var node in graph.Values)
        {
            using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(node));
            var name = serialized.RootElement.GetProperty("class_type").GetString()!;
            if (!schema.RootElement.TryGetProperty(name, out _)) throw new InvalidDataException("Falta un nodo visual requerido: " + name + ". Prepará el motor para repararlo.");
        }
        var clientId = Guid.NewGuid().ToString("N");
        using var socketLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var socket = new ClientWebSocket();
        Task socketTask = Task.CompletedTask;
        if (useSocket)
        {
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(ct); connection.CancelAfter(TimeSpan.FromSeconds(5));
            try { await socket.ConnectAsync(new($"ws://127.0.0.1:{endpoint.Port}/ws?clientId={clientId}"), connection.Token); socketTask = ObserveProgressAsync(socket, progress, socketLifetime.Token); }
            catch (WebSocketException) { /* History polling remains available. */ }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* History polling remains available. */ }
        }
        try
        {
            using var submit = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "prompt")) { Content = JsonContent.Create(new { prompt = graph, client_id = clientId }) };
            submit.Headers.Authorization = new("Bearer", token);
            using var response = await http.SendAsync(submit, ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidDataException("ComfyUI rechazó el flujo visual" + DescribeRejection(await response.Content.ReadAsStringAsync(ct)) + ". Revisá Diagnóstico; no se generó un resultado.");
            using var accepted = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var promptId = accepted.RootElement.GetProperty("prompt_id").GetString()!;
            if (!Guid.TryParse(promptId, out _)) throw new InvalidDataException("Identificador del motor inválido.");
            progress?.Report(new(submitted));
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (workerAlive is not null && !workerAlive()) throw new IOException("El motor visual se cerró durante la tarea. Revisá Diagnóstico y volvé a iniciar la IA.");
                using var history = await GetAsync("history/" + promptId, ct);
                if (history.RootElement.TryGetProperty(promptId, out var result) && result.TryGetProperty("status", out var status))
                {
                    if (status.TryGetProperty("status_str", out var state) && state.GetString() == "error") throw new IOException(DescribeFailure(status));
                    if (status.TryGetProperty("completed", out var completed) && completed.GetBoolean()) return result.Clone();
                }
                await Task.Delay(1000, ct);
            }
        }
        finally { socketLifetime.Cancel(); socket.Abort(); try { await socketTask; } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { } }
    }
    public static string? ReadPreparedPrompt(JsonElement result)
    {
        if (!result.TryGetProperty("outputs", out var outputs) || !outputs.TryGetProperty("32", out var prepared)
            || !prepared.TryGetProperty("text", out var texts) || texts.ValueKind != JsonValueKind.Array || texts.GetArrayLength() == 0 || texts[0].ValueKind != JsonValueKind.String) return null;
        var text = texts[0].GetString()?.Trim(); return text?.Length is > 0 and <= 12000 ? text : null;
    }
    /// <summary>Reason ComfyUI rejected a prompt: the node and the offending input, when it reports them.</summary>
    public static string DescribeRejection(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var parts = new List<string>();
            if (root.TryGetProperty("error", out var error))
                foreach (var name in new[] { "type", "message" })
                    if (error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) parts.Add(value.GetString()!.Trim());
            if (root.TryGetProperty("node_errors", out var nodes) && nodes.ValueKind == JsonValueKind.Object)
                foreach (var node in nodes.EnumerateObject())
                    if (node.Value.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                        foreach (var item in errors.EnumerateArray())
                            if (item.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(message.GetString()))
                                parts.Add("nodo " + node.Name + ": " + message.GetString()!.Trim());
            var text = string.Join(": ", parts.Distinct());
            if (text.Length > 300) text = text[..300] + "…";
            return text.Length == 0 ? "" : " · " + text;
        }
        catch (JsonException) { return ""; }
    }
    public static string DescribeFailure(JsonElement status)
    {
        if (status.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            foreach (var message in messages.EnumerateArray())
            {
                if (message.ValueKind != JsonValueKind.Array || message.GetArrayLength() < 2 || message[0].GetString() != "execution_error") continue;
                var error = message[1];
                var type = error.TryGetProperty("exception_type", out var exceptionType) ? exceptionType.GetString() ?? "" : "";
                var detail = error.TryGetProperty("exception_message", out var exceptionMessage) ? exceptionMessage.GetString() ?? "" : "";
                var node = error.TryGetProperty("node_type", out var nodeType) ? nodeType.GetString() ?? "" : "";
                if (type.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase) || detail.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
                    return "El motor se quedó sin memoria. Cerrá otras aplicaciones que usen la GPU y reducí la resolución o duración; revisá Diagnóstico para ver el detalle.";
                var brief = string.Join(' ', detail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (brief.Length > 350) brief = brief[..350] + "…";
                return $"La generación falló en {(string.IsNullOrWhiteSpace(node) ? "el motor" : node)}: {(string.IsNullOrWhiteSpace(brief) ? type : brief)}. Revisá Diagnóstico para ver el registro completo.";
            }
        return "El motor no pudo completar la generación. Revisá Diagnóstico para conocer la causa; tus resultados anteriores se conservan.";
    }
    public static string[] ReadFiles(JsonElement result, string outputRoot, string id)
    {
        var files = new List<string>();
        if (result.TryGetProperty("outputs", out var outputs)) foreach (var node in outputs.EnumerateObject())
            foreach (var kind in new[] { "images", "gifs", "videos" })
                if (node.Value.TryGetProperty(kind, out var entries) && entries.ValueKind == JsonValueKind.Array)
                    foreach (var entry in entries.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("type", out var type) || type.GetString() != "output") continue;
                        var name = entry.GetProperty("filename").GetString()!;
                        var sub = entry.TryGetProperty("subfolder", out var folder) ? folder.GetString() ?? "" : "";
                        var path = SafeArchive.ResolveEntry(outputRoot, string.IsNullOrEmpty(sub) ? name : sub.Replace('\\', '/') + "/" + name);
                        if (!Path.GetFileName(path).StartsWith(id + "_", StringComparison.Ordinal) || Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".mp4" or ".gif")) throw new InvalidDataException("Resultado visual fuera de la tarea.");
                        if (!File.Exists(path)) throw new IOException("El motor informó un resultado que no existe.");
                        files.Add(path);
                    }
        if (files.Count == 0) throw new IOException("La generación terminó sin archivos de salida.");
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static async Task ObserveProgressAsync(ClientWebSocket socket, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        var buffer = new byte[32768];
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var message = await socket.ReceiveAsync(buffer.AsMemory(), ct);
                if (message.MessageType != WebSocketMessageType.Text || !message.EndOfMessage) continue;
                try
                {
                    using var data = JsonDocument.Parse(buffer.AsMemory(0, message.Count));
                    if (data.RootElement.GetProperty("type").GetString() == "executing" && data.RootElement.GetProperty("data").TryGetProperty("node", out var node))
                    {
                        var stage = node.ValueKind == JsonValueKind.String ? node.GetString() switch { "31" => "Preparando la descripción y analizando la referencia", "32" => "Liberando memoria del asistente visual", "11" => "Generando la imagen o animación", "12" => "Convirtiendo el resultado en imagen", _ => null } : null;
                        if (stage is not null) progress?.Report(new(stage));
                    }
                    if (data.RootElement.GetProperty("type").GetString() == "progress")
                    {
                        var p = data.RootElement.GetProperty("data"); var value = p.GetProperty("value").GetDouble(); var max = p.GetProperty("max").GetDouble();
                        if (max > 0) progress?.Report(new($"Generando · paso {value:0} de {max:0}", (long)value, (long)max));
                    }
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
            }
        }
        catch (WebSocketException) { }
    }
}
