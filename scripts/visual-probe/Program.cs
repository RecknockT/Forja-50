using System.Diagnostics;
using System.Text.Json;
using MiIALocal.Core;

// Manual integration probe: never part of CI; explicit arguments enable downloads/GPU.
if (args.Length < 2 || !Directory.Exists(args[0]) || args[1] is not ("download" or "run"))
    throw new ArgumentException("Uso: DATA_ROOT_EXISTENTE download|run [--assistant] [--allow-gpu]");
var paths = new AppPaths(Path.GetFullPath(args[0]));
var hardware = await HardwareDetector.DetectAsync(default);
if (hardware.Problem is { } problem) throw new InvalidOperationException(problem);
var gpu = hardware.SelectedGpu!;
var assistant = args.Contains("--assistant");
using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
var downloader = new VerifiedDownloader(http);
var lastUpdate = Stopwatch.StartNew();
var progress = new Progress<PreparationProgress>(p =>
{
    if (lastUpdate.Elapsed.TotalSeconds < 5) return;
    lastUpdate.Restart(); Console.WriteLine(JsonSerializer.Serialize(p));
});
if (args[1] == "download")
{
    foreach (var model in VisualCatalog.For(false, gpu, assistant))
    {
        Console.WriteLine("Descarga verificada: " + model.Asset.Name);
        await downloader.DownloadAsync(model.Asset, VisualCatalog.PathFor(paths, model), progress, default);
    }
    Console.WriteLine("PASS archivos descargados y SHA-256 verificado");
    return;
}
if (!args.Contains("--allow-gpu")) throw new InvalidOperationException("La prueba física requiere --allow-gpu.");
var memory = await HardwareDetector.ReadMemoryAsync(gpu.Index, default);
if (memory.FreeMiB < 11000) throw new InvalidOperationException("No hay 11 GiB libres. No se cierra ninguna otra aplicación.");
var settings = new VisualSettings { HiDreamLicenseAccepted = true, EnhancePrompt = assistant };
await using var runtime = new VisualRuntime(paths, http);
await runtime.StartAsync(settings, gpu, default);
var report = new List<object>();
long peak = memory.UsedMiB;
using var sampling = new CancellationTokenSource();
var monitor = Task.Run(async () =>
{
    try { while (true) { var value = await HardwareDetector.ReadMemoryAsync(gpu.Index, sampling.Token); Interlocked.Exchange(ref peak, Math.Max(peak, value.UsedMiB)); await Task.Delay(1000, sampling.Token); } }
    catch (OperationCanceledException) { }
});
try
{
    string? reference = null;
    foreach (var side in new[] { 1024, 2048 })
    {
        var job = await runtime.GenerateAsync(new(VisualTask.Image,
            "A clean 1990s anime cel illustration of a white-haired elf mage wearing a white and gold cape, holding a wooden magical staff, flying above green hills. Clear line art, flat cel shading, blue sky. No lettering.",
            side, side, 20261021, HighPrecision: gpu.IsRtx3090, EnhancePrompt: assistant), null, progress, default);
        report.Add(new { job.Id, job.TotalSeconds, job.Files, job.Request, PeakTotalMiB = peak, Assistant = assistant });
        reference = job.Files.First(f => Path.GetExtension(f) == ".png");
        Console.WriteLine("PASS generación " + side + ": " + job.TotalSeconds + " s " + reference);
    }
    var edit = await runtime.GenerateAsync(new(VisualTask.Edit,
        "Change only the white cape to a vivid red cape. Preserve the same elf, face, white hair, staff, pose, framing, background, and 1990s anime cel style. Do not add writing.",
        2048, 2048, 20261021, HighPrecision: gpu.IsRtx3090, EnhancePrompt: assistant), reference, progress, default);
    report.Add(new { edit.Id, edit.TotalSeconds, edit.Files, edit.Request, PeakTotalMiB = peak, Assistant = assistant });
    Console.WriteLine("PASS edición: " + edit.TotalSeconds + " s");
}
finally
{
    sampling.Cancel(); await monitor;
    await runtime.StopAsync();
    var destination = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
    Directory.CreateDirectory(destination);
    File.WriteAllText(Path.Combine(destination, "hidream-probe-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json"), JsonSerializer.Serialize(new { Gpu = gpu.Name, RamGiB = hardware.RamGiB, PeakTotalMiB = peak, Runs = report }, new JsonSerializerOptions { WriteIndented = true }));
}
