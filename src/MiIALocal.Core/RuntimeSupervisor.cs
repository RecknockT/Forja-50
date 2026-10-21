using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace MiIALocal.Core;

public enum RuntimeState { Stopped, Loading, Ready, Stopping, Error }
public sealed class RuntimeSupervisor(AppPaths paths, HttpClient client) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private OwnedProcess? model, agent;
    private string project = "";
    private string modelKey = "";
    private string loadedProfile = "";
    public string AgentPassword { get; private set; } = "";
    public int AgentPort { get; private set; }
    public int ModelPort { get; private set; }
    public RuntimeState State { get; private set; } = RuntimeState.Stopped;
    public event Action<RuntimeState, string>? StateChanged;
    private void SetState(RuntimeState state, string text) { State = state; StateChanged?.Invoke(state, text); }

    public async Task<string> StartAsync(AppSettings settings, GpuInfo gpu, string selectedProject, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var preset = GpuProfiles.For(gpu);
            if (settings.HardwareProfileId != preset.Id) throw new InvalidOperationException("La placa cambió. Volvé a comprobar el equipo y preparar su motor antes de iniciar.");
            GpuProfiles.ValidateBackend(settings);
            var ninfer5080 = settings.EngineMode == EngineMode.NInfer5080;
            if (ninfer5080) settings.NativeTuning.Validate(settings.ContextTokens);
            else { _ = ContextProfile.For(settings.ContextTokens); settings.Tuning.Validate(settings.ContextTokens); }
            var engineExe = ninfer5080 ? paths.NinferEngineExe(settings) : paths.KvMemEngineExe(settings);
            var modelPath = paths.ActiveModelPath(settings);
            var modelAsset = ReleaseProfile.ModelFor(settings.EngineMode);
            var profileKey = string.Join(':', settings.EngineMode, settings.ContextTokens, gpu.Index, modelPath, engineExe,
                ninfer5080 ? Ninfer5080Profile.Signature(settings.ContextTokens, settings.NinferVision) + System.Text.Json.JsonSerializer.Serialize(settings.NativeTuning) : System.Text.Json.JsonSerializer.Serialize(settings.Tuning));
            selectedProject = Path.GetFullPath(selectedProject);
            if (!Directory.Exists(selectedProject)) throw new DirectoryNotFoundException("Elegí una carpeta de proyecto válida.");
            if (State == RuntimeState.Ready && loadedProfile == profileKey && project == selectedProject && model is { HasExited: false } && agent is { HasExited: false }) return AgentConfiguration.ProjectUrl(AgentPort, project);
            if (!new Provisioner(paths, new VerifiedDownloader(client)).IsPrepared(settings)) throw new InvalidOperationException("Primero prepará la IA o importá tu modelo descargado.");
            SetState(RuntimeState.Loading, "Preparando el proyecto y cargando Qwen en tu placa...");
            if (agent is not null) { await agent.DisposeAsync(); agent = null; }
            if (model is not null && loadedProfile != profileKey) { await model.DisposeAsync(); model = null; }
            if (model is null || model.HasExited)
            {
                if (model is not null) { await model.DisposeAsync(); model = null; }
                if (!await VerifiedDownloader.MatchesAsync(modelPath, modelAsset, ct)) throw new InvalidDataException("El modelo no coincide con esta versión. Usá Preparar IA para repararlo.");
                if (!File.Exists(engineExe)) throw new InvalidOperationException("Falta el motor de esta versión. Usá Preparar IA para repararlo.");
                ModelPort = FreePort(); modelKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
                var engineArguments = ninfer5080
                    ? Ninfer5080Arguments(modelPath, ModelPort, modelKey, settings.ContextTokens, settings.NinferVision, gpu.Index, Ninfer5080Profile.For(gpu).PrefillChunk, settings.NativeTuning)
                    : EngineArguments(modelPath, gpu.Index, ModelPort, modelKey, settings.ContextTokens, settings.Tuning);
                model = OwnedProcess.Start(engineExe, engineArguments, Path.GetDirectoryName(engineExe)!, NewLog("modelo"));
                await model.WaitForReadyAsync(client, new($"http://127.0.0.1:{ModelPort}/health"), "Bearer " + modelKey, TimeSpan.FromMinutes(4), ct);
                loadedProfile = profileKey;
            }
            ct.ThrowIfCancellationRequested();
            AgentPort = FreePort(); AgentPassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var instruction = Path.Combine(paths.Root, "instrucciones.md"); AppPaths.AtomicWrite(instruction, AgentConfiguration.Instructions);
            var agentTuning = ninfer5080 ? new EngineTuning { MaxOutput = settings.NativeTuning.MaxOutput, AgentTemperature = settings.NativeTuning.AgentTemperature } : settings.Tuning;
            var agentModelName = ninfer5080 ? "Qwen3.8-27B · NInfer-5080" : "Qwen 27B · Mi IA Local";
            var environment = new Dictionary<string, string>
            {
                ["XDG_DATA_HOME"] = Path.Combine(paths.Root, "agent-data"), ["XDG_STATE_HOME"] = Path.Combine(paths.Root, "agent-state"),
                ["XDG_CACHE_HOME"] = Path.Combine(paths.Root, "agent-cache"), ["XDG_CONFIG_HOME"] = Path.Combine(paths.Root, "agent-config"),
                ["OPENCODE_CONFIG_CONTENT"] = AgentConfiguration.Build(ModelPort, modelKey, instruction, settings.ContextTokens, agentTuning, agentModelName),
                ["OPENCODE_SERVER_PASSWORD"] = AgentPassword, ["OPENCODE_SERVER_USERNAME"] = "opencode",
                ["OPENCODE_DISABLE_AUTOUPDATE"] = "true", ["OPENCODE_DISABLE_LSP_DOWNLOAD"] = "true", ["OPENCODE_DISABLE_DEFAULT_PLUGINS"] = "true",
                ["OPENCODE_DISABLE_SHARE"] = "true", ["DO_NOT_TRACK"] = "1", ["OTEL_SDK_DISABLED"] = "true",
                ["PATH"] = Path.GetDirectoryName(paths.GitExe) + ";" + Path.GetDirectoryName(paths.NodeExe) + ";" + Environment.GetEnvironmentVariable("PATH")
            };
            agent = OwnedProcess.Start(paths.AgentExe, ["serve", "--hostname", "127.0.0.1", "--port", AgentPort.ToString(), "--pure"], selectedProject, NewLog("agente"), environment);
            var auth = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("opencode:" + AgentPassword));
            await agent.WaitForReadyAsync(client, new($"http://127.0.0.1:{AgentPort}/global/health"), auth, TimeSpan.FromMinutes(1), ct);
            project = selectedProject; settings.LastProject = project; paths.SaveSettings(settings);
            SetState(RuntimeState.Ready, "IA lista. Podés trabajar con los archivos y Git de tu proyecto.");
            return AgentConfiguration.ProjectUrl(AgentPort, project);
        }
        catch (Exception ex)
        {
            await StopOwnedAsync();
            SetState(ex is OperationCanceledException ? RuntimeState.Stopped : RuntimeState.Error, ex is OperationCanceledException ? "Inicio cancelado." : ex.Message);
            throw;
        }
        finally { gate.Release(); }
    }
    public async Task StopAsync()
    {
        await gate.WaitAsync();
        try { SetState(RuntimeState.Stopping, "Deteniendo la IA y liberando la GPU..."); await StopOwnedAsync(); SetState(RuntimeState.Stopped, "IA detenida. Tus chats y archivos siguen guardados."); }
        finally { gate.Release(); }
    }
    private async Task StopOwnedAsync()
    {
        var previousAgent = agent; agent = null;
        var previousModel = model; model = null;
        try { if (previousAgent is not null) await previousAgent.DisposeAsync(); }
        finally { if (previousModel is not null) await previousModel.DisposeAsync(); }
        AgentPassword = "";
    }
    public bool ProcessesHealthy => State == RuntimeState.Ready && model is { HasExited: false } && agent is { HasExited: false };
    public static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    private string NewLog(string name) => Path.Combine(paths.Logs, name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    public static string[] EngineArguments(string modelPath, int device, int port, string key, int contextTokens = ReleaseProfile.Context, EngineTuning? tuning = null)
    {
        tuning ??= new(); tuning.Validate(contextTokens);
        string N(object value) => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
        string[] arguments = [modelPath, "--host", "127.0.0.1", "--port", N(port), "--api-key", key, "--device", N(device), "--model-id", "qwen-local", "--max-context", N(contextTokens), "--max-concurrency", "1", "--kvmem-budget", N(tuning.HistoryTokens), "--kvmem-gen-reserve", N(tuning.GenerationReserve), "--kvmem-host-mib", N(tuning.EffectiveHostMiB(contextTokens)), "--kvmem-sessions", "1", "--kvmem-sink-tokens", N(tuning.SinkTokens), "--kvmem-recent-tokens", N(tuning.RecentTokens), "--kv-dtype", tuning.KvType, "--gdn-state-fp16", "--prefill-chunk", N(tuning.PrefillChunk), "--spec", "mtp", "--draft-tokens", N(tuning.DraftTokens), "--ngram-draft-tokens", N(tuning.NgramDraftTokens), "--ngram-min-match", N(tuning.NgramMinMatch), "--device-profile", "off", "--default-thinking-budget", N(Math.Max(1, tuning.ThinkingTokens)), "--default-max-tokens", N(tuning.MaxOutput), "--temperature", N(tuning.Temperature), "--top-p", N(tuning.TopP), "--top-k", N(tuning.TopK), "--min-p", N(tuning.MinP)];
        return tuning.ThinkingTokens == 0 ? [.. arguments, "--no-thinking"] : arguments;
    }
    public static string[] Ninfer5080Arguments(string modelPath, int port, string apiKey, int contextTokens = Ninfer5080Profile.DefaultContext, bool vision = false, int device = 0, int automaticPrefill = Ninfer5080Profile.PrefillChunk, NativeEngineTuning? tuning = null)
    {
        tuning ??= new(); tuning.Validate(contextTokens);
        string N(object value) => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
        var arguments = new List<string>
        {
            modelPath, "--host", "127.0.0.1", "--port", N(port), "--api-key", apiKey, "--device", N(device), "--model-id", Ninfer5080Profile.ModelId,
            "--max-context", N(contextTokens), "--kv-capacity", N(contextTokens), "--kv-dtype", Ninfer5080Profile.KvDtype,
            "--spec", "mtp", "--draft-tokens", N(tuning.DraftTokens), "--prefill-chunk", N(tuning.EffectivePrefill(automaticPrefill)),
            "--embedding-host", "--default-thinking-budget", N(Math.Max(1,tuning.ThinkingTokens)), "--default-max-tokens", N(tuning.MaxOutput),
            "--prefix-checkpoint-policy", Ninfer5080Profile.CheckpointPolicy, "--max-concurrency", "1",
            "--max-pending-requests", N(Ninfer5080Profile.MaxPendingRequests), "--pending-timeout-ms", N(Ninfer5080Profile.PendingTimeoutMs)
        };
        if (vision) arguments.AddRange(["--vision", "--vision-max-tokens", N(tuning.VisionMaxTokens)]);
        if (!tuning.CudaGraph) arguments.Add("--no-cuda-graph");
        if (tuning.ThinkingTokens == 0) arguments.Add("--no-thinking");
        return [.. arguments];
    }
    public async ValueTask DisposeAsync() { await StopAsync(); gate.Dispose(); }
}
