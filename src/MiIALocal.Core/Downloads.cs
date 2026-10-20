using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace MiIALocal.Core;

public sealed record PreparationProgress(string Stage, long Done = 0, long? Total = null)
{
    public double? Percentage => Total is > 0 ? Math.Clamp(Done * 100d / Total.Value, 0, 100) : null;
}

public sealed class VerifiedDownloader(HttpClient client)
{
    public static async Task<bool> MatchesAsync(string file, DownloadAsset asset, CancellationToken ct)
    {
        if (!File.Exists(file) || (asset.Bytes is long size && new FileInfo(file).Length != size)) return false;
        await using var input = File.OpenRead(file);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    public async Task DownloadAsync(DownloadAsset asset, string destination, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        if (new Uri(asset.Url).Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("La descarga debe usar HTTPS.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            progress?.Report(new($"Comprobando {asset.Name}"));
            if (await MatchesAsync(destination, asset, ct)) return;
            File.Move(destination, destination + ".invalid-" + Guid.NewGuid().ToString("N"));
        }
        var partial = destination + ".part";
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (asset.Bytes is long expected && offset >= expected)
        {
            if (offset == expected && await MatchesAsync(partial, asset, ct)) { File.Move(partial, destination); return; }
            File.Move(partial, partial + ".invalid-" + Guid.NewGuid().ToString("N")); offset = 0;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content.Headers.ContentRange?.From != offset) throw new InvalidDataException("El servidor devolvió un fragmento incorrecto. La copia parcial se conservó.");
        }
        else offset = 0; // The server may ignore Range. Restart instead of appending duplicate data.
        var total = response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength is long length ? offset + length : asset.Bytes);
        if (asset.Bytes is long required && total is long advertised && advertised != required) throw new InvalidDataException("El tamaño publicado de la descarga no coincide con el perfil.");
        await using (var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, true))
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[1024 * 1024]; var done = offset; var clock = Stopwatch.StartNew();
            while (true)
            {
                var count = await source.ReadAsync(buffer, ct); if (count == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, count), ct); done += count;
                if (clock.ElapsedMilliseconds >= 300) { progress?.Report(new(asset.Name, done, total)); clock.Restart(); }
            }
            await output.FlushAsync(ct); progress?.Report(new(asset.Name, done, total));
        }
        progress?.Report(new($"Verificando integridad de {asset.Name}"));
        if (!await MatchesAsync(partial, asset, ct))
        {
            File.Move(partial, partial + ".invalid-" + Guid.NewGuid().ToString("N"));
            throw new InvalidDataException("La comprobación de integridad falló. No se usó el archivo; podés reintentar.");
        }
        File.Move(partial, destination);
    }
}

public static class SafeArchive
{
    public static string ResolveEntry(string root, string entry)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(prefix, entry.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || entry.Contains(':')) throw new InvalidDataException("El paquete contiene una ruta que sale de su carpeta.");
        return path;
    }
    public static void Extract(string zip, string destination, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zip);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var path = ResolveEntry(destination, entry.FullName);
            if (!seen.Add(path)) throw new InvalidDataException("El paquete contiene rutas duplicadas.");
            // Reject Unix symbolic links. No archive entry may redirect extraction.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("No se admiten enlaces simbólicos en paquetes.");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) Directory.CreateDirectory(path);
            else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path, false); }
        }
    }
}

public sealed class Provisioner(AppPaths paths, VerifiedDownloader downloader)
{
    public bool IsPrepared(AppSettings settings) => File.Exists(paths.EngineExe) && File.Exists(paths.AgentExe) && File.Exists(paths.GitExe) && File.Exists(paths.NodeExe) && File.Exists(paths.ModelPath(settings));
    public async Task PrepareAsync(AppSettings settings, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        if (!settings.LicenseAccepted) throw new InvalidOperationException("Revisá las licencias y confirmá su aceptación antes de descargar.");
        foreach (var asset in ReleaseProfile.Components)
        {
            var archive = Path.Combine(paths.Downloads, asset.FileName);
            await downloader.DownloadAsync(asset, archive, progress, ct);
            var target = Path.Combine(paths.Runtime, asset.Id);
            var marker = Path.Combine(target, ".installed.json");
            if (File.Exists(marker) && await ValidateInstalledAsync(target, marker, ct)) continue;
            progress?.Report(new($"Preparando {asset.Name}"));
            var staging = Path.Combine(paths.Runtime, asset.Id + ".stage-" + Guid.NewGuid().ToString("N"));
            await Task.Run(() => SafeArchive.Extract(archive, staging, ct), ct);
            var hashes = new Dictionary<string, string>();
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested(); await using var input = File.OpenRead(file);
                hashes[Path.GetRelativePath(staging, file)] = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct));
            }
            AppPaths.AtomicWrite(Path.Combine(staging, ".installed.json"), System.Text.Json.JsonSerializer.Serialize(hashes));
            if (Directory.Exists(target)) Directory.Move(target, target + ".previous-" + Guid.NewGuid().ToString("N"));
            Directory.Move(staging, target);
        }
        progress?.Report(new("Comprobando el modelo"));
        if (!string.IsNullOrWhiteSpace(settings.ImportedModel))
        {
            if (!await VerifiedDownloader.MatchesAsync(settings.ImportedModel, ReleaseProfile.Model, ct)) throw new InvalidDataException("El archivo elegido no es el modelo de esta versión o está incompleto.");
        }
        else await downloader.DownloadAsync(ReleaseProfile.Model, paths.ModelPath(settings), progress, ct);
        paths.SaveSettings(settings);
    }
    private static async Task<bool> ValidateInstalledAsync(string root, string marker, CancellationToken ct)
    {
        try
        {
            var files = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(marker, ct));
            if (files is null || files.Count == 0) return false;
            foreach (var (entry, hash) in files)
            {
                var path = SafeArchive.ResolveEntry(root, entry);
                if (!File.Exists(path)) return false;
                await using var input = File.OpenRead(path);
                if (!Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct)).Equals(hash, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException) { return false; }
    }
}
