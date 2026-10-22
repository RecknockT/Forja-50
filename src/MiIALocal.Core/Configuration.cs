using System.Text.Json;

namespace MiIALocal.Core;

public sealed record DownloadAsset(string Id, string Name, string Url, string Sha256, string FileName, long? Bytes = null);

/// <summary>Motor con el que se atiende la inferencia. Cada modo usa su propio binario, modelo y argumentos.</summary>
public enum EngineMode { KvMem, NInfer5080 }
public enum AppMode { Programming, Visual }

public static class ReleaseProfile
{
    public const string Version = "0.4.15-dev";
    public const string ModelName = "Qwen 27B · Swift 1.5 IQ3_S";
    public const int Context = 65536;
    public const int MaxOutput = 8192;
    public static readonly DownloadAsset Model = new("model", "Modelo Qwen (12,8 GB)", "https://huggingface.co/fyb1214/Swift-1.5-Qwen3.8-27B-GSQ-RCO-NInfer/resolve/7da3e38b9ee7a6c8888cf2112d3c5a48a952a2a8/swift15_iq3s_mtp.ninfer", "0e1771e4df425a7e05b0ffce8662cb500dc2546f3759767fe6ac2699d64c0816", "swift15_iq3s_mtp.ninfer", 12790639360);
    // Artefacto oficial del proyecto ninfer-5080 (Todd Ballinger) en Hugging Face, fijado a una revisión inmutable.
    public static readonly DownloadAsset NinferModel = new("ninfer5080-model", "Modelo Qwen3.8-27B · NInfer-5080 (16,5 GB)", "https://huggingface.co/ninfer-5080/Qwen3.8-27B-RTX5080/resolve/e23a3301eed611ac3196e3389c3d572bc5ddeb9a/qwen3_8_27b.ninfer", "c4a7e9ab593a7f42d58208fa0065d67a82d61921107686cc9f6ed1ec6b050e21", "qwen3_8_27b.ninfer", 16461267456);
    public static readonly DownloadAsset Engine = new("ninfer", "Motor NInfer / KVMem", "https://github.com/kvmem/kvmem/releases/download/v0.18.0/kvmem-v0.18.0-ninfer-windows-rtx50-sm120a.zip", "f893dc9053cb27ef8cdbc32687c6bb29346d5829d5eb2272a58ddef7b6dbdf7d", "kvmem-0.18.0-rtx50.zip", 900767487);
    public static readonly DownloadAsset Engine3090 = new("ninfer-rtx30", "Motor NInfer / KVMem · RTX 3090", "https://github.com/kvmem/kvmem/releases/download/v0.18.0/kvmem-v0.18.0-ninfer-windows-rtx30-sm86.zip", "1f4bab96cde5f5a80eb7d46f074cca5f8a9294121ed53570b0c422a05eb6e301", "kvmem-0.18.0-rtx30.zip", 1018841562);
    public static readonly DownloadAsset NativeEngine = new("ninfer5080", "Motor NInfer nativo Windows", "https://github.com/RecknockT/Forja-50/releases/download/ninfer-native-aee9106/ninfer-native-windows-sm120a-aee9106.zip", "db7b60c45f880dfdfed92a964270759b02ffb3b9e94817127593d49d1ff80524", "ninfer-native-windows-sm120a-aee9106.zip", 243616338);
    public static readonly DownloadAsset Agent = new("opencode", "Agente OpenCode", "https://github.com/anomalyco/opencode/releases/download/v1.19.3/opencode-windows-x64.zip", "e4ce9117f55fc068414513789f8c7d1049f855d7518bb4e350b9170a3a20d8c3", "opencode-1.19.3.zip", 62204138);
    public static readonly DownloadAsset Git = new("git", "Git portable", "https://github.com/git-for-windows/git/releases/download/v2.53.0.windows.3/MinGit-2.53.0.3-64-bit.zip", "0d7c85a26e45668b35d0d0aeb763289376cfc039e55e0938a617ed0dfa32e433", "mingit-2.53.0.3.zip", 41300221);
    public static readonly DownloadAsset GitHubCredentialManager = new("github-gcm", "Inicio de sesión de GitHub", "https://github.com/git-ecosystem/git-credential-manager/releases/download/v3.0.1/gcm-win-x64-3.0.1.zip", "bc51abd9e613a856f909b5ea9244b5a265a4fc8a85383e2982d76034d80cfe3f", "gcm-win-x64-3.0.1.zip", 20608990);
    public static readonly DownloadAsset Node = new("node", "Node y npm portables", "https://nodejs.org/dist/v24.19.0/node-v24.19.0-win-x64.zip", "57f71ab3652e797d84acddc79c81cc9ff1c6ddb2a1974cdb83f00fee9bff4c73", "node-24.19.0.zip");
    public static readonly DownloadAsset[] Components = [Engine, Agent, Git, Node];
    public static DownloadAsset[] ComponentsFor(EngineMode mode) => mode == EngineMode.NInfer5080 ? [NativeEngine, Agent, Git, Node] : Components;
    public static DownloadAsset KvMemEngineFor(AppSettings settings) => settings.HardwareProfileId == GpuProfiles.Rtx3090Id ? Engine3090 : Engine;
    public static DownloadAsset[] ComponentsFor(AppSettings settings)
    {
        GpuProfiles.ValidateBackend(settings);
        return settings.EngineMode == EngineMode.NInfer5080 ? ComponentsFor(settings.EngineMode) : [KvMemEngineFor(settings), Agent, Git, Node];
    }
    public static DownloadAsset ModelFor(EngineMode mode) => mode == EngineMode.NInfer5080 ? NinferModel : Model;
}

public sealed class AppSettings
{
    public AppMode Mode { get; set; }
    public VisualSettings Visual { get; set; } = new();
    public int ContextTokens { get; set; } = ReleaseProfile.Context;
    public EngineTuning Tuning { get; set; } = new();
    public bool UseRecommendedProfile { get; set; } = true;
    public string HardwareProfileId { get; set; } = "";
    public string LastProject { get; set; } = "";
    public string ModelDirectory { get; set; } = "models";
    public string ImportedModel { get; set; } = "";
    public bool LicenseAccepted { get; set; }
    public EngineMode EngineMode { get; set; } = EngineMode.NInfer5080;
    public NativeEngineTuning NativeTuning { get; set; } = new();
    public string NinferEnginePath { get; set; } = "";
    public string ImportedNinferModel { get; set; } = "";
    public bool NinferVision { get; set; }
}

public sealed record ContextProfile(int Tokens, int HostCacheMiB)
{
    public const int HistoryBudget = 24576;
    public const int GenerationReserve = 8192;
    public static ContextProfile For(int tokens) => tokens switch
    {
        32768 => new(32768, 2048),
        65536 => new(65536, 4096),
        131072 => new(131072, 8192),
        _ => throw new InvalidDataException("Contexto KVMem no admitido. Elegí 32K, 64K o 128K según la placa.")
    };
}

public sealed class AppPaths
{
    public string Root { get; }
    public string Runtime => Path.Combine(Root, "runtime");
    public string Downloads => Path.Combine(Root, "downloads");
    public string Logs => Path.Combine(Root, "logs");
    public string VisualRoot => Path.Combine(Root, "visual");
    public string VisualModels => Path.Combine(VisualRoot, "models");
    public string VisualInputs => Path.Combine(VisualRoot, "inputs");
    public string VisualOutputs => Path.Combine(VisualRoot, "creations");
    public string VisualJobs => Path.Combine(VisualRoot, "jobs");
    public string ComfyRoot => Path.Combine(Runtime, "comfy", "ComfyUI_windows_portable");
    public string ComfyPython => Path.Combine(ComfyRoot, "python_embeded", "python.exe");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string? SettingsRecoveryMessage { get; private set; }
    public string AgentConfig => Path.Combine(Root, "opencode.json");
    public string EngineExe => Path.Combine(Runtime, "ninfer", "kvmem-v0.18.0-ninfer-windows-rtx50-sm120a", "workers", "ninfer", "ninfer-kvmem-server.exe");
    public string KvMemEngineExe(AppSettings settings) => ReleaseProfile.KvMemEngineFor(settings) == ReleaseProfile.Engine3090
        ? Path.Combine(Runtime, ReleaseProfile.Engine3090.Id, "kvmem-v0.18.0-ninfer-windows-rtx30-sm86", "workers", "ninfer", "ninfer-kvmem-server.exe") : EngineExe;
    public string AgentExe => Path.Combine(Runtime, "opencode", "opencode.exe");
    public string GitExe => Path.Combine(Runtime, "git", "cmd", "git.exe");
    public string GitHubCredentialExe => Path.Combine(Runtime, ReleaseProfile.GitHubCredentialManager.Id, "git-credential-manager.exe");
    public string GitHubCredentialStore => Path.Combine(Root, "github-auth");
    public string GitHubAccountFile => Path.Combine(Root, "github-account.json");
    public string NodeExe => Path.Combine(Runtime, "node", "node-v24.19.0-win-x64", "node.exe");
    public AppPaths(string? root = null)
    {
        Root = Path.GetFullPath(root ?? DefaultDataRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        foreach (var dir in new[] { Root, Runtime, Downloads, Logs }) Directory.CreateDirectory(dir);
    }
    public static string DefaultDataRoot(string localApplicationData)
    {
        // Keep existing downloads, credentials and absolute paths intact on upgrade.
        var legacy = Path.Combine(localApplicationData, "MiIALocal");
        return Directory.Exists(legacy) ? legacy : Path.Combine(localApplicationData, "Forja 50");
    }
    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsFile)) return new();
        try
        {
            var text = File.ReadAllText(SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(text, JsonOptions) ?? throw new JsonException("Ajustes vacíos.");
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.EnumerateObject().Any(p => p.Name.Equals(nameof(AppSettings.UseRecommendedProfile), StringComparison.OrdinalIgnoreCase))) settings.UseRecommendedProfile = false;
            // Preserve the old backend and manual tuning when loading a pre-selector installation.
            if (!document.RootElement.EnumerateObject().Any(p => p.Name.Equals(nameof(AppSettings.EngineMode), StringComparison.OrdinalIgnoreCase))) settings.EngineMode = EngineMode.KvMem;
            settings.Tuning ??= new();
            settings.Visual ??= new();
            if (!Enum.IsDefined(settings.Mode)) settings.Mode = AppMode.Programming;
            settings.Visual.Normalize();
            settings.NativeTuning ??= new();
            if (!Enum.IsDefined(settings.EngineMode)) throw new InvalidDataException("Motor desconocido.");
            if (settings.EngineMode == EngineMode.NInfer5080)
            {
                if (settings.ContextTokens is not (65536 or 131072)) settings.ContextTokens = Ninfer5080Profile.DefaultContext;
                settings.NativeTuning.Validate(settings.ContextTokens);
            }
            else settings.Tuning.Validate(settings.ContextTokens);
            if (string.IsNullOrWhiteSpace(settings.ModelDirectory)) settings.ModelDirectory = "models";
            _ = ModelDirectoryPath(settings);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException)
        {
            var backup = Path.Combine(Root, "settings.invalid-" + Guid.NewGuid().ToString("N") + ".json");
            File.Move(SettingsFile, backup);
            SettingsRecoveryMessage = "Se restauraron los ajustes recomendados porque el archivo anterior no era válido. La copia anterior está en " + backup;
            var defaults = new AppSettings(); SaveSettings(defaults); return defaults;
        }
    }
    public void SaveSettings(AppSettings settings) => AtomicWrite(SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
    public string ModelDirectoryPath(AppSettings settings) => Path.GetFullPath(string.IsNullOrWhiteSpace(settings.ModelDirectory) ? "models" : settings.ModelDirectory, Root);
    public string ModelPath(AppSettings settings) => string.IsNullOrWhiteSpace(settings.ImportedModel) ? Path.Combine(ModelDirectoryPath(settings), ReleaseProfile.Model.FileName) : Path.GetFullPath(settings.ImportedModel, Root);
    // El modo nativo mantiene su modelo y motor separados del backend anterior.
    public string NinferModelPath(AppSettings settings) => string.IsNullOrWhiteSpace(settings.ImportedNinferModel) ? Path.Combine(ModelDirectoryPath(settings), ReleaseProfile.NinferModel.FileName) : Path.GetFullPath(settings.ImportedNinferModel, Root);
    public string ActiveModelPath(AppSettings settings) => settings.EngineMode == EngineMode.NInfer5080 ? NinferModelPath(settings) : ModelPath(settings);
    public string ImportedModelPath(AppSettings settings) => settings.EngineMode == EngineMode.NInfer5080 ? settings.ImportedNinferModel : settings.ImportedModel;
    public string NinferEngineExe(AppSettings settings) => string.IsNullOrWhiteSpace(settings.NinferEnginePath) ? Path.Combine(Runtime, "ninfer5080", "ninfer-serve.exe") : Path.GetFullPath(settings.NinferEnginePath, Root);
    public bool HasNinferEngine(AppSettings settings) => File.Exists(NinferEngineExe(settings));
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static void AtomicWrite(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}
