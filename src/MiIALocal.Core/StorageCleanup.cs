namespace MiIALocal.Core;

public enum StorageCategory { Models, Components, Downloads, Conversations, Settings }
public sealed record StorageEntry(StorageCategory Category, string Title, string[] Paths, long Bytes, string? BlockedReason);

/// <summary>Deletes only known application data. Imported assets, projects and links are protected.</summary>
public sealed class StorageCleanup(AppPaths paths, AppSettings settings)
{
    private static readonly StringComparison Comparison = StringComparison.OrdinalIgnoreCase;
    public static bool Within(string path, string parent) => path.Equals(parent, Comparison) || path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, Comparison);
    private string[] Imported => new[] { settings.ImportedModel, settings.ImportedNinferModel, settings.NinferEnginePath }
        .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.GetFullPath(p, paths.Root)).ToArray();

    public StorageEntry[] Inspect()
    {
        var models = new List<string>(); var partials = new List<string>();
        foreach (var asset in new[] { ReleaseProfile.Model, ReleaseProfile.NinferModel })
        {
            var file = Path.Combine(paths.ModelDirectoryPath(settings), asset.FileName);
            if (Imported.Any(p => p.Equals(file, Comparison))) continue;
            if (File.Exists(file)) models.Add(file);
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
            Entry(StorageCategory.Models, "Modelos descargados por Forja", models),
            Entry(StorageCategory.Components, "Motores y herramientas instalados", [paths.Runtime]),
            Entry(StorageCategory.Downloads, "Caché y descargas incompletas", new[] { paths.Downloads, Path.Combine(paths.Root, "updates") }.Concat(partials)),
            Entry(StorageCategory.Conversations, "Conversaciones y datos del agente", new[] { "agent-data", "agent-state", "agent-cache", "agent-config", "browser", "opencode.json", "instrucciones.md" }.Select(p => Path.Combine(paths.Root, p))),
            Entry(StorageCategory.Settings, "Configuración y registros", configuration)
        ];
    }
    private StorageEntry Entry(StorageCategory category, string title, IEnumerable<string> candidates)
    {
        var targets = candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        try
        {
            long bytes = 0;
            foreach (var target in targets) { Validate(target); bytes += InspectTree(target); }
            return new(category, title, targets, bytes, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(category, title, targets, 0, ex.Message); }
    }
    private void CheckAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Se conserva una ruta con enlaces: " + current);
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git"))) throw new IOException("Se conserva un repositorio Git: " + current);
        }
    }
    private void Validate(string target)
    {
        CheckAncestors(target);
        if (!string.IsNullOrWhiteSpace(settings.LastProject))
        {
            var project = Path.GetFullPath(settings.LastProject, paths.Root);
            if (Within(target, project) || Within(project, target)) throw new IOException("Se conserva la carpeta de proyecto: " + project);
        }
        if (Imported.Any(p => Within(p, target) || Within(target, p))) throw new IOException("Se conserva un archivo importado o su carpeta: " + target);
    }
    private long InspectTree(string target)
    {
        Validate(target);
        if (File.Exists(target)) return new FileInfo(target).Length;
        long bytes = 0;
        foreach (var child in Directory.EnumerateFileSystemEntries(target))
        {
            if (Path.GetFileName(child).Equals(".git", Comparison)) throw new IOException("Se conserva una carpeta que contiene un repositorio: " + target);
            bytes += InspectTree(child);
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
