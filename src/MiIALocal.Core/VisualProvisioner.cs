using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Archives;

namespace MiIALocal.Core;

public sealed class VisualProvisioner(AppPaths paths, VerifiedDownloader downloader)
{
    private string Marker => Path.Combine(paths.ComfyRoot, ".forja-" + VisualCatalog.Runtime.Sha256[..16]);
    public bool IsPrepared(bool animation, GpuInfo? gpu = null, bool assistant = true, bool fullEdit = false, bool largeAssistant = false) => !RuntimeMissing && Missing(animation, gpu, assistant, fullEdit, largeAssistant).Length == 0
        && (!animation || gpu?.IsRtx3090 != true || new GgufRuntime(paths, downloader).IsPrepared);

    /// <summary>Models that are still missing or incomplete, so the app can say which one blocks the start.</summary>
    public VisualModel[] Missing(bool animation, GpuInfo? gpu = null, bool assistant = true, bool fullEdit = false, bool largeAssistant = false)
    {
        if (!File.Exists(Marker) || !File.Exists(paths.ComfyPython) || !File.Exists(Path.Combine(paths.ComfyRoot, "ComfyUI", "main.py"))) return [];
        var missing = new List<VisualModel>();
        foreach (var model in VisualCatalog.For(animation, gpu, assistant, fullEdit, largeAssistant))
        {
            var file = VisualCatalog.PathFor(paths, model);
            if (!File.Exists(file) || new FileInfo(file).Length != model.Asset.Bytes) missing.Add(model);
        }
        return [.. missing];
    }

    /// <summary>True when the engine itself (not a model) is what is missing.</summary>
    public bool RuntimeMissing => !File.Exists(Marker) || !File.Exists(paths.ComfyPython) || !File.Exists(Path.Combine(paths.ComfyRoot, "ComfyUI", "main.py"));

    /// <summary>Human readable state of the engine and every expected model, to diagnose a blocked start.</summary>
    public string Report(bool animation, GpuInfo? gpu = null, bool assistant = true, bool fullEdit = false, bool largeAssistant = false)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("Forja 50 · comprobación del modo visual");
        text.AppendLine("Datos: " + paths.Root);
        text.AppendLine("Motor: " + paths.ComfyRoot);
        text.AppendLine("Marca del motor: " + (File.Exists(Marker) ? "presente" : "falta") + " · " + Path.GetFileName(Marker));
        text.AppendLine("Python del motor: " + (File.Exists(paths.ComfyPython) ? "presente" : "falta") + " · " + paths.ComfyPython);
        text.AppendLine("main.py: " + (File.Exists(Path.Combine(paths.ComfyRoot, "ComfyUI", "main.py")) ? "presente" : "falta"));
        text.AppendLine("Opciones: asistente=" + assistant + " · modelo Full para editar=" + fullEdit + " · asistente 12B=" + largeAssistant + " · animación=" + animation);
        text.AppendLine("Placa: " + (gpu is null ? "no detectada" : gpu.Name + " · " + gpu.FreeMiB + " MiB libres"));
        if (animation && gpu?.IsRtx3090 == true)
            text.AppendLine("Cargador GGUF y dependencia aislada: " + (new GgufRuntime(paths, downloader).IsPrepared ? "listos" : "faltan; pulsá Descargar y preparar"));
        foreach (var model in VisualCatalog.For(animation, gpu, assistant, fullEdit, largeAssistant))
        {
            var file = VisualCatalog.PathFor(paths, model);
            var exists = File.Exists(file);
            var actual = exists ? new FileInfo(file).Length : 0;
            var state = !exists ? "FALTA" : actual == model.Asset.Bytes ? "correcto" : "tamaño distinto";
            text.AppendLine("- " + model.Asset.Name + ": " + state + " · " + actual + " de " + model.Asset.Bytes + " bytes · " + file);
        }
        text.AppendLine("Puede iniciar: " + (IsPrepared(animation, gpu, assistant, fullEdit, largeAssistant) ? "sí" : "no"));
        return text.ToString();
    }

    public long RequiredFreeBytes(bool animation, GpuInfo? gpu = null, bool assistant = true, bool fullEdit = false, bool largeAssistant = false)
    {
        long required = 2_000_000_000;
        if (!File.Exists(Marker)) required += 12_000_000_000 + VisualCatalog.Runtime.Bytes!.Value - VerifiedDownloader.PersistedBytes(VisualCatalog.Runtime, Path.Combine(paths.Downloads, VisualCatalog.Runtime.FileName));
        foreach (var model in VisualCatalog.For(animation, gpu, assistant, fullEdit, largeAssistant))
        {
            var destination = VisualCatalog.PathFor(paths, model);
            if (!File.Exists(destination) || new FileInfo(destination).Length != model.Asset.Bytes)
                required += model.Asset.Bytes!.Value - VerifiedDownloader.PersistedBytes(model.Asset, destination);
        }
        if (animation && gpu?.IsRtx3090 == true && !new GgufRuntime(paths, downloader).IsPrepared) required += GgufRuntime.Assets.Sum(a => a.Bytes!.Value) * 3;
        return required;
    }
    public async Task PrepareAsync(VisualSettings settings, IProgress<PreparationProgress>? progress, CancellationToken ct, GpuInfo? gpu = null)
    {
        if (!settings.HiDreamLicenseAccepted) throw new InvalidOperationException("Revisá y aceptá las licencias de HiDream y los componentes visuales antes de descargar.");
        if (settings.IncludeAnimation && !VisualCatalog.SupportsAnimation(gpu)) throw new InvalidOperationException("No se detectó una placa compatible con el perfil de animación.");
        var required = RequiredFreeBytes(settings.IncludeAnimation, gpu, settings.EnhancePrompt, settings.FullEdit, settings.LargeAssistant);
        if (new DriveInfo(Path.GetPathRoot(paths.Root)!).AvailableFreeSpace < required) throw new IOException($"Necesitás aproximadamente {required / 1e9:0.0} GB libres para esta preparación visual.");
        if (!File.Exists(Marker) || !File.Exists(paths.ComfyPython) || !File.Exists(Path.Combine(paths.ComfyRoot, "ComfyUI", "main.py")))
        {
            var archive = Path.Combine(paths.Downloads, VisualCatalog.Runtime.FileName);
            await downloader.DownloadAsync(VisualCatalog.Runtime, archive, progress, ct);
            progress?.Report(new("Extrayendo el motor visual; puede tardar unos minutos"));
            var target = Path.Combine(paths.Runtime, "comfy");
            var staging = target + ".stage-" + Guid.NewGuid().ToString("N");
            try
            {
                await Task.Run(() => ExtractSevenZip(archive, staging, ct), ct);
                var inner = Path.Combine(staging, "ComfyUI_windows_portable");
                if (!File.Exists(Path.Combine(inner, "python_embeded", "python.exe")) || !File.Exists(Path.Combine(inner, "ComfyUI", "main.py"))) throw new InvalidDataException("El paquete visual no tiene la estructura esperada.");
                AppPaths.AtomicWrite(Path.Combine(inner, Path.GetFileName(Marker)), VisualCatalog.Runtime.Sha256);
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(target)) Directory.Move(target, target + ".previous-" + Guid.NewGuid().ToString("N"));
                Directory.Move(staging, target);
            }
            finally { if (Directory.Exists(staging)) DeleteStaging(staging); }
        }
        foreach (var model in VisualCatalog.For(settings.IncludeAnimation, gpu, settings.EnhancePrompt, settings.FullEdit, settings.LargeAssistant))
            await downloader.DownloadAsync(model.Asset, VisualCatalog.PathFor(paths, model), progress, ct);
        if (settings.IncludeAnimation && gpu?.IsRtx3090 == true) await new GgufRuntime(paths, downloader).PrepareAsync(progress, ct);
        WriteConfiguration();
    }
    public async Task ImportModelsAsync(IEnumerable<string> files, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        foreach (var source in files)
        {
            var model = VisualCatalog.Images.SingleOrDefault(m => m.Asset.FileName.Equals(Path.GetFileName(source), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("El archivo no pertenece al perfil HiDream fijado por Forja.");
            progress?.Report(new("Comprobando " + model.Asset.Name));
            if (!await VerifiedDownloader.MatchesAsync(source, model.Asset, ct)) throw new InvalidDataException("El tamaño o SHA-256 de " + model.Asset.FileName + " no coincide con la versión de Forja.");
            var destination = VisualCatalog.PathFor(paths, model);
            if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".import-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var input = File.OpenRead(source))
                await using (var output = File.Create(temporary)) await input.CopyToAsync(output, ct);
                if (!await VerifiedDownloader.MatchesAsync(temporary, model.Asset, ct)) throw new InvalidDataException("La copia no pasó la comprobación de integridad.");
                ct.ThrowIfCancellationRequested(); File.Move(temporary, destination, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public async Task VerifyModelsAsync(bool animation, CancellationToken ct, GpuInfo? gpu = null, bool assistant = true, bool fullEdit = false, bool largeAssistant = false)
    {
        if (!IsPrepared(animation, gpu, assistant, fullEdit, largeAssistant)) throw new InvalidOperationException("Prepará el modo visual antes de iniciar.");
        foreach (var model in VisualCatalog.For(animation, gpu, assistant, fullEdit, largeAssistant))
            if (!await VerifiedDownloader.MatchesAsync(VisualCatalog.PathFor(paths, model), model.Asset, ct))
                throw new InvalidDataException("Un modelo visual está incompleto o fue modificado. Prepará el modo para repararlo.");
    }
    public void WriteConfiguration()
    {
        foreach (var dir in new[] { paths.VisualInputs, paths.VisualOutputs, paths.VisualJobs, Path.Combine(paths.VisualRoot, "user"), Path.Combine(paths.VisualRoot, "temp") }) Directory.CreateDirectory(dir);
        AppPaths.AtomicWrite(Path.Combine(paths.VisualRoot, "models.yaml"), "forja:\n  base_path: " + JsonSerializer.Serialize(paths.VisualModels) + "\n  checkpoints: checkpoints\n  diffusion_models: diffusion_models\n  text_encoders: text_encoders\n  vae: vae\n  loras: loras\n");
        var destination = Path.Combine(paths.ComfyRoot, "ComfyUI", "custom_nodes", "forja_guard", "__init__.py");
        using var resource = typeof(VisualProvisioner).Assembly.GetManifestResourceStream("MiIALocal.Core.VisualAssets.forja_guard.py") ?? throw new IOException("Falta el puente visual de Forja.");
        using var reader = new StreamReader(resource); using var animationResource = typeof(VisualProvisioner).Assembly.GetManifestResourceStream("MiIALocal.Core.VisualAssets.forja_animation.py") ?? throw new IOException("Falta el puente de animación.");
        using var animationReader = new StreamReader(animationResource);
        AppPaths.AtomicWrite(destination, reader.ReadToEnd() + "\n" + animationReader.ReadToEnd());
    }
    public static void ExtractSevenZip(string archivePath, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        // Validate the complete directory before writing the first entry.
        foreach (var entry in archive.Entries)
        {
            _ = SafeArchive.ResolveEntry(destination, entry.Key ?? throw new InvalidDataException("Entrada sin nombre."));
            if (entry.LinkTarget is not null || entry.IsEncrypted) throw new InvalidDataException("Enlaces o entradas cifradas no admitidos.");
        }
        using var reader = archive.ExtractAllEntries();
        long total = 0;
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested(); var entry = reader.Entry;
            var path = SafeArchive.ResolveEntry(destination, entry.Key!);
            // The managed engine is immutable; omit updater Git metadata so it cannot be mistaken for a user repository during cleanup.
            if (entry.Key!.Replace('\\', '/').Split('/').Any(p => p.Equals(".git", StringComparison.OrdinalIgnoreCase))) continue;
            total += entry.Size; if (total > 20_000_000_000) throw new InvalidDataException("El motor supera el tamaño de extracción previsto.");
            if (entry.IsDirectory) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = reader.OpenEntryStream(); using var output = File.Create(path);
            var buffer = new byte[131072]; int count;
            while ((count = input.Read(buffer)) != 0) { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
        }
    }
    private static void DeleteStaging(string directory)
    {
        // Only freshly created, validated extraction directories reach here.
        foreach (var child in Directory.EnumerateFileSystemEntries(directory))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) throw new IOException("Enlace inesperado en la extracción temporal.");
            if (Directory.Exists(child)) DeleteStaging(child); else File.Delete(child);
        }
        Directory.Delete(directory);
    }
}
