using System.Text.Json;

namespace MiIALocal.Core;

public enum VisualTask { Image, Edit, Animation }
public sealed class VisualSettings
{
    public bool LicenseAccepted { get; set; }
    public bool IncludeAnimation { get; set; }
    public bool WebReferencesEnabled { get; set; } = true;
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 1024;
    public int AnimationFrames { get; set; } = 73;
    public int AnimationSteps { get; set; } = 20;
    public void Normalize()
    {
        if (Width is < 512 or > 1024 || Width % 32 != 0) Width = 1024;
        if (Height is < 512 or > 1024 || Height % 32 != 0) Height = 1024;
        if (AnimationFrames is < 25 or > 121 || AnimationFrames % 4 != 1) AnimationFrames = 73;
        if (AnimationSteps is < 10 or > 30) AnimationSteps = 20;
    }
}

public sealed record VisualModel(DownloadAsset Asset, string Folder);
public static class VisualCatalog
{
    public const string ComfyVersion = "0.42.1";
    public static readonly DownloadAsset Runtime = new("comfy", "Motor visual ComfyUI · NVIDIA", "https://github.com/Comfy-Org/ComfyUI/releases/download/v0.42.1/ComfyUI_windows_portable_nvidia.7z", "496afd18790ec685d99ebba2edf200d7371a29fbdd3a992bb0f4f52f244a6874", "ComfyUI-0.42.1-nvidia.7z", 2018444114);
    private static VisualModel Model(string id, string title, string repo, string revision, string file, long bytes, string sha)
        => new(new(id, title, $"https://huggingface.co/{repo}/resolve/{revision}/split_files/{file}", sha, Path.GetFileName(file), bytes), file.Split('/')[0]);
    public static readonly VisualModel Flux = Model("visual-flux", "FLUX.2 Klein 4B · imágenes", "Comfy-Org/flux2-klein-4B", "5f526678002e43af5551dadb73ce2e8c91b43afe", "diffusion_models/flux-2-klein-4b.safetensors", 7751105712, "ec3d4e733a771f61c052fb4856c48b336c55eaf2c65487c2a1faeb9bbda7a343");
    public static readonly VisualModel FluxText = Model("visual-flux-text", "Codificador de imágenes", "Comfy-Org/flux2-klein-4B", "5f526678002e43af5551dadb73ce2e8c91b43afe", "text_encoders/qwen_3_4b.safetensors", 8044982048, "6c671498573ac2f7a5501502ccce8d2b08ea6ca2f661c458e708f36b36edfc5a");
    public static readonly VisualModel FluxVae = Model("visual-flux-vae", "Decodificador de imágenes", "Comfy-Org/flux2-klein-4B", "5f526678002e43af5551dadb73ce2e8c91b43afe", "vae/flux2-vae.safetensors", 336211292, "868fe7b343cc8f3a19dbcfcafbc3d5f888802be3f89bd81b65b3621a066ce8f3");
    public static readonly VisualModel Wan = Model("visual-wan", "Wan 2.2 5B · animación", "Comfy-Org/Wan_2.2_ComfyUI_Repackaged", "ee6f4a40737a995bf5818954cfce6d59443b0f04", "diffusion_models/wan2.2_ti2v_5B_fp16.safetensors", 9999658848, "456f901338bd9eadbded3828b819109a9b68e8a525ca5cf8d0049a69fcfeca1e");
    public static readonly VisualModel WanText = Model("visual-wan-text", "Codificador de animación", "Comfy-Org/Wan_2.2_ComfyUI_Repackaged", "ee6f4a40737a995bf5818954cfce6d59443b0f04", "text_encoders/umt5_xxl_fp8_e4m3fn_scaled.safetensors", 6735906897, "c3355d30191f1f066b26d93fba017ae9809dce6c627dda5f6a66eaa651204f68");
    public static readonly VisualModel WanVae = Model("visual-wan-vae", "Decodificador de animación", "Comfy-Org/Wan_2.2_ComfyUI_Repackaged", "ee6f4a40737a995bf5818954cfce6d59443b0f04", "vae/wan2.2_vae.safetensors", 1409400960, "e40321bd36b9709991dae2530eb4ac303dd168276980d3e9bc4b6e2b75fed156");
    public static VisualModel[] Images => [Flux, FluxText, FluxVae];
    public static VisualModel[] Animations => [Wan, WanText, WanVae];
    public static VisualModel[] All => [.. Images, .. Animations];
    public static VisualModel[] For(bool animation) => animation ? All : Images;
    public static string PathFor(AppPaths paths, VisualModel model) => Path.Combine(paths.VisualModels, model.Folder, model.Asset.FileName);
    public static long DownloadBytes(bool animation) => Runtime.Bytes!.Value + For(animation).Sum(m => m.Asset.Bytes!.Value);
}

public sealed record VisualRequest(VisualTask Task, string Prompt, int Width, int Height, long Seed, int Frames = 73, int Steps = 20)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Task) || string.IsNullOrWhiteSpace(Prompt) || Prompt.Length > 6000) throw new InvalidDataException("Escribí una descripción de hasta 6000 caracteres.");
        var maximum = Task == VisualTask.Animation ? 832 : 1024;
        if (Width < 256 || Height < 256 || Width > maximum || Height > maximum || Width % 32 != 0 || Height % 32 != 0)
            throw new InvalidDataException("La resolución supera el perfil visual o no es múltiplo de 32.");
        if (Seed < 0 || Seed > 9007199254740991L) throw new InvalidDataException("Semilla fuera de rango.");
        if (Frames is < 25 or > 121 || Frames % 4 != 1 || Steps is < 10 or > 30) throw new InvalidDataException("Duración o pasos de animación fuera del perfil.");
    }
}

public sealed class VisualJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public string Status { get; set; } = "Preparando";
    public string? Error { get; set; }
    public VisualRequest Request { get; set; } = new(VisualTask.Image, "", 1024, 1024, 0);
    public string? Reference { get; set; }
    public VisualReference? OnlineReference { get; set; }
    public string[] Files { get; set; } = [];
    public string WorkflowVersion { get; set; } = "forja-visual-2 / ComfyUI " + VisualCatalog.ComfyVersion;
}

public sealed class VisualJobStore(AppPaths paths)
{
    public void Save(VisualJob job)
    {
        if (job.Id.Length != 32 || !job.Id.All(Uri.IsHexDigit)) throw new InvalidDataException("Identificador de trabajo inválido.");
        var document = JsonSerializer.SerializeToNode(job, AppPaths.JsonOptions)!;
        foreach (var file in job.Files) if (!StorageCleanup.Within(Path.GetFullPath(file), paths.VisualOutputs)) throw new InvalidDataException("Resultado fuera de la carpeta visual.");
        document[nameof(VisualJob.Files)] = JsonSerializer.SerializeToNode(job.Files.Select(file => Path.GetRelativePath(paths.VisualOutputs, file)).ToArray());
        AppPaths.AtomicWrite(Path.Combine(paths.VisualJobs, job.Id + ".json"), document.ToJsonString(AppPaths.JsonOptions));
    }
    public VisualJob[] Read()
    {
        if (!Directory.Exists(paths.VisualJobs)) return [];
        var jobs = new List<VisualJob>();
        foreach (var file in Directory.EnumerateFiles(paths.VisualJobs, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<VisualJob>(File.ReadAllText(file), AppPaths.JsonOptions) is { Id: not null, Request.Prompt: not null, Files: not null } job
                    && job.Id.Length == 32 && job.Id.All(Uri.IsHexDigit) && Path.GetFileNameWithoutExtension(file).Equals(job.Id, StringComparison.OrdinalIgnoreCase))
                { job.Files = job.Files.Select(f => SafeArchive.ResolveEntry(paths.VisualOutputs, f)).ToArray(); jobs.Add(job); }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException) { }
        }
        return jobs.OrderByDescending(j => j.Created).ToArray();
    }
    public void RecoverInterrupted()
    {
        foreach (var job in Read().Where(j => j.Status is "Preparando" or "Generando")) { job.Status = "Interrumpido"; Save(job); }
    }
}
