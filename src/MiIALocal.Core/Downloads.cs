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

public sealed record DownloadOptions(int Connections = 16, int ChunkBytes = 64 * 1024 * 1024, long ParallelThreshold = 64 * 1024 * 1024, int Retries = 3, TimeSpan? StallTimeout = null, TimeSpan? RetryDelay = null);

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
    public static string ChunkDirectory(DownloadAsset asset, string destination, int chunkBytes = 64 * 1024 * 1024) => destination + ".chunks-" + asset.Sha256[..12] + "-" + chunkBytes;
    public static bool HasPartial(DownloadAsset asset, string destination) => !File.Exists(destination) && (File.Exists(destination + ".part") || Directory.Exists(ChunkDirectory(asset, destination)) && Directory.EnumerateFiles(ChunkDirectory(asset, destination), "*.bin*").Any());
    public static long PersistedBytes(DownloadAsset asset, string destination)
    {
        var total = asset.Bytes ?? long.MaxValue;
        if (File.Exists(destination) && new FileInfo(destination).Length == total) return total;
        var prefix = File.Exists(destination + ".part") ? Math.Min(total, new FileInfo(destination + ".part").Length) : 0;
        var directory = ChunkDirectory(asset, destination);
        if (!Directory.Exists(directory)) return prefix;
        var chunks = new Dictionary<long, long>();
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(file), @"^(\d+)-(\d+)\.bin(?:\.part)?$");
            if (!match.Success || !long.TryParse(match.Groups[1].Value, out var start) || !long.TryParse(match.Groups[2].Value, out var end) || start < prefix || end < start || end >= total) continue;
            var length = Math.Min(new FileInfo(file).Length, end - start + 1);
            chunks[start] = Math.Max(chunks.GetValueOrDefault(start), length);
        }
        return Math.Min(total, prefix + chunks.Values.Sum());
    }
    private async Task ParallelAsync(DownloadAsset asset, string partial, long offset, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        if (options.Connections is < 1 or > 32 || options.ChunkBytes is < 1 or > 128 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(options));
        var total = asset.Bytes!.Value;
        var directory = ChunkDirectory(asset, partial[..^5], options.ChunkBytes);
        Directory.CreateDirectory(directory);
        var firstIndex = offset / options.ChunkBytes;
        var chunks = Enumerable.Range(0, checked((int)((total - 1) / options.ChunkBytes - firstIndex + 1)))
            .Select(index => (Start: Math.Max(offset, (firstIndex + index) * options.ChunkBytes), End: Math.Min(total - 1, (firstIndex + index + 1) * options.ChunkBytes - 1)))
            .Select(range => new Chunk(range.Start, range.End, Path.Combine(directory, $"{range.Start}-{range.End}.bin"))).ToArray();
        long done = offset;
        foreach (var chunk in chunks)
        {
            foreach (var path in new[] { chunk.Path, chunk.Path + ".part" })
            {
                if (!File.Exists(path)) continue;
                var size = new FileInfo(path).Length;
                if (size > chunk.Length || path == chunk.Path && size != chunk.Length)
                    File.Move(path, path + ".invalid-" + Guid.NewGuid().ToString("N"));
            }
            done += File.Exists(chunk.Path) ? chunk.Length : File.Exists(chunk.Path + ".part") ? new FileInfo(chunk.Path + ".part").Length : 0;
        }
        var meter = new TransferMeter(asset.Name + $" · hasta {options.Connections} conexiones", progress);
        meter.Report(done, total, 0, true);
        using var workers = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var appendGate = new SemaphoreSlim(1, 1);
        var next = -1; var toAppend = 0; long committedPrefix = offset;
        async Task CommitChunkAsync(string staging, string completed)
        {
            // MoveFileEx can expose the destination before releasing its Windows handle.
            // Publish under the same gate as readers so another worker cannot open it mid-rename.
            await appendGate.WaitAsync(workers.Token);
            try { File.Move(staging, completed); }
            finally { appendGate.Release(); }
        }
        async Task AppendReadyAsync()
        {
            await appendGate.WaitAsync(workers.Token);
            try
            {
                while (toAppend < chunks.Length && File.Exists(chunks[toAppend].Path))
                {
                    workers.Token.ThrowIfCancellationRequested();
                    var chunk = chunks[toAppend];
                    // Finish this small local copy before pausing. A committed prefix has no holes.
                    await using (var output = new FileStream(partial, FileMode.Append, FileAccess.Write, FileShare.Read, 128 * 1024, true))
                    await using (var input = File.OpenRead(chunk.Path))
                    { await input.CopyToAsync(output, CancellationToken.None); await output.FlushAsync(CancellationToken.None); }
                    Interlocked.Exchange(ref committedPrefix, chunk.End + 1); toAppend++;
                    File.Delete(chunk.Path);
                }
            }
            finally { appendGate.Release(); }
        }
        async Task FetchAsync(Chunk chunk)
        {
            // On resume an earlier worker can already have appended and removed this cached
            // chunk before its own worker starts. Read the prefix after the file check.
            if (File.Exists(chunk.Path) || chunk.End < Interlocked.Read(ref committedPrefix)) return;
            var staging = chunk.Path + ".part";
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var received = File.Exists(staging) ? new FileInfo(staging).Length : 0;
                    if (received == chunk.Length) { await CommitChunkAsync(staging, chunk.Path); return; }
                    using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
                    request.Headers.Range = new(chunk.Start + received, chunk.End);
                    using var response = await SendAsync(request, workers.Token);
                    if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.RequestedRangeNotSatisfiable) throw new RangesUnsupportedException();
                    response.EnsureSuccessStatusCode();
                    var range = response.Content.Headers.ContentRange;
                    if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != chunk.Start + received || range.To != chunk.End || range.Length != total)
                        throw new InvalidDataException("El servidor devolvió un fragmento incorrecto. Se conservó el progreso anterior.");
                    await using (var source = await response.Content.ReadAsStreamAsync(workers.Token))
                    await using (var output = new FileStream(staging, FileMode.Append, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        var buffer = new byte[1024 * 1024];
                        while (received < chunk.Length)
                        {
                            var read = await ReadAsync(source, buffer.AsMemory(0, (int)Math.Min(buffer.Length, chunk.Length - received)), workers.Token);
                            if (read == 0) throw new IOException("El fragmento llegó incompleto.");
                            await output.WriteAsync(buffer.AsMemory(0, read), workers.Token); received += read;
                            meter.Report(Interlocked.Add(ref done, read), total, read);
                        }
                        await output.FlushAsync(workers.Token);
                    }
                    await CommitChunkAsync(staging, chunk.Path); return;
                }
                catch (Exception ex) when (CanRetry(ex, workers.Token) && attempt < options.Retries)
                { await RetryAsync(asset.Name, attempt, progress, workers.Token); }
                catch (OperationCanceledException ex) when (!workers.IsCancellationRequested)
                { throw new HttpRequestException("Una conexión sigue sin responder después de los reintentos. El progreso se conservó; podés reanudar.", ex); }
            }
        }
        async Task WorkerAsync()
        {
            try
            {
                while (Interlocked.Increment(ref next) is var index && index < chunks.Length)
                { workers.Token.ThrowIfCancellationRequested(); await FetchAsync(chunks[index]); await AppendReadyAsync(); }
            }
            catch { workers.Cancel(); throw; }
        }
        var tasks = Enumerable.Range(0, options.Connections).Select(_ => WorkerAsync()).ToArray();
        try { await Task.WhenAll(tasks); }
        catch
        {
            if (!ct.IsCancellationRequested && tasks.Any(t => t.Exception?.InnerExceptions.Any(e => e is RangesUnsupportedException) == true)) throw new RangesUnsupportedException();
            var error = tasks.Where(t => t.IsFaulted).SelectMany(t => t.Exception!.InnerExceptions).FirstOrDefault();
            if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
        if (toAppend != chunks.Length || new FileInfo(partial).Length != total) throw new IOException("La descarga aún tiene fragmentos pendientes; podés reanudar.");
        meter.Report(total, total, 0, true);
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
    private sealed record Chunk(long Start, long End, string Path) { public long Length => End - Start + 1; }
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
        private readonly object gate = new();
        private readonly Queue<(long Time, int Bytes)> samples = new();
        private long firstByte = -1, lastReport, lastDone;
        public void Report(long done, long? total, int count, bool force = false)
        {
            lock (gate)
            {
            var now = clock.ElapsedMilliseconds;
            done = lastDone = Math.Max(lastDone, done);
            if (count > 0) { if (firstByte < 0) firstByte = now; samples.Enqueue((now, count)); }
            while (samples.TryPeek(out var sample) && sample.Time < now - 2000) samples.Dequeue();
            if (!force && now - lastReport < 200) return;
            lastReport = now;
            // Recent transfer rate excludes DNS/TLS startup and does not average an earlier stall
            // into the rest of a multi-gigabyte download.
            var seconds = Math.Max((now - Math.Max(firstByte, now - 2000)) / 1000d, .1);
            progress?.Report(new(name, done, total, samples.Sum(sample => (long)sample.Bytes) / seconds));
            }
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
    public bool IsPrepared(AppSettings settings)
    {
        try { GpuProfiles.ValidateBackend(settings); }
        catch (InvalidDataException) { return false; }
        return File.Exists(paths.AgentExe) && File.Exists(paths.GitExe) && File.Exists(paths.NodeExe) && File.Exists(paths.ActiveModelPath(settings))
            && (GitHubConnection.SavedAccount(paths) is null || File.Exists(paths.GitHubCredentialExe))
            && (settings.EngineMode == EngineMode.NInfer5080 ? paths.HasNinferEngine(settings) : File.Exists(paths.KvMemEngineExe(settings)));
    }
    public async Task PrepareAsync(AppSettings settings, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        GpuProfiles.ValidateBackend(settings);
        if (!settings.LicenseAccepted) throw new InvalidOperationException("Revisá las licencias y confirmá su aceptación antes de descargar.");
        if (settings.EngineMode == EngineMode.NInfer5080 && !string.IsNullOrWhiteSpace(settings.NinferEnginePath) && !paths.HasNinferEngine(settings)) throw new InvalidOperationException("No se encuentra el motor importado. Elegí un archivo válido o restaurá el motor automático.");
        await PrepareComponentsAsync(ReleaseProfile.ComponentsFor(settings).Where(asset => asset != ReleaseProfile.NativeEngine || string.IsNullOrWhiteSpace(settings.NinferEnginePath)), progress, ct);
        if (GitHubConnection.SavedAccount(paths) is not null) await PrepareComponentsAsync([ReleaseProfile.GitHubCredentialManager], progress, ct);
        progress?.Report(new("Comprobando el modelo"));
        var modelAsset = ReleaseProfile.ModelFor(settings.EngineMode);
        if (!string.IsNullOrWhiteSpace(paths.ImportedModelPath(settings)))
        {
            if (!await VerifiedDownloader.MatchesAsync(paths.ActiveModelPath(settings), modelAsset, ct)) throw new InvalidDataException("El archivo elegido no es el modelo de esta versión o está incompleto.");
        }
        else await downloader.DownloadAsync(modelAsset, paths.ActiveModelPath(settings), progress, ct);
        paths.SaveSettings(settings);
    }
    public Task PrepareGitHubAsync(IProgress<PreparationProgress>? progress, CancellationToken ct) => PrepareComponentsAsync([ReleaseProfile.Git, ReleaseProfile.GitHubCredentialManager], progress, ct);
    private async Task PrepareComponentsAsync(IEnumerable<DownloadAsset> assets, IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        foreach (var asset in assets)
        {
            var archive = Path.Combine(paths.Downloads, asset.FileName);
            await downloader.DownloadAsync(asset, archive, progress, ct);
            var target = Path.Combine(paths.Runtime, asset.Id);
            var markerName = ".installed-" + asset.Sha256[..16] + ".json";
            var marker = Path.Combine(target, markerName);
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
            AppPaths.AtomicWrite(Path.Combine(staging, markerName), System.Text.Json.JsonSerializer.Serialize(hashes));
            if (Directory.Exists(target)) Directory.Move(target, target + ".previous-" + Guid.NewGuid().ToString("N"));
            Directory.Move(staging, target);
        }
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
