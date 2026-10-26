namespace MiIALocal.Core;

public enum StorageCategory { Models, Components, Downloads, Conversations, Settings, GitHub, Legacy }
public sealed record StorageEntry(StorageCategory Category, string Title, string[] Paths, long Bytes, string? BlockedReason);

/// <summary>Deletes only known application data. Imported assets, projects and links are protected.</summary>
public sealed class StorageCleanup(AppPaths paths, AppSettings settings)
{
    private static readonly StringComparison Comparison = StringComparison.OrdinalIgnoreCase;
    public static bool Within(string path, string parent) => path.Equals(parent, Comparison) || path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, Comparison);
    private string[] Imported => new[] { settings.ImportedModel, settings.ImportedNinferModel, settings.NinferEnginePath, paths.VisualInputs, paths.VisualOutputs }
        .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.GetFullPath(p, paths.Root)).ToArray();

    public StorageEntry[] Inspect(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var models = new List<string>(); var legacy = new List<string>(); var partials = new List<string>();
        var managedModels = new[] { ReleaseProfile.Model, ReleaseProfile.NinferModel }.Select(a => (Asset: a, Directory: paths.ModelDirectoryPath(settings), Previous: false))
            .Concat(VisualCatalog.Images.Concat(VisualCatalog.Animations).Concat(VisualCatalog.GgufAnimations.Except(VisualCatalog.Animations)).Select(m => (Asset: m.Asset, Directory: Path.GetDirectoryName(VisualCatalog.PathFor(paths, m))!, Previous: false)))
            .Concat(VisualCatalog.Legacy.Append(VisualCatalog.FluxVae).Select(m => (Asset: m.Asset, Directory: Path.GetDirectoryName(VisualCatalog.PathFor(paths, m))!, Previous: true)));
        foreach (var (asset, modelDirectory, previous) in managedModels)
        {
            ct.ThrowIfCancellationRequested();
            var file = Path.Combine(modelDirectory, asset.FileName);
            if (Imported.Any(p => p.Equals(file, Comparison))) continue;
            if (File.Exists(file)) (previous ? legacy : models).Add(file);
            var directory = Path.GetDirectoryName(file)!;
            // Check ancestors before even inspecting a custom model directory.
            try { CheckAncestors(directory); }
            catch (IOException) { continue; }
            if (!Directory.Exists(directory)) continue;
            foreach (var candidate in Directory.EnumerateFileSystemEntries(directory, asset.FileName + ".*"))
            {
                var suffix = Path.GetFileName(candidate)[asset.FileName.Length..];
                if (suffix == ".part" || suffix.StartsWith(".chunks-", StringComparison.Ordinal) || suffix.StartsWith(".invalid-", StringComparison.Ordinal) || suffix.StartsWith(".part.invalid-", StringComparison.Ordinal)) partials.Add(candidate);
            }
        }
        var configuration = new List<string> { paths.Logs };
        foreach (var file in Directory.EnumerateFiles(paths.Root, "settings*.json"))
            if (Path.GetFileName(file) == "settings.json" || Path.GetFileName(file).StartsWith("settings.invalid-", StringComparison.Ordinal) || Path.GetFileName(file).StartsWith("settings.json.before-native-", StringComparison.Ordinal)) configuration.Add(file);
        return [
            Entry(StorageCategory.Models, "Modelos descargados por Forja", models, ct),
            Entry(StorageCategory.Legacy, "Modelos anteriores (Wan 5B, FLUX.2 y Qwen3, ya no se usan)", legacy, ct),
            Entry(StorageCategory.Components, "Motores y herramientas instalados", [paths.Runtime], ct),
            Entry(StorageCategory.Downloads, "Caché y descargas incompletas", new[] { paths.Downloads, Path.Combine(paths.Root, "updates") }.Concat(partials), ct),
            Entry(StorageCategory.Conversations, "Conversaciones e historial de trabajos", new[] { "agent-data", "agent-state", "agent-cache", "agent-config", "browser", "opencode.json", "instrucciones.md", "visual/jobs", "visual/user" }.Select(p => Path.Combine(paths.Root, p)), ct),
            Entry(StorageCategory.Settings, "Configuración y registros", configuration, ct),
            Entry(StorageCategory.GitHub, "Sesión de GitHub de Forja", [paths.GitHubCredentialStore, paths.GitHubAccountFile], ct)
        ];
    }
    private StorageEntry Entry(StorageCategory category, string title, IEnumerable<string> candidates, CancellationToken ct)
    {
        var targets = candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        try
        {
            long bytes = 0;
            var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets) { ct.ThrowIfCancellationRequested(); bytes += InspectTree(target, ct, ancestors); }
            return new(category, title, targets, bytes, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(category, title, targets, 0, ex.Message); }
    }
    private void CheckAncestors(string path, HashSet<string>? inspected = null)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (inspected?.Contains(current) == true) break;
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Se conserva una ruta con enlaces: " + current);
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git"))) throw new IOException("Se conserva un repositorio Git: " + current);
            inspected?.Add(current);
        }
    }
    private void Validate(string target, HashSet<string>? inspected = null)
    {
        CheckAncestors(target, inspected);
        if (!string.IsNullOrWhiteSpace(settings.LastProject))
        {
            var project = Path.GetFullPath(settings.LastProject, paths.Root);
            if (Within(target, project) || Within(project, target)) throw new IOException("Se conserva la carpeta de proyecto: " + project);
        }
        if (Imported.Any(p => Within(p, target) || Within(target, p))) throw new IOException("Se conserva un archivo importado o su carpeta: " + target);
    }
    private long InspectTree(string target, CancellationToken ct, HashSet<string> inspected)
    {
        ct.ThrowIfCancellationRequested();
        Validate(target, inspected);
        if (File.Exists(target)) return new FileInfo(target).Length;
        long bytes = 0;
        foreach (var child in Directory.EnumerateFileSystemEntries(target))
        {
            if (Path.GetFileName(child).Equals(".git", Comparison)) throw new IOException("Se conserva una carpeta que contiene un repositorio: " + target);
            bytes += InspectTree(child, ct, inspected);
        }
        return bytes;
    }
    public void Delete(IEnumerable<StorageCategory> categories)
    {
        var selected = categories.ToHashSet();
        // Build a fresh plan and validate ALL selections before deleting anything.
        var plan = Inspect().Where(e => selected.Contains(e.Category)).ToArray();
        foreach (var entry in plan)
            if (entry.BlockedReason is not null) throw new IOException(entry.BlockedReason);
        foreach (var entry in plan)
            foreach (var target in entry.Paths) DeleteTree(target);
    }
    private void DeleteTree(string target)
    {
        Validate(target);
        if (File.Exists(target)) { File.Delete(target); return; }
        if (!Directory.Exists(target)) return;
        // No recursive Directory.Delete: links are checked again immediately before each operation.
        foreach (var child in Directory.EnumerateFileSystemEntries(target)) DeleteTree(child);
        Validate(target); Directory.Delete(target, false);
    }
}
