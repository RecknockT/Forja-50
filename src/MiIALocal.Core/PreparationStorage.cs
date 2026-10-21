namespace MiIALocal.Core;

public static class PreparationStorage
{
    public static IReadOnlyDictionary<string, long> Requirements(AppPaths paths, AppSettings settings)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetPathRoot(paths.Root)!] = 4_000_000_000
        };
        if (string.IsNullOrWhiteSpace(settings.ImportedModel)) result[Path.GetPathRoot(paths.ModelDirectoryPath(settings))!] = 30_000_000_000;
        return result;
    }
    public static void Check(AppPaths paths, AppSettings settings)
    {
        foreach (var (drive, required) in Requirements(paths, settings))
            if (new DriveInfo(drive).AvailableFreeSpace < required) throw new IOException($"Necesitás aproximadamente {required / 1_000_000_000} GB libres en {drive} para preparar esta versión.");
    }
}
