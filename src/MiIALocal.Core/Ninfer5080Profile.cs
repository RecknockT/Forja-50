namespace MiIALocal.Core;

/// <summary>
/// Port Windows/MSVC de YukinoKaorisuna sobre ninfer-5080 de Todd Ballinger.
/// El artefacto coincide con el oficial; la revisión Windows no es el runtime Linux v1.5.
/// La compilación no equivale a validar inferencia en las dos placas.
/// </summary>
public static class Ninfer5080Profile
{
    public const string DisplayName = "NInfer-5080 (Todd Ballinger)";
    public const string ModelId = "qwen-local";
    public const int DefaultContext = 65536;
    public const int MaxContext = 131072;
    public const int DraftTokens = 3;
    public const int PrefillChunk = 896;
    public const int VisionMaxTokens = 2048;
    public const int ThinkingBudget = 2048;
    public const int MaxOutput = 8192;
    public const int MaxPendingRequests = 16;
    public const int PendingTimeoutMs = 180000;
    public const string KvDtype = "q4";
    public const string CheckpointPolicy = "rolling-tool";
    public const string ProduceUrl = "https://github.com/toddballinger/ninfer-5080";
    public const string WindowsSourceCommit = "aee91065f4d3635a07d681800f87829e18f0e227";

    public static NativeGpuPreset For(GpuInfo gpu)
    {
        _ = GpuProfiles.For(gpu);
        return gpu.Name == "NVIDIA GeForce RTX 5080"
            ? new("native-rtx5080", "RTX 5080 · Windows nativo", 896, 14000)
            : new("native-rtx5070ti", "RTX 5070 Ti · Windows nativo", 512, 14000);
    }

    public static int[] Contexts => [DefaultContext, MaxContext];

    public static void Validate(int context)
    {
        if (context is not (DefaultContext or MaxContext)) throw new InvalidDataException("El modo NInfer-5080 admite contexto 64K o 128K.");
    }

    /// <summary>Identidad de la configuración cargada; un cambio aquí obliga a recargar el motor.</summary>
    public static string Signature(int context, bool vision) =>
        $"{context}:{KvDtype}:{DraftTokens}:{PrefillChunk}:{VisionMaxTokens}:{ThinkingBudget}:{MaxPendingRequests}:{CheckpointPolicy}:{vision}";

    /// <summary>Aviso para placas donde el contexto completo no está validado. null cuando no corresponde.</summary>
    public static string? WarningFor(string gpuName, int context) =>
        context == MaxContext
            ? $"128K es experimental en Windows nativo ({gpuName}). Puede no entrar en la memoria libre; todavía no validamos el contexto completo. Usá 64K como punto de partida."
            : null;
}

public sealed record NativeGpuPreset(string Id, string DisplayName, int PrefillChunk, int MinimumFreeMiB);

public sealed class NativeEngineTuning
{
    public int PrefillChunk { get; set; } // 0 = perfil automático por placa.
    public int ThinkingTokens { get; set; } = 2048;
    public int MaxOutput { get; set; } = 8192;
    public int DraftTokens { get; set; } = 3;
    public int VisionMaxTokens { get; set; } = 2048;
    public bool CudaGraph { get; set; } = true;
    public double AgentTemperature { get; set; } = .6;
    public int EffectivePrefill(int automatic) => PrefillChunk == 0 ? automatic : PrefillChunk;
    public void Validate(int context)
    {
        Ninfer5080Profile.Validate(context);
        if (PrefillChunk != 0 && (PrefillChunk is < 64 or > 1792 || PrefillChunk % 64 != 0)) throw new InvalidDataException("Prefill: usá 0 para automático o un múltiplo de 64 entre 64 y 1792.");
        if (MaxOutput is < 256 or > 16384 || ThinkingTokens < 0 || ThinkingTokens >= MaxOutput) throw new InvalidDataException("El razonamiento debe ser menor que la salida máxima (256 a 16384 tokens).");
        if (DraftTokens is < 1 or > 4 || VisionMaxTokens is < 256 or > 2048) throw new InvalidDataException("MTP admite 1 a 4 borradores y visión 256 a 2048 tokens.");
        if (!double.IsFinite(AgentTemperature) || AgentTemperature is < 0 or > 2) throw new InvalidDataException("La temperatura debe estar entre 0 y 2.");
    }
}
