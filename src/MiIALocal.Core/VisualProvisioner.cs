using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Archives;

namespace MiIALocal.Core;

public sealed class VisualProvisioner(AppPaths paths, VerifiedDownloader downloader)
{
    private string Marker => Path.Combine(paths.ComfyRoot, ".forja-" + VisualCatalog.Runtime.Sha256[..16]);
    public bool IsPrepared(bool animation) => File.Exists(Marker) && File.Exists(paths.ComfyPython)
        && File.Exists(Path.Combine(paths.ComfyRoot, "ComfyUI", "main.py"))
        && VisualCatalog.For(animation).All(m => File.Exists(VisualCatalog.PathFor(paths, m)) && new FileInfo(VisualCatalog.PathFor(paths, m)).Length == m.Asset.Bytes);

    public long RequiredFreeBytes(bool animation)
    {
        long required = 2_000_000_000;
        if (!File.Exists(Marker)) required += 12_000_000_000 + VisualCatalog.Runtime.Bytes!.Value - VerifiedDownloader.PersistedBytes(VisualCatalog.Runtime, Path.Combine(paths.Downloads, VisualCatalog.Runtime.FileName));
        foreach (var model in VisualCatalog.For(animation))
        {
            var destination = VisualCatalog.PathFor(paths, model);
            if (!File.Exists(destination) || new FileInfo(destination).Length != model.Asset.Bytes)
                required += model.Asset.Bytes!.Value - VerifiedDownloader.PersistedBytes(model.Asset, destination);
        }
        return required;
    }
    public async Task PrepareAsync(VisualSettings settings, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        if (!settings.LicenseAccepted) throw new InvalidOperationException("Revisá y aceptá las licencias visuales antes de descargar.");
        var required = RequiredFreeBytes(settings.IncludeAnimation);
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
        foreach (var model in VisualCatalog.For(settings.IncludeAnimation))
            await downloader.DownloadAsync(model.Asset, VisualCatalog.PathFor(paths, model), progress, ct);
        WriteConfiguration();
    }
    public async Task VerifyModelsAsync(bool animation, CancellationToken ct)
    {
        if (!IsPrepared(animation)) throw new InvalidOperationException("Prepará el modo visual antes de iniciar.");
        foreach (var model in VisualCatalog.For(animation))
            if (!await VerifiedDownloader.MatchesAsync(VisualCatalog.PathFor(paths, model), model.Asset, ct))
                throw new InvalidDataException("Un modelo visual está incompleto o fue modificado. Prepará el modo para repararlo.");
    }
    public void WriteConfiguration()
    {
        foreach (var dir in new[] { paths.VisualInputs, paths.VisualOutputs, paths.VisualJobs, Path.Combine(paths.VisualRoot, "user"), Path.Combine(paths.VisualRoot, "temp") }) Directory.CreateDirectory(dir);
        AppPaths.AtomicWrite(Path.Combine(paths.VisualRoot, "models.yaml"), "forja:\n  base_path: " + JsonSerializer.Serialize(paths.VisualModels) + "\n  diffusion_models: diffusion_models\n  text_encoders: text_encoders\n  vae: vae\n");
        var destination = Path.Combine(paths.ComfyRoot, "ComfyUI", "custom_nodes", "forja_guard", "__init__.py");
        using var resource = typeof(VisualProvisioner).Assembly.GetManifestResourceStream("MiIALocal.Core.VisualAssets.forja_guard.py") ?? throw new IOException("Falta el puente visual de Forja.");
        using var reader = new StreamReader(resource); AppPaths.AtomicWrite(destination, reader.ReadToEnd());
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
