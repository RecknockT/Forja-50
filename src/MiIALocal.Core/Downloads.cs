using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace MiIALocal.Core;

public sealed record PreparationProgress(string Stage, long Done = 0, long? Total = null, double? BytesPerSecond = null)
{
    public double? Percentage => Total is > 0 ? Math.Clamp(Done * 100d / Total.Value, 0, 100) : null;
    public TimeSpan? Remaining => Total is long total && BytesPerSecond is > 0 ? TimeSpan.FromSeconds(Math.Clamp((total - Done) / BytesPerSecond.Value, 0, TimeSpan.MaxValue.TotalSeconds / 2)) : null;
    public string TransferDetails => Total is long total
        ? $"{Done / 1e9:0.00} / {total / 1e9:0.00} GB · " + (BytesPerSecond is > 0 ? $"{BytesPerSecond / 1e6:0.00} MB/s · restante aprox. {Remaining:hh\\:mm\\:ss}" : "calculando velocidad...")
        : "Preparando y verificando archivos...";
}

public sealed record DownloadOptions(int Connections = 4, int ChunkBytes = 8 * 1024 * 1024, long ParallelThreshold = 64 * 1024 * 1024, int Retries = 3, TimeSpan? StallTimeout = null, TimeSpan? RetryDelay = null);

public sealed class VerifiedDownloader(HttpClient client, DownloadOptions? options = null)
{
    private readonly DownloadOptions options = options ?? new();
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
        if (offset > 0 && asset.Bytes is null && await MatchesAsync(partial, asset, ct)) { File.Move(partial, destination); return; }
        if (asset.Bytes is long expected && offset >= expected)
        {
            if (offset == expected && await MatchesAsync(partial, asset, ct)) { File.Move(partial, destination); return; }
            File.Move(partial, partial + ".invalid-" + Guid.NewGuid().ToString("N")); offset = 0;
        }
        if (asset.Bytes is long length && length >= options.ParallelThreshold)
        {
            try { await ParallelAsync(asset, partial, offset, progress, ct); }
            catch (RangesUnsupportedException) { await SerialWithRetriesAsync(asset, partial, progress, ct); }
        }
        else await SerialWithRetriesAsync(asset, partial, progress, ct);
        progress?.Report(new($"Verificando integridad de {asset.Name}"));
        if (!await MatchesAsync(partial, asset, ct))
        {
            File.Move(partial, partial + ".invalid-" + Guid.NewGuid().ToString("N"));
            throw new InvalidDataException("La comprobación de integridad falló. No se usó el archivo; podés reintentar.");
        }
        ct.ThrowIfCancellationRequested();
        File.Move(partial, destination);
    }
    private async Task SerialWithRetriesAsync(DownloadAsset asset, string partial, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await SerialAsync(asset, partial, progress, ct); return; }
            catch (Exception ex) when (CanRetry(ex, ct) && attempt < options.Retries)
            { await RetryAsync(asset.Name, attempt, progress, ct); }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            { throw new HttpRequestException("La descarga sigue sin responder después de los reintentos. El progreso se conservó; podés reanudar.", ex); }
        }
    }
    private async Task SerialAsync(DownloadAsset asset, string partial, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var response = await SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && offset > 0)
        {
            File.Move(partial, partial + ".invalid-" + Guid.NewGuid().ToString("N"));
            await SerialAsync(asset, partial, progress, ct);
            return;
        }
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content.Headers.ContentRange?.From != offset) throw new InvalidDataException("El servidor devolvió un fragmento incorrecto. La copia parcial se conservó.");
        }
        else offset = 0; // The server may ignore Range. Restart instead of appending duplicate data.
        var total = response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength is long length ? offset + length : asset.Bytes);
        if (asset.Bytes is long required && total is long advertised && advertised != required) throw new InvalidDataException("El tamaño publicado de la descarga no coincide con el perfil.");
        var meter = new TransferMeter(asset.Name, progress);
        await using (var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, true))
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[1024 * 1024]; var done = offset;
            while (true)
            {
                var count = await ReadAsync(source, buffer, ct); if (count == 0) break;
                if (total is long limit && done + count > limit) throw new InvalidDataException("El servidor envió más datos de los esperados.");
                await output.WriteAsync(buffer.AsMemory(0, count), ct); done += count;
                meter.Report(done, total, count);
            }
            await output.FlushAsync(ct);
            if (total is long expected && done != expected) throw new IOException("La conexión se interrumpió antes de completar el archivo.");
            meter.Report(done, total, 0, true);
        }
    }
    private async Task ParallelAsync(DownloadAsset asset, string partial, long offset, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        if (options.Connections is < 1 or > 8 || options.ChunkBytes is < 1 or > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(options));
        var total = asset.Bytes!.Value;
        var meter = new TransferMeter(asset.Name + $" · {options.Connections} conexiones", progress);
        // Only contiguous, completed batches reach .part. Existing serial partials remain compatible.
        while (offset < total)
        {
            ct.ThrowIfCancellationRequested();
            using var batch = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var counts = new long[options.Connections]; var gate = new object();
            var starts = Enumerable.Range(0, options.Connections).Select(i => offset + (long)i * options.ChunkBytes).Where(start => start < total).ToArray();
            var tasks = starts.Select((start, index) => FetchAsync(start, Math.Min(start + options.ChunkBytes - 1, total - 1), index)).ToArray();
            async Task<byte[]> FetchAsync(long start, long end, int index)
            {
                try
                {
                    for (var attempt = 0; ; attempt++)
                    {
                        try
                        {
                            lock (gate) counts[index] = 0;
                            using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
                            request.Headers.Range = new(start, end);
                            using var response = await SendAsync(request, batch.Token);
                            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.RequestedRangeNotSatisfiable) throw new RangesUnsupportedException();
                            response.EnsureSuccessStatusCode();
                            var range = response.Content.Headers.ContentRange;
                            if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != start || range.To != end || range.Length != total)
                                throw new InvalidDataException("El servidor devolvió un fragmento incorrecto. Se conservó el progreso anterior.");
                            var data = new byte[checked((int)(end - start + 1))]; var filled = 0;
                            await using var source = await response.Content.ReadAsStreamAsync(batch.Token);
                            while (filled < data.Length)
                            {
                                var read = await ReadAsync(source, data.AsMemory(filled), batch.Token);
                                if (read == 0) throw new IOException("El fragmento llegó incompleto.");
                                filled += read;
                                lock (gate) { counts[index] = filled; meter.Report(offset + counts.Sum(), total, read); }
                            }
                            return data;
                        }
                        catch (Exception ex) when (CanRetry(ex, batch.Token) && attempt < options.Retries)
                        { await RetryAsync(asset.Name, attempt, progress, batch.Token); }
                        catch (OperationCanceledException ex) when (!batch.IsCancellationRequested)
                        { throw new HttpRequestException("Una conexión sigue sin responder después de los reintentos. El progreso se conservó; podés reanudar.", ex); }
                    }
                }
                catch { batch.Cancel(); throw; }
            }
            byte[][] buffers;
            try { buffers = await Task.WhenAll(tasks); }
            catch
            {
                // Observe all workers before fallback; never leave requests running in the background.
                if (!ct.IsCancellationRequested && tasks.Any(t => t.Exception?.InnerExceptions.Any(e => e is RangesUnsupportedException) == true)) throw new RangesUnsupportedException();
                var error = tasks.Where(t => t.IsFaulted).SelectMany(t => t.Exception!.InnerExceptions).FirstOrDefault();
                if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                throw;
            }
            await using var output = new FileStream(partial, FileMode.Append, FileAccess.Write, FileShare.Read, 1024 * 1024, true);
            foreach (var data in buffers) { await output.WriteAsync(data, ct); offset += data.Length; }
            await output.FlushAsync(ct); meter.Report(offset, total, 0, true);
        }
    }
    private TimeSpan StallTimeout => options.StallTimeout ?? TimeSpan.FromSeconds(30);
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(StallTimeout);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    }
    private async Task<int> ReadAsync(Stream source, Memory<byte> buffer, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(StallTimeout);
        return await source.ReadAsync(buffer, timeout.Token);
    }
    private static bool CanRetry(Exception ex, CancellationToken ct) => !ct.IsCancellationRequested && ex switch
    {
        HttpRequestException http => http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)http.StatusCode.Value >= 500,
        IOException => ex is not InvalidDataException,
        OperationCanceledException => true,
        _ => false
    };
    private async Task RetryAsync(string name, int attempt, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new($"Conexión interrumpida o sin respuesta · reintentando {name} ({attempt + 1}/{options.Retries})"));
        await Task.Delay(options.RetryDelay ?? TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
    }
    private sealed class RangesUnsupportedException : Exception;
    private sealed class TransferMeter(string name, IProgress<PreparationProgress>? progress)
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long received, lastReport;
        public void Report(long done, long? total, int count, bool force = false)
        {
            received += count;
            if (!force && clock.ElapsedMilliseconds - lastReport < 300) return;
            lastReport = clock.ElapsedMilliseconds;
            progress?.Report(new(name, done, total, received / Math.Max(clock.Elapsed.TotalSeconds, .001)));
        }
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
