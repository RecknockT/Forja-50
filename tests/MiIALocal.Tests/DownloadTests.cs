using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using MiIALocal.Core;

static class DownloadTests
{
    public static async Task RunAsync(string root, Action<bool, string> assert, Func<string, Func<Task>, Task> test)
    {
        var bytes = new byte[100003]; new Random(50).NextBytes(bytes);
        var asset = new DownloadAsset("test", "Modelo", "https://example.invalid/model", Convert.ToHexStringLower(SHA256.HashData(bytes)), "model.bin", bytes.Length);
        var options = new DownloadOptions(4, 8192, 1, 2, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1));
        HttpResponseMessage Range(HttpRequestMessage request)
        {
            var range = request.Headers.Range!.Ranges.Single(); var start = range.From!.Value; var end = range.To ?? bytes.Length - 1;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[(int)start..((int)end + 1)]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, bytes.Length); return response;
        }
        await test("Paralelo: cuatro conexiones, orden correcto, velocidad y ETA", async () =>
        {
            var active = 0; var peak = 0; var destination = Path.Combine(root, "parallel.bin");
            var reports = new List<PreparationProgress>();
            using var client = new HttpClient(new AsyncHandler(async (request, ct) => {
                var current = Interlocked.Increment(ref active); int previous;
                do { previous = peak; if (current <= previous) break; } while (Interlocked.CompareExchange(ref peak, current, previous) != previous);
                await Task.Delay(request.Headers.Range!.Ranges.Single().From == 0 ? 30 : 5, ct); Interlocked.Decrement(ref active); return Range(request);
            }));
            await new VerifiedDownloader(client, options).DownloadAsync(asset, destination, new InlineProgress(p => reports.Add(p)), default);
            assert(peak == 4 && (await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "No hubo paralelismo real o se mezclaron fragmentos.");
            assert(reports.Any(p => p.BytesPerSecond > 0 && p.Remaining is not null && p.TransferDetails.Contains("MB/s")), "Falta velocidad o ETA.");
        });
        await test("Descarga continua: conexiones rápidas no esperan al primer fragmento", async () =>
        {
            var destination = Path.Combine(root, "continuous.bin");
            var advanced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var client = new HttpClient(new AsyncHandler(async (request, ct) => {
                var from = request.Headers.Range!.Ranges.Single().From;
                if (from == 0) await advanced.Task.WaitAsync(ct);
                if (from >= 32768) advanced.TrySetResult();
                await Task.Delay(2, ct); return Range(request);
            }));
            await new VerifiedDownloader(client, options).DownloadAsync(asset, destination, null, default);
            assert(advanced.Task.IsCompleted && (await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "Se esperó a completar el lote anterior.");
        });
        await test("Pausa: guardar fragmentos fuera de orden y no descargarlos otra vez", async () =>
        {
            var destination = Path.Combine(root, "out-of-order.bin"); using var pause = new CancellationTokenSource();
            using (var client = new HttpClient(new AsyncHandler(async (request, ct) => {
                var from = request.Headers.Range!.Ranges.Single().From;
                if (from == 0) { await Task.Delay(Timeout.Infinite, ct); }
                if (from >= 65536) { pause.Cancel(); ct.ThrowIfCancellationRequested(); }
                await Task.Delay(2, ct); return Range(request);
            })))
            {
                try { await new VerifiedDownloader(client, options).DownloadAsync(asset, destination, null, pause.Token); throw new Exception("No se pausó."); } catch (OperationCanceledException) { }
            }
            var directory = VerifiedDownloader.ChunkDirectory(asset, destination, options.ChunkBytes);
            var saved = Directory.GetFiles(directory, "*.bin").Select(path => long.Parse(Path.GetFileName(path).Split('-')[0])).ToHashSet();
            assert(saved.Count > 0, "Se perdieron los fragmentos adelantados al pausar.");
            using var resumed = new HttpClient(new AsyncHandler((request, _) => {
                assert(!saved.Contains(request.Headers.Range!.Ranges.Single().From!.Value), "Se repitió un fragmento ya guardado."); return Task.FromResult(Range(request));
            }));
            await new VerifiedDownloader(resumed, options).DownloadAsync(asset, destination, null, default);
            assert((await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "Reanudación fuera de orden corrupta.");
        });
        await test("Fragmento incompleto: reanudar desde su byte guardado", async () =>
        {
            var destination = Path.Combine(root, "chunk-resume.bin"); var directory = VerifiedDownloader.ChunkDirectory(asset, destination, options.ChunkBytes);
            Directory.CreateDirectory(directory); await File.WriteAllBytesAsync(Path.Combine(directory, "0-8191.bin.part"), bytes[..2000]);
            var resumed = false;
            using var client = new HttpClient(new AsyncHandler((request, _) => {
                var range = request.Headers.Range!.Ranges.Single(); if (range.To == 8191) { assert(range.From == 2000, "Se perdió el progreso dentro del fragmento."); resumed = true; }
                return Task.FromResult(Range(request));
            }));
            await new VerifiedDownloader(client, options).DownloadAsync(asset, destination, null, default);
            assert(resumed && (await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "No se reanudó el fragmento parcial.");
        });
        await test("Paralelo: reanudar parcial creado por versión anterior", async () =>
        {
            var destination = Path.Combine(root, "old-part.bin"); await File.WriteAllBytesAsync(destination + ".part", bytes[..9011]); var minimum = long.MaxValue;
            using var client = new HttpClient(new AsyncHandler((request, _) => { minimum = Math.Min(minimum, request.Headers.Range!.Ranges.Single().From!.Value); return Task.FromResult(Range(request)); }));
            await new VerifiedDownloader(client, options).DownloadAsync(asset, destination, null, default);
            assert(minimum == 9011 && (await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "Se perdió el progreso de la versión anterior.");
        });
        await test("Paralelo: servidor sin Range vuelve a una conexión", async () =>
        {
            var destination = Path.Combine(root, "fallback.bin"); await File.WriteAllBytesAsync(destination + ".part", bytes[..12]);
            using var client = new HttpClient(new AsyncHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })));
            await new VerifiedDownloader(client, options).DownloadAsync(asset, destination, null, default);
            assert((await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "Fallback corrupto.");
        });
        await test("Paralelo: pausa conserva bloques y reanuda sin repetirlos", async () =>
        {
            var destination = Path.Combine(root, "paused.bin"); using var pause = new CancellationTokenSource();
            using (var client = new HttpClient(new AsyncHandler((request, ct) => {
                if (request.Headers.Range!.Ranges.Single().From >= 32768) { pause.Cancel(); ct.ThrowIfCancellationRequested(); }
                return Task.FromResult(Range(request));
            })))
            {
                try { await new VerifiedDownloader(client, options).DownloadAsync(asset, destination, null, pause.Token); throw new Exception("No se pausó."); } catch (OperationCanceledException) { }
            }
            var prefix = File.Exists(destination + ".part") ? new FileInfo(destination + ".part").Length : 0;
            var chunkDirectory = VerifiedDownloader.ChunkDirectory(asset, destination, options.ChunkBytes);
            var persisted = prefix + Directory.GetFiles(chunkDirectory).Sum(path => new FileInfo(path).Length);
            assert(persisted > 0 && !File.Exists(destination), "La pausa perdió el progreso o activó un modelo incompleto.");
            using var resumed = new HttpClient(new AsyncHandler((request, _) => { assert(request.Headers.Range!.Ranges.Single().From >= prefix, "Se repitió el prefijo guardado."); return Task.FromResult(Range(request)); }));
            await new VerifiedDownloader(resumed, options).DownloadAsync(asset, destination, null, default);
            assert((await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "Reanudación corrupta.");
        });
        await test("Paralelo: detectar stream trabado y recuperar con reintento", async () =>
        {
            var blocked = 0; var requests = 0; var destination = Path.Combine(root, "stalled.bin");
            using var client = new HttpClient(new AsyncHandler((request, _) => {
                Interlocked.Increment(ref requests); var response = Range(request);
                if (Interlocked.CompareExchange(ref blocked, 1, 0) == 0) { var range = response.Content.Headers.ContentRange; response.Content.Dispose(); response.Content = new StreamContent(new StalledStream()); response.Content.Headers.ContentRange = range; }
                return Task.FromResult(response);
            }));
            await new VerifiedDownloader(client, options).DownloadAsync(asset, destination, null, default);
            assert(requests > 13 && (await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "No se recuperó una conexión estancada.");
        });
        await test("Reintentos: límite ante 503 conserva el parcial", async () =>
        {
            var calls = 0; var destination = Path.Combine(root, "unavailable.bin"); await File.WriteAllBytesAsync(destination + ".part", bytes[..12]);
            using var client = new HttpClient(new AsyncHandler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }));
            try { await new VerifiedDownloader(client, options with { ParallelThreshold = long.MaxValue }).DownloadAsync(asset, destination, null, default); throw new Exception("Se aceptó HTTP 503."); } catch (HttpRequestException) { }
            assert(calls == 3 && new FileInfo(destination + ".part").Length == 12, "Reintentos ilimitados o progreso perdido.");
        });
        await test("Paralelo: rechazar Content-Range incorrecto", async () =>
        {
            var destination = Path.Combine(root, "bad-parallel.bin");
            using var client = new HttpClient(new AsyncHandler((request, _) => { var response = Range(request); response.Content.Headers.ContentRange = new(0, 3, bytes.Length); return Task.FromResult(response); }));
            try { await new VerifiedDownloader(client, options).DownloadAsync(asset, destination, null, default); throw new Exception("Se aceptó un rango incorrecto."); } catch (InvalidDataException) { }
            assert(!File.Exists(destination), "Se activó una descarga corrupta.");
        });
        await test("Una conexión: recuperar timeout de lectura sin perder datos", async () =>
        {
            var calls = 0; var destination = Path.Combine(root, "serial-stall.bin");
            using var client = new HttpClient(new AsyncHandler((_, _) => {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = ++calls == 1 ? new StreamContent(new StalledStream()) : new ByteArrayContent(bytes);
                response.Content.Headers.ContentLength = bytes.Length; return Task.FromResult(response);
            }));
            await new VerifiedDownloader(client, options with { ParallelThreshold = long.MaxValue }).DownloadAsync(asset, destination, null, default);
            assert(calls == 2 && (await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "La conexión única quedó trabada.");
        });
        await test("Timeout agotado: informar error de red, no pausa del usuario", async () =>
        {
            var destination = Path.Combine(root, "timeout-limit.bin");
            using var client = new HttpClient(new AsyncHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) })));
            try { await new VerifiedDownloader(client, options with { ParallelThreshold = long.MaxValue, Retries = 1 }).DownloadAsync(asset, destination, null, default); throw new Exception("No se limitó el timeout."); } catch (HttpRequestException) { }
            assert(File.Exists(destination + ".part") && !File.Exists(destination), "No se conservó un parcial seguro.");
        });
        await test("Paralelo: SHA-256 incorrecto nunca activa el modelo", async () =>
        {
            var destination = Path.Combine(root, "parallel-hash.bin");
            using var client = new HttpClient(new AsyncHandler((request, _) => Task.FromResult(Range(request))));
            try { await new VerifiedDownloader(client, options).DownloadAsync(asset with { Sha256 = new string('0', 64) }, destination, null, default); throw new Exception("Se aceptó SHA-256 incorrecto."); } catch (InvalidDataException) { }
            assert(!File.Exists(destination) && Directory.GetFiles(root, "parallel-hash.bin.part.invalid-*").Length == 1, "Se activó un modelo corrupto.");
        });
    }
    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request, cancellationToken); }
    private sealed class InlineProgress(Action<PreparationProgress> report) : IProgress<PreparationProgress>
    { public void Report(PreparationProgress value) => report(value); }
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
