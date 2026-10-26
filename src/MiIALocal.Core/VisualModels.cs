using System.Text.Json;

namespace MiIALocal.Core;

public enum VisualTask { Image, Edit, Animation }
public sealed class VisualSettings
{
    public bool LicenseAccepted { get; set; }
    public bool HiDreamLicenseAccepted { get; set; }
    public bool EnhancePrompt { get; set; } = true;
    public bool ReviewPreparedPrompt { get; set; } = true;
    /// <summary>Retained for old settings; the only active image model is now Full.</summary>
    public bool FullEdit { get; set; } = true;
    /// <summary>Retained for old settings; the only active assistant is now Gemma 4 12B.</summary>
    public bool LargeAssistant { get; set; } = true;
    public bool IncludeAnimation { get; set; }
    public bool AnimationSol { get; set; } = true;
    public bool WebReferencesEnabled { get; set; } = true;
    public int Width { get; set; } = 2048;
    public int Height { get; set; } = 2048;
    public int AnimationFrames { get; set; } = 73;
    public int AnimationSteps { get; set; } = 4;
    public void Normalize()
    {
        if (Width is < 512 or > 2048 || Width % 32 != 0) Width = 1024;
        if (Height is < 512 or > 2048 || Height % 32 != 0) Height = 1024;
        if (AnimationFrames is < 25 or > 121 || AnimationFrames % 4 != 1) AnimationFrames = 73;
        AnimationSteps = 4;
        FullEdit = true; LargeAssistant = true;
    }
}

public sealed record VisualModel(DownloadAsset Asset, string Folder);
public static class VisualCatalog
{
    public const string ComfyVersion = "0.42.1";
    public static readonly DownloadAsset Runtime = new("comfy", "Motor visual ComfyUI · NVIDIA", "https://github.com/Comfy-Org/ComfyUI/releases/download/v0.42.1/ComfyUI_windows_portable_nvidia.7z", "496afd18790ec685d99ebba2edf200d7371a29fbdd3a992bb0f4f52f244a6874", "ComfyUI-0.42.1-nvidia.7z", 2018444114);
    private static VisualModel Model(string id, string title, string repo, string revision, string file, long bytes, string sha)
        => new(new(id, title, $"https://huggingface.co/{repo}/resolve/{revision}/split_files/{file}", sha, Path.GetFileName(file), bytes), file.Split('/')[0]);
    public const string HiDreamRevision = "377ec7124bc46a15736c68cd9e4ad6d7da7a7614";
    public static readonly VisualModel HiDream = new(new("visual-hidream-dev-fp8", "HiDream-O1 Dev · FP8 escalado", $"https://huggingface.co/Comfy-Org/HiDream-O1-Image/resolve/{HiDreamRevision}/checkpoints/hidream_o1_image_dev_fp8_scaled.safetensors", "7cbf53a475e0a13f92f2ec08bcffdb9b9de4305ef3b6f35cdd784d09dcd8d0cc", "hidream_o1_image_dev_fp8_scaled.safetensors", 8067535296), "checkpoints");
    public static readonly VisualModel HiDreamBf16 = new(new("visual-hidream-dev-bf16", "HiDream-O1 Dev · BF16 para RTX 3090", $"https://huggingface.co/Comfy-Org/HiDream-O1-Image/resolve/{HiDreamRevision}/checkpoints/hidream_o1_image_dev_bf16.safetensors", "cede93912730dd046fd39a20af008344a7dce75dafabd16e419a222c3e504051", "hidream_o1_image_dev_bf16.safetensors", 16365209824), "checkpoints");
    // The model author recommends the Full (undistilled) checkpoint for editing; the Comfy template
    // runs it at 40 steps, CFG 5, ModelNoiseScale 8 and patch seam smoothing.
    public static readonly VisualModel HiDreamFull = new(new("visual-hidream-full-fp8", "HiDream-O1 Full · crear y editar · FP8 escalado", $"https://huggingface.co/Comfy-Org/HiDream-O1-Image/resolve/{HiDreamRevision}/checkpoints/hidream_o1_image_fp8_scaled.safetensors", "05ad98bc4a94557697f31b839f6dbf6dba293a353d9e3c52eef7818b5802d206", "hidream_o1_image_fp8_scaled.safetensors", 8067535296), "checkpoints");
    public static readonly VisualModel PromptAssistant = new(new("visual-gemma4-e4b-int8", "Asistente visual Gemma 4 E4B · INT8", "https://huggingface.co/Comfy-Org/gemma-4/resolve/63d0f7c476756b88910170c1df75e2384ea1af31/text_encoders/gemma4_e4b_it_int8_convrot.safetensors", "974d0c838ef4ac1a989b06ccb4e57691c21b6270dd8e345ffa7531f9388f117c", "gemma4_e4b_it_int8_convrot.safetensors", 8090965702), "text_encoders");
    // Single optional assistant; loaded in its own stage before the generator.
    public static readonly VisualModel PromptAssistant12B = new(new("visual-gemma4-12b-int8", "Asistente visual Gemma 4 12B · INT8", "https://huggingface.co/Comfy-Org/gemma-4/resolve/63d0f7c476756b88910170c1df75e2384ea1af31/text_encoders/gemma4_12b_int8_convrot.safetensors", "bf77dc0b435c487a638909d8f2ccf5a7e4c9838e7bc56545ea6e251a603c5793", "gemma4_12b_int8_convrot.safetensors", 12055234634), "text_encoders");
    public static VisualModel Assistant(bool large) => PromptAssistant12B;
    public static VisualModel Checkpoint(GpuInfo? gpu) => HiDreamFull;
    // Retain legacy downloads in storage management; never delete them as part of migration.
    public static VisualModel[] Legacy => [HiDream, HiDreamBf16, PromptAssistant, LegacyWan, LegacyWanVae,
        Model("visual-flux", "FLUX.2 Klein 4B anterior", "Comfy-Org/flux2-klein-4B", "5f526678002e43af5551dadb73ce2e8c91b43afe", "diffusion_models/flux-2-klein-4b.safetensors", 7751105712, "ec3d4e733a771f61c052fb4856c48b336c55eaf2c65487c2a1faeb9bbda7a343"),
        Model("visual-flux-text", "Codificador Qwen3 4B anterior", "Comfy-Org/flux2-klein-4B", "5f526678002e43af5551dadb73ce2e8c91b43afe", "text_encoders/qwen_3_4b.safetensors", 8044982048, "6c671498573ac2f7a5501502ccce8d2b08ea6ca2f661c458e708f36b36edfc5a")];
    public static readonly VisualModel FluxVae = Model("visual-flux-vae", "Decodificador de imágenes", "Comfy-Org/flux2-klein-4B", "5f526678002e43af5551dadb73ce2e8c91b43afe", "vae/flux2-vae.safetensors", 336211292, "868fe7b343cc8f3a19dbcfcafbc3d5f888802be3f89bd81b65b3621a066ce8f3");
    public static readonly VisualModel LegacyWan = Model("visual-wan", "Wan 2.2 5B · animación", "Comfy-Org/Wan_2.2_ComfyUI_Repackaged", "ee6f4a40737a995bf5818954cfce6d59443b0f04", "diffusion_models/wan2.2_ti2v_5B_fp16.safetensors", 9999658848, "456f901338bd9eadbded3828b819109a9b68e8a525ca5cf8d0049a69fcfeca1e");
    public static readonly VisualModel WanText = Model("visual-wan-text", "Codificador de animación", "Comfy-Org/Wan_2.2_ComfyUI_Repackaged", "ee6f4a40737a995bf5818954cfce6d59443b0f04", "text_encoders/umt5_xxl_fp8_e4m3fn_scaled.safetensors", 6735906897, "c3355d30191f1f066b26d93fba017ae9809dce6c627dda5f6a66eaa651204f68");
    public static readonly VisualModel LegacyWanVae = Model("visual-wan-vae", "Decodificador de animación", "Comfy-Org/Wan_2.2_ComfyUI_Repackaged", "ee6f4a40737a995bf5818954cfce6d59443b0f04", "vae/wan2.2_vae.safetensors", 1409400960, "e40321bd36b9709991dae2530eb4ac303dd168276980d3e9bc4b6e2b75fed156");
    public static VisualModel[] Images => [HiDreamFull, PromptAssistant12B];
    public static readonly VisualModel Wan = new(new("visual-wan-14b", "Wan 2.2 A14B NVFP4 · etapa inicial", "https://huggingface.co/lightx2v/LightWan2.2-A14B/resolve/f4b91eba1fd7dc2eb038191e2bca7ed33423032d/Wan2.2-I2V-A14B_NVFP4_Sparse_high_comfy.safetensors", "998934b07f124111b83c3bfcdf9efc2ff67ff531879806e54a1453c14066a5db", "Wan2.2-I2V-A14B_NVFP4_Sparse_high_comfy.safetensors", 8379918824), "diffusion_models");
    public static readonly VisualModel WanLow = new(new("visual-wanlow-14b", "Wan 2.2 A14B NVFP4 · etapa final", "https://huggingface.co/lightx2v/LightWan2.2-A14B/resolve/f4b91eba1fd7dc2eb038191e2bca7ed33423032d/Wan2.2-I2V-A14B_NVFP4_Sparse_low_comfy.safetensors", "a26076d43590941a6526ed52d2b682a9340c81f650ada0751a90e5bdcde5efb7", "Wan2.2-I2V-A14B_NVFP4_Sparse_low_comfy.safetensors", 8379918824), "diffusion_models");
    public static readonly VisualModel WanVae = new(new("visual-wanvae-14b", "Decodificador Wan 2.1 · A14B", "https://huggingface.co/Comfy-Org/Wan-2.2/resolve/ee6f4a40737a995bf5818954cfce6d59443b0f04/split_files/vae/wan_2.1_vae.safetensors", "2fc39d31359a4b0a64f55876d8ff7fa8d780956ae2cb13463b0223e15148976b", "wan_2.1_vae.safetensors", 253815318), "vae");
    public static readonly VisualModel WanGguf = new(new("visual-wangguf", "Wan 2.2 A14B GGUF Q4_K_S · etapa inicial · RTX 3090", "https://huggingface.co/QuantStack/Wan2.2-I2V-A14B-GGUF/resolve/6c6717459277b9cd1f72579d78a0fd62a79e57dc/HighNoise/Wan2.2-I2V-A14B-HighNoise-Q4_K_S.gguf", "8fc5f726ccf1214e43c6a599bdef1e63daf7fab06e71fb74081416c4b9ce200b", "Wan2.2-I2V-A14B-HighNoise-Q4_K_S.gguf", 8748151296), "diffusion_models");
    public static readonly VisualModel WanGgufLow = new(new("visual-wangguflow", "Wan 2.2 A14B GGUF Q4_K_S · etapa final · RTX 3090", "https://huggingface.co/QuantStack/Wan2.2-I2V-A14B-GGUF/resolve/6c6717459277b9cd1f72579d78a0fd62a79e57dc/LowNoise/Wan2.2-I2V-A14B-LowNoise-Q4_K_S.gguf", "54e2313ef7fbb169e72ab1d7cecdc90a04db01097c86f9f39097b8caacea50fb", "Wan2.2-I2V-A14B-LowNoise-Q4_K_S.gguf", 8748151296), "diffusion_models");
    public static readonly VisualModel WanLora = new(new("visual-wanlora", "LightX2V 4 pasos · etapa inicial", "https://huggingface.co/Comfy-Org/Wan-2.2/resolve/ee6f4a40737a995bf5818954cfce6d59443b0f04/split_files/loras/wan2.2_i2v_lightx2v_4steps_lora_v1_high_noise.safetensors", "d176c808d6fc461999b68e321efcb7501b20b8c3797523ed0df14f7d1deff11e", "wan2.2_i2v_lightx2v_4steps_lora_v1_high_noise.safetensors", 1226977424), "loras");
    public static readonly VisualModel WanLoraLow = new(new("visual-wanloralow", "LightX2V 4 pasos · etapa final", "https://huggingface.co/Comfy-Org/Wan-2.2/resolve/ee6f4a40737a995bf5818954cfce6d59443b0f04/split_files/loras/wan2.2_i2v_lightx2v_4steps_lora_v1_low_noise.safetensors", "024f21de095bc8fad9809ded3e9e49a2e170dcf27075da8145ba7d60d8aab7f9", "wan2.2_i2v_lightx2v_4steps_lora_v1_low_noise.safetensors", 1226977424), "loras");
    public static bool SupportsAnimation(GpuInfo? gpu) => gpu is not null && (gpu.IsRtx3090 || gpu.Name.Contains("5080") || gpu.Name.Contains("5070 Ti"));
    public static VisualModel[] Animations => [Wan, WanLow, WanText, WanVae];
    public static VisualModel[] GgufAnimations => [WanGguf, WanGgufLow, WanLora, WanLoraLow, WanText, WanVae];
    public static VisualModel[] AnimationFor(GpuInfo? gpu) => gpu?.IsRtx3090 == true ? GgufAnimations : Animations;
    public static VisualModel[] All => [.. Images, .. Animations, .. GgufAnimations.Except(Animations), .. Legacy, FluxVae];
    // Old selection arguments remain readable for compatibility. They cannot select
    // retired models or duplicate Full; animation uses its own GPU-specific weights.
    public static VisualModel[] For(bool animation, GpuInfo? gpu = null, bool assistant = true, bool fullEdit = false, bool largeAssistant = false) =>
        [HiDreamFull, .. assistant ? new[] { PromptAssistant12B } : [], .. animation ? AnimationFor(gpu) : []];
    public static string PathFor(AppPaths paths, VisualModel model) => Path.Combine(paths.VisualModels, model.Folder, model.Asset.FileName);
    public static long DownloadBytes(bool animation, GpuInfo? gpu = null, bool assistant = true, bool fullEdit = false, bool largeAssistant = false) =>
        Runtime.Bytes!.Value + For(animation, gpu, assistant, fullEdit, largeAssistant).Sum(m => m.Asset.Bytes!.Value)
        + (animation && gpu?.IsRtx3090 == true ? GgufRuntime.Assets.Sum(a => a.Bytes!.Value) : 0);

}

/// <summary>Assistant caption reviewed by the person plus the staged input it was prepared with.</summary>
public sealed record PreparedPrompt(string Text, string? StagedReference, double PreparationSeconds = 0);

/// <summary>
/// HiDream-O1 is trained at roughly 4 megapixels (2048x2048 and the resolutions listed by its
/// own latent node). A single reference defines the framing, so the canvas takes its aspect
/// ratio snapped to the closest trained resolution: keeping the raw thumbnail size (about
/// 0.6 MP) leaves the model off-distribution and the output comes out as noise.
/// </summary>
public static class VisualSizes
{
    public static readonly (int Width, int Height)[] Trained = [
        (2048, 2048), (2304, 1728), (2304, 1792), (2496, 1664), (2560, 1440), (3104, 1312),
        (1728, 2304), (1792, 2304), (1664, 2496), (1440, 2560), (1312, 3104)];

    public static (int Width, int Height) ForReference(int width, int height)
    {
        if (width < 1 || height < 1) return (2048, 2048);
        var aspect = Math.Log((double)width / height);
        var best = Trained[0]; var distance = double.MaxValue;
        foreach (var candidate in Trained)
        {
            var current = Math.Abs(Math.Log((double)candidate.Width / candidate.Height) - aspect);
            if (current < distance) { distance = current; best = candidate; }
        }
        return best;
    }
}

public sealed record VisualRequest(VisualTask Task, string Prompt, int Width, int Height, long Seed, int Frames = 73, int Steps = 4, bool HighPrecision = false, bool EnhancePrompt = false, bool FullEdit = false, bool LargeAssistant = false, bool AnimationSol = true, bool AnimationGguf = false)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Task) || string.IsNullOrWhiteSpace(Prompt) || Prompt.Length > 6000) throw new InvalidDataException("Escribí una descripción de hasta 6000 caracteres.");
        if (Task == VisualTask.Animation)
        {
            if (Width < 256 || Height < 256 || Width > 832 || Height > 832 || Width % 32 != 0 || Height % 32 != 0)
                throw new InvalidDataException("La resolución supera el perfil visual o no es múltiplo de 32.");
        }
        else if (Width < 256 || Height < 256 || Width > 3104 || Height > 3104 || Width % 32 != 0 || Height % 32 != 0 || (long)Width * Height > 2048L * 2048)
            throw new InvalidDataException("La resolución supera el perfil visual o no es múltiplo de 32.");
        if (Seed < 0 || Seed > 9007199254740991L) throw new InvalidDataException("Semilla fuera de rango.");
        if (Frames is < 25 or > 121 || Frames % 4 != 1 || Steps is < 4 or > 30) throw new InvalidDataException("Duración o pasos de animación fuera del perfil.");
    }
}

public sealed class VisualJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public string Status { get; set; } = "Preparando";
    public string? Error { get; set; }
    public string? PreparedPrompt { get; set; }
    public double? TotalSeconds { get; set; }
    public VisualRequest Request { get; set; } = new(VisualTask.Image, "", 1024, 1024, 0);
    public string? Reference { get; set; }
    public VisualReference? OnlineReference { get; set; }
    public string[] Files { get; set; } = [];
    public string WorkflowVersion { get; set; } = "forja-hidream-full-3 / ComfyUI " + VisualCatalog.ComfyVersion;
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
