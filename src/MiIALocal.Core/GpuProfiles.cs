using System.Text.Json;

namespace MiIALocal.Core;

public sealed record GpuPreset(string Id, string DisplayName, int ContextTokens, bool BasicContextProbeValidated)
{
    // Independent factories allow adjusting each board after physical measurements.
    public EngineTuning CreateTuning(int? context = null) => Id switch
    {
        "rtx5080-desktop-16gb" => new(),
        "rtx5070ti-desktop-16gb" => new(),
        GpuProfiles.Rtx3090Id => new()
        {
            HistoryTokens = (context ?? ContextTokens) == 131072 ? 98304 : (context ?? ContextTokens) == 65536 ? 49152 : 24576,
            GenerationReserve = (context ?? ContextTokens) == 32768 ? 8192 : 16384,
            PrefillChunk = 512
        },
        _ => throw new InvalidDataException("Perfil de placa desconocido.")
    };
    public bool Matches(int context, EngineTuning tuning) => context == ContextTokens && JsonSerializer.Serialize(tuning) == JsonSerializer.Serialize(CreateTuning());
    public bool MatchesTuning(int context, EngineTuning tuning) => JsonSerializer.Serialize(tuning) == JsonSerializer.Serialize(CreateTuning(context));
}

public static class GpuProfiles
{
    public const string Rtx3090Id = "rtx3090-desktop-24gb";
    public static bool Is3090(AppSettings settings) => settings.HardwareProfileId == Rtx3090Id;
    public static int[] KvMemContexts(AppSettings settings) => Is3090(settings) ? [32768, 65536, 131072] : [32768, 65536];
    public static void ValidateBackend(AppSettings settings)
    {
        if (Is3090(settings) && settings.EngineMode != EngineMode.KvMem)
            throw new InvalidDataException("La RTX 3090 requiere KVMem para RTX 30. El motor NInfer-5080 es exclusivo de RTX 50.");
        if (settings.EngineMode == EngineMode.KvMem && !KvMemContexts(settings).Contains(settings.ContextTokens))
            throw new InvalidDataException("Ese contexto KVMem no está habilitado para esta placa. Restaurá el perfil recomendado.");
    }
    public static GpuPreset For(GpuInfo gpu)
    {
        if (!gpu.HasRequiredMemory) throw new InvalidDataException("El perfil requiere 16 GB de VRAM en RTX 50 o 24 GB en RTX 3090.");
        return gpu.Name switch
        {
            "NVIDIA GeForce RTX 5080" => new("rtx5080-desktop-16gb", "RTX 5080 · escritorio", 65536, true),
            "NVIDIA GeForce RTX 5070 Ti" => new("rtx5070ti-desktop-16gb", "RTX 5070 Ti · escritorio", 65536, false),
            "NVIDIA GeForce RTX 3090" => new(Rtx3090Id, "RTX 3090 · escritorio", 131072, false),
            _ => throw new InvalidDataException("Esta versión solo admite RTX 5070 Ti, RTX 5080 y RTX 3090 de escritorio.")
        };
    }
    public static GpuPreset Apply(AppSettings settings, GpuInfo gpu)
    {
        var preset = For(gpu);
        if (gpu.IsRtx3090 && settings.EngineMode != EngineMode.KvMem)
        {
            settings.EngineMode = EngineMode.KvMem;
            settings.LicenseAccepted = false;
        }
        if (settings.UseRecommendedProfile && settings.EngineMode == EngineMode.KvMem) { settings.ContextTokens = preset.ContextTokens; settings.Tuning = preset.CreateTuning(); }
        settings.HardwareProfileId = preset.Id;
        return preset;
    }
    public static int MinimumFreeMiB(AppSettings settings, GpuInfo gpu) => settings.EngineMode == EngineMode.NInfer5080
        ? (settings.ContextTokens == 131072 ? 15500 : Ninfer5080Profile.For(gpu).MinimumFreeMiB)
        : gpu.IsRtx3090 && settings.ContextTokens == 131072 ? 20000 : gpu.IsRtx3090 && settings.ContextTokens == 65536 ? 17000 : 13000;
}
