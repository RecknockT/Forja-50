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
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool IsReady => worker is { HasExited: false } && client is not null;
    public bool HasProcess => worker is not null;
    public static string[] Arguments(AppPaths paths, GpuInfo gpu, int port) =>
    ["-s", Path.Combine(paths.ComfyRoot, "ComfyUI", "main.py"), "--windows-standalone-build", "--disable-auto-launch",
     "--listen", "127.0.0.1", "--port", port.ToString(), "--cuda-device", gpu.Index.ToString(),
     "--extra-model-paths-config", Path.Combine(paths.VisualRoot, "models.yaml"), "--output-directory", paths.VisualOutputs,
     "--input-directory", paths.VisualInputs, "--user-directory", Path.Combine(paths.VisualRoot, "user"),
     "--disable-all-custom-nodes", "--whitelist-custom-nodes", "forja_guard",
     "--offline", "--disable-assets", "--cache-none", "--disable-dynamic-vram", "--lowvram", "--reserve-vram", gpu.IsRtx3090 ? "2" : "1.5"];

    public async Task StartAsync(VisualSettings settings, GpuInfo gpu, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            _ = GpuProfiles.For(gpu);
            if (IsReady) return;
            await StopWorkerAsync();
            var provisioner = new VisualProvisioner(paths, new VerifiedDownloader(http));
            await provisioner.VerifyModelsAsync(false, ct); provisioner.WriteConfiguration();
            var port = RuntimeSupervisor.FreePort(); var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            worker = OwnedProcess.Start(paths.ComfyPython, Arguments(paths, gpu, port), paths.ComfyRoot, Path.Combine(paths.Logs, "visual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")),
                new Dictionary<string, string> { ["FORJA_VISUAL_TOKEN"] = token, ["PYTHONUTF8"] = "1", ["PYTHONNOUSERSITE"] = "1", ["HF_HUB_OFFLINE"] = "1", ["TRANSFORMERS_OFFLINE"] = "1" });
            await worker.WaitForReadyAsync(http, new($"http://127.0.0.1:{port}/forja/health"), "Bearer " + token, TimeSpan.FromMinutes(3), ct);
            client = new(http, new($"http://127.0.0.1:{port}/"), token);
            using var health = await client.GetAsync("forja/health", ct);
            if (!health.RootElement.TryGetProperty("forja", out var guard) || !guard.GetBoolean()) throw new IOException("El motor visual no confirmó su aislamiento.");
        }
        catch { await StopWorkerAsync(); throw; }
        finally { gate.Release(); }
    }
    public async Task<VisualJob> GenerateAsync(VisualRequest request, string? reference, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        var store = new VisualJobStore(paths); VisualJob? job = null;
        try
        {
            request.Validate();
            if (!IsReady || client is null) throw new InvalidOperationException("Iniciá primero la IA del modo visual.");
            if (request.Task == VisualTask.Animation)
                await new VisualProvisioner(paths, new VerifiedDownloader(http)).VerifyModelsAsync(true, ct);
            string? input = null;
            if (reference is not null)
            {
                var extension = Path.GetExtension(reference).ToLowerInvariant();
                if (extension is not (".png" or ".jpg" or ".jpeg") || !File.Exists(reference) || new FileInfo(reference).Length > 32_000_000) throw new InvalidDataException("Elegí una referencia PNG/JPG de hasta 32 MB.");
                input = Guid.NewGuid().ToString("N") + extension;
                File.Copy(reference, Path.Combine(paths.VisualInputs, input));
            }
            job = new() { Request = request, Reference = input, Status = "Generando" }; store.Save(job);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(45));
            job.Files = await client.GenerateAsync(request, job.Id, input, paths.VisualOutputs, progress, deadline.Token);
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
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly string token;
    public ComfyClient(HttpClient http, Uri endpoint, string token)
    {
        if (endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1" || endpoint.AbsolutePath != "/" || !string.IsNullOrEmpty(endpoint.UserInfo)) throw new ArgumentException("El motor visual debe ser local.");
        this.http = http; this.endpoint = endpoint; this.token = token;
    }
    public async Task<JsonDocument> GetAsync(string route, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, route)); request.Headers.Authorization = new("Bearer", token);
        using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    public async Task<string[]> GenerateAsync(VisualRequest request, string id, string? reference, string outputRoot, IProgress<PreparationProgress>? progress, CancellationToken ct, bool useSocket = true)
    {
        var graph = VisualWorkflows.Build(request, id, reference);
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
            try { await socket.ConnectAsync(new($"ws://127.0.0.1:{endpoint.Port}/ws?clientId={clientId}"), ct); socketTask = ObserveProgressAsync(socket, progress, socketLifetime.Token); }
            catch (WebSocketException) { /* History polling remains available. */ }
        }
        try
        {
            using var submit = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "prompt")) { Content = JsonContent.Create(new { prompt = graph, client_id = clientId }) };
            submit.Headers.Authorization = new("Bearer", token);
            using var response = await http.SendAsync(submit, ct);
            if (!response.IsSuccessStatusCode) throw new InvalidDataException("ComfyUI rechazó el flujo visual. Revisá Diagnóstico; no se generó un resultado.");
            using var accepted = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var promptId = accepted.RootElement.GetProperty("prompt_id").GetString()!;
            if (!Guid.TryParse(promptId, out _)) throw new InvalidDataException("Identificador del motor inválido.");
            progress?.Report(new("Generando; la primera carga puede tardar varios minutos"));
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                using var history = await GetAsync("history/" + promptId, ct);
                if (history.RootElement.TryGetProperty(promptId, out var result))
                {
                    if (result.TryGetProperty("status", out var status))
                    {
                        if (status.TryGetProperty("status_str", out var state) && state.GetString() == "error") throw new IOException("El motor no pudo completar la generación. Puede faltar memoria; revisá los registros y reducí la resolución o duración.");
                        if (status.TryGetProperty("completed", out var completed) && completed.GetBoolean()) return ReadFiles(result, outputRoot, id);
                    }
                }
                await Task.Delay(1000, ct);
            }
        }
        finally { socketLifetime.Cancel(); socket.Abort(); try { await socketTask; } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { } }
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
