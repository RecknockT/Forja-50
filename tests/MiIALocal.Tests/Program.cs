using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MiIALocal.Core;

if (args.Contains("--integration")) return await Integration.RunAsync(args);
if (args.Contains("--context-probe")) return await ContextProbe.RunAsync(args);

var failures = 0;
var root = Path.Combine(Path.GetTempPath(), "MiIALocal-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
async Task Test(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Se esperaba " + typeof(T).Name); }
DownloadAsset Asset(byte[] bytes) => new("test", "Prueba", "https://example.invalid/model", Convert.ToHexStringLower(SHA256.HashData(bytes)), "model.bin", bytes.Length);

await Test("Hardware: admitir solo las dos placas de escritorio", () =>
{
    var names = new[] { "NVIDIA GeForce RTX 5080", "NVIDIA GeForce RTX 5070 Ti", "NVIDIA GeForce RTX 5080 Laptop GPU", "NVIDIA GeForce RTX 5070 Ti Laptop GPU", "NVIDIA GeForce RTX 5070", "NVIDIA GeForce RTX 5090" };
    var rows = names.Select((n, i) => $"{i}, {n}, 16303, 14000, 617.42");
    var gpus = HardwareDetector.ParseGpus(string.Join('\n', rows));
    Assert(gpus.Count == 6 && gpus.Count(g => g.IsSupported) == 2, "Se admitió hardware fuera del alcance.");
    Assert(new HardwareReport(gpus, 31.2).Problem is null, "RAM reservada de un equipo de 32 GB no debe bloquear.");
    Assert(new HardwareReport(gpus, 15.8).Problem is not null, "No se detectó RAM insuficiente.");
    Assert(new HardwareReport([new(0, names[0], 8000, 7000, "617.42")], 32).Problem is not null, "No se detectó VRAM insuficiente.");
    return Task.CompletedTask;
});
await Test("ZIP: rechazar traversal y rutas absolutas", async () =>
{
    foreach (var entry in new[] { "../escape.txt", "a/../../escape.txt", "C:\\escape.txt", "\\escape.txt" })
        await Throws<InvalidDataException>(() => { SafeArchive.ResolveEntry(root, entry); return Task.CompletedTask; });
    Assert(SafeArchive.ResolveEntry(root, "a/archivo.txt").StartsWith(root), "Ruta normal inválida.");
});
await Test("ZIP: nunca escribir fuera del destino", async () =>
{
    var zip = Path.Combine(root, "bad.zip");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { var entry = archive.CreateEntry("../escape.txt"); using var writer = new StreamWriter(entry.Open()); writer.Write("escape"); }
    await Throws<InvalidDataException>(() => { SafeArchive.Extract(zip, Path.Combine(root, "extract"), default); return Task.CompletedTask; });
    Assert(!File.Exists(Path.Combine(root, "escape.txt")), "El ZIP escribió fuera del destino.");
});
await Test("Descarga: reanudar con Range y comprobar SHA-256", async () =>
{
    var bytes = Encoding.UTF8.GetBytes("modelo-con-contenido-verificable-5080"); var asset = Asset(bytes);
    var destination = Path.Combine(root, "resumed.bin"); await File.WriteAllBytesAsync(destination + ".part", bytes[..9]);
    using var client = new HttpClient(new Handler(request =>
    {
        Assert(request.Headers.Range?.Ranges.Single().From == 9, "No se pidió el fragmento correcto.");
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[9..]) };
        response.Content.Headers.ContentRange = new(9, bytes.Length - 1, bytes.Length); return response;
    }));
    await new VerifiedDownloader(client).DownloadAsync(asset, destination, null, default);
    Assert((await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "Descarga corrupta.");
});
await Test("Descarga: servidor ignora Range sin duplicar bytes", async () =>
{
    var bytes = Encoding.UTF8.GetBytes("contenido-completo"); var destination = Path.Combine(root, "ignored-range.bin");
    await File.WriteAllBytesAsync(destination + ".part", bytes[..4]);
    using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
    await new VerifiedDownloader(client).DownloadAsync(Asset(bytes), destination, null, default);
    Assert((await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "Se anexó la copia completa a una parcial.");
});
await Test("Descarga: hash incorrecto no se activa", async () =>
{
    var destination = Path.Combine(root, "corrupt.bin"); var correct = Encoding.UTF8.GetBytes("abcdef");
    using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ghijkl")) }));
    await Throws<InvalidDataException>(() => new VerifiedDownloader(client).DownloadAsync(Asset(correct), destination, null, default));
    Assert(!File.Exists(destination), "Se activó un archivo incorrecto.");
});
await Test("Descarga: Range incorrecto conserva el parcial", async () =>
{
    var bytes = Encoding.UTF8.GetBytes("contenido-completo"); var destination = Path.Combine(root, "wrong-range.bin"); await File.WriteAllBytesAsync(destination + ".part", bytes[..4]);
    using var client = new HttpClient(new Handler(_ => { var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[5..]) }; response.Content.Headers.ContentRange = new(5, bytes.Length - 1, bytes.Length); return response; }));
    await Throws<InvalidDataException>(() => new VerifiedDownloader(client).DownloadAsync(Asset(bytes), destination, null, default));
    Assert(new FileInfo(destination + ".part").Length == 4, "Se modificó un parcial con datos incorrectos.");
});
await Test("Proyectos: ruta Unicode viaja completa en el enlace", () =>
{
    var project = Path.Combine(root, "Proyecto á con espacios"); var url = AgentConfiguration.ProjectUrl(18201, project);
    var encoded = new Uri(url).Segments[1].Trim('/').Replace('-', '+').Replace('_', '/'); encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
    Assert(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)) == Path.GetFullPath(project), "Se alteró la ruta."); return Task.CompletedTask;
});
await Test("Configuración: solo proveedor local y contexto de referencia", () =>
{
    using var json = System.Text.Json.JsonDocument.Parse(AgentConfiguration.Build(12345, "test-key", "C:\\instrucciones.md"));
    Assert(json.RootElement.GetProperty("enabled_providers")[0].GetString() == "local", "Proveedor de nube habilitado.");
    Assert(json.RootElement.GetProperty("provider").GetProperty("local").GetProperty("models").GetProperty("qwen-local").GetProperty("limit").GetProperty("context").GetInt32() == 65536, "No se activó el contexto predeterminado de 64K."); return Task.CompletedTask;
});
await Test("Contexto 64K: agente y motor coinciden sin duplicar presupuesto GPU", async () =>
{
    using var json = System.Text.Json.JsonDocument.Parse(AgentConfiguration.Build(12345, "key", "instructions.md", 65536));
    Assert(json.RootElement.GetProperty("provider").GetProperty("local").GetProperty("models").GetProperty("qwen-local").GetProperty("limit").GetProperty("context").GetInt32() == 65536, "El agente conserva un límite diferente.");
    var arguments = RuntimeSupervisor.EngineArguments("model.ninfer", 0, 12345, "key", 65536);
    string Value(string flag) => arguments[Array.IndexOf(arguments, flag) + 1];
    Assert(Value("--max-context") == "65536" && Value("--kvmem-host-mib") == "4096", "Falta ampliar contexto o RAM.");
    Assert(Value("--kvmem-budget") == "24576" && Value("--kvmem-gen-reserve") == "8192", "Se aumentó el presupuesto GPU sin prueba física.");
    await Throws<InvalidDataException>(() => { ContextProfile.For(262144); return Task.CompletedTask; });
});
await Test("Ajustes nuevos usan 64K y se conserva la alternativa 32K", () =>
{
    var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"LastProject\":\"\"}")!;
    Assert(settings.ContextTokens == 65536, "Los ajustes nuevos no usan 64K.");
    settings.ContextTokens = 32768;
    Assert(ContextProfile.For(settings.ContextTokens).HostCacheMiB == 2048, "Se perdió la alternativa de 32K.");
    return Task.CompletedTask;
});
await Test("Avanzada: rechazar presupuestos incompatibles y muestreo inválido", async () =>
{
    foreach (var tuning in new[] { new EngineTuning { HistoryTokens = 65536 }, new EngineTuning { HostCacheMiB = 100000 }, new EngineTuning { ThinkingTokens = 9000 }, new EngineTuning { TopK = 21 }, new EngineTuning { Temperature = double.NaN }, new EngineTuning { PrefillChunk = 100 } })
        await Throws<InvalidDataException>(() => { tuning.Validate(65536); return Task.CompletedTask; });
});
await Test("Avanzada: parámetros reales, límite del agente y números con punto", () =>
{
    var previous = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("es-AR");
        var tuning = new EngineTuning { MaxOutput = 4096, ThinkingTokens = 512, TopP = .9, AgentTemperature = .4, HostCacheMiB = 3072 };
        var arguments = RuntimeSupervisor.EngineArguments("model.ninfer", 0, 12345, "key", 65536, tuning);
        string Value(string flag) => arguments[Array.IndexOf(arguments, flag) + 1];
        Assert(Value("--top-p") == "0.9" && Value("--default-max-tokens") == "4096" && Value("--kvmem-host-mib") == "3072", "Los argumentos ignoran ajustes o usan coma decimal.");
        using var json = System.Text.Json.JsonDocument.Parse(AgentConfiguration.Build(12345, "key", "instructions.md", 65536, tuning));
        Assert(json.RootElement.GetProperty("provider").GetProperty("local").GetProperty("models").GetProperty("qwen-local").GetProperty("limit").GetProperty("output").GetInt32() == 4096, "El agente y el motor tienen límites diferentes.");
        Assert(json.RootElement.GetProperty("agent").GetProperty("build").GetProperty("temperature").GetDouble() == .4, "No se aplicó temperatura del agente.");
        tuning.ThinkingTokens = 0;
        var noThinking = RuntimeSupervisor.EngineArguments("model.ninfer", 0, 12345, "key", 65536, tuning);
        Assert(noThinking.Contains("--no-thinking") && noThinking[Array.IndexOf(noThinking, "--default-thinking-budget") + 1] == "1", "Desactivar razonamiento envía un presupuesto cero no admitido.");
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    return Task.CompletedTask;
});
if (OperatingSystem.IsWindows())
{
    await Test("Proceso: salida rápida 0 y Unicode sin falso error", async () =>
    {
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        await using (var process = OwnedProcess.Start(shell, ["-NoProfile", "-Command", "[Console]::OutputEncoding = [Text.Encoding]::UTF8; Write-Output 'Revisión correcta'; exit 0"], root, Path.Combine(root, "early")))
        { var deadline = DateTime.UtcNow.AddSeconds(15); while (!process.HasExited && DateTime.UtcNow < deadline) await Task.Delay(100); Assert(process.HasExited, "El proceso de prueba no terminó."); }
        Assert((await File.ReadAllTextAsync(Path.Combine(root, "early.out.log"))).Contains("Revisión correcta"), "Se dañaron los caracteres.");
    });
}
Console.WriteLine($"RESULTADO: {failures} fallos. Registros: {root}");
return failures == 0 ? 0 : 1;

sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
}
