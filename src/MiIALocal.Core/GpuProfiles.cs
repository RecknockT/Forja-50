using System.Text.Json;

namespace MiIALocal.Core;

public sealed record GpuPreset(string Id, string DisplayName, int ContextTokens, bool BasicContextProbeValidated)
{
    // Independent factories allow adjusting each board after physical measurements.
    public EngineTuning CreateTuning() => Id switch
    {
        "rtx5080-desktop-16gb" => new(),
        "rtx5070ti-desktop-16gb" => new(),
        _ => throw new InvalidDataException("Perfil de placa desconocido.")
    };
    public bool Matches(int context, EngineTuning tuning) => context == ContextTokens && JsonSerializer.Serialize(tuning) == JsonSerializer.Serialize(CreateTuning());
}

public static class GpuProfiles
{
    public static GpuPreset For(GpuInfo gpu)
    {
        if (gpu.TotalMiB < 15000) throw new InvalidDataException("El perfil requiere 16 GB de VRAM.");
        return gpu.Name switch
        {
            "NVIDIA GeForce RTX 5080" => new("rtx5080-desktop-16gb", "RTX 5080 · escritorio", 65536, true),
            "NVIDIA GeForce RTX 5070 Ti" => new("rtx5070ti-desktop-16gb", "RTX 5070 Ti · escritorio", 65536, false),
            _ => throw new InvalidDataException("Esta versión solo admite RTX 5070 Ti y RTX 5080 de escritorio.")
        };
    }
    public static GpuPreset Apply(AppSettings settings, GpuInfo gpu)
    {
        var preset = For(gpu);
        if (settings.UseRecommendedProfile) { settings.ContextTokens = preset.ContextTokens; settings.Tuning = preset.CreateTuning(); }
        settings.HardwareProfileId = preset.Id;
        return preset;
    }
}
