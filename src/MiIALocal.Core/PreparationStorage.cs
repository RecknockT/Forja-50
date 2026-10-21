namespace MiIALocal.Core;

public static class PreparationStorage
{
    public static IReadOnlyDictionary<string, long> Requirements(AppPaths paths, AppSettings settings)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetPathRoot(paths.Root)!] = 4_000_000_000
        };
        if (string.IsNullOrWhiteSpace(paths.ImportedModelPath(settings)))
        {
            var drive = Path.GetPathRoot(paths.ModelDirectoryPath(settings))!;
            if (settings.EngineMode == EngineMode.NInfer5080)
            {
                var remaining = ReleaseProfile.NinferModel.Bytes!.Value - VerifiedDownloader.PersistedBytes(ReleaseProfile.NinferModel, paths.ActiveModelPath(settings));
                result[drive] = result.GetValueOrDefault(drive) + remaining + 1_000_000_000;
            }
            else result[drive] = 30_000_000_000;
        }
        return result;
    }
    public static void Check(AppPaths paths, AppSettings settings)
    {
        foreach (var (drive, required) in Requirements(paths, settings))
            if (new DriveInfo(drive).AvailableFreeSpace < required) throw new IOException($"Necesitás aproximadamente {required / 1_000_000_000} GB libres en {drive} para preparar esta versión.");
    }
}
