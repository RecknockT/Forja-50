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
await Test("Descarga: parcial completo sin tamaño se activa sin pedir Range", async () =>
{
    var bytes = Encoding.UTF8.GetBytes("node-completo-verificado"); var destination = Path.Combine(root, "unknown-size.bin");
    await File.WriteAllBytesAsync(destination + ".part", bytes);
    using var client = new HttpClient(new Handler(_ => throw new Exception("No debe descargar un parcial completo válido.")));
    await new VerifiedDownloader(client).DownloadAsync(Asset(bytes) with { Bytes = null }, destination, null, default);
    Assert((await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "Se perdió el parcial válido.");
});
await Test("Descarga: 416 permite reiniciar un parcial incompatible", async () =>
{
    var bytes = Encoding.UTF8.GetBytes("archivo-correcto"); var destination = Path.Combine(root, "range-416.bin");
    await File.WriteAllTextAsync(destination + ".part", "parcial-incompatible"); var requests = 0;
    using var client = new HttpClient(new Handler(request =>
    {
        requests++;
        if (requests == 1) { Assert(request.Headers.Range is not null, "Falta intento de reanudación."); return new(HttpStatusCode.RequestedRangeNotSatisfiable); }
        Assert(request.Headers.Range is null, "Se repitió el Range rechazado.");
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    }));
    await new VerifiedDownloader(client).DownloadAsync(Asset(bytes) with { Bytes = null }, destination, null, default);
    Assert(requests == 2 && (await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "El error 416 deja la descarga bloqueada.");
});
await Test("Portable: modelo predeterminado sigue a la carpeta de datos", () =>
{
    var before = new AppPaths(Path.Combine(root, "portable-original", "datos")); before.SaveSettings(new());
    var moved = new AppPaths(Path.Combine(root, "portable-movida", "datos")); File.Copy(before.SettingsFile, moved.SettingsFile);
    Assert(moved.ModelPath(moved.LoadSettings()) == Path.Combine(moved.Root, "models", ReleaseProfile.Model.FileName), "El modelo conserva la ruta de la portable anterior.");
    return Task.CompletedTask;
});
await Test("Ajustes inválidos: recuperar inicio y conservar la copia anterior", () =>
{
    foreach (var invalid in new[] { "{incompleto", "{\"ContextTokens\":65536,\"Tuning\":{\"HistoryTokens\":65536}}" })
    {
        var paths = new AppPaths(Path.Combine(root, Guid.NewGuid().ToString("N"))); File.WriteAllText(paths.SettingsFile, invalid);
        var settings = paths.LoadSettings(); var backups = Directory.GetFiles(paths.Root, "settings.invalid-*.json");
        Assert(settings.ContextTokens == 65536 && settings.Tuning.HistoryTokens == 24576 && !settings.LicenseAccepted, "No se restauró un perfil válido.");
        Assert(backups.Length == 1 && File.ReadAllText(backups[0]) == invalid && paths.SettingsRecoveryMessage is not null, "No se conservó o informó la configuración anterior.");
    }
    return Task.CompletedTask;
});
await Test("Almacenamiento: verificar unidad del programa y unidad del modelo", () =>
{
    var paths = new AppPaths(Path.Combine(root, "storage")); var settings = new AppSettings { ModelDirectory = "Z:\\models" };
    var requirements = PreparationStorage.Requirements(paths, settings);
    Assert(requirements.Count == 2 && requirements[Path.GetPathRoot(paths.Root)!] == 4_000_000_000 && requirements["Z:\\"] == 30_000_000_000, "Se olvidó la unidad del programa.");
    settings.ImportedModel = "Z:\\model.ninfer";
    Assert(PreparationStorage.Requirements(paths, settings).Count == 1, "Un modelo importado exige espacio de descarga en la unidad equivocada.");
    return Task.CompletedTask;
});
await Test("Placas: perfiles independientes y solo hardware admitido", async () =>
{
    var a = GpuProfiles.For(new(0, "NVIDIA GeForce RTX 5080", 16303, 14000, "test"));
    var b = GpuProfiles.For(new(0, "NVIDIA GeForce RTX 5070 Ti", 16303, 14000, "test"));
    Assert(a.Id != b.Id && a.ContextTokens == 65536 && b.ContextTokens == 65536 && a.BasicContextProbeValidated && !b.BasicContextProbeValidated, "Se confundieron perfiles o validaciones físicas.");
    var edited = a.CreateTuning(); edited.MaxOutput = 4096;
    Assert(b.CreateTuning().MaxOutput == 8192, "Los perfiles comparten ajustes mutables.");
    await Throws<InvalidDataException>(() => { GpuProfiles.For(new(0, "NVIDIA GeForce RTX 5080 Laptop GPU", 16303, 14000, "test")); return Task.CompletedTask; });
});
await Test("Placas: conservar ajustes manuales anteriores al detector", () =>
{
    var paths = new AppPaths(Path.Combine(root, "legacy-profile"));
    File.WriteAllText(paths.SettingsFile, "{\"ContextTokens\":32768,\"Tuning\":{\"MaxOutput\":4096,\"ThinkingTokens\":512}}");
    var settings = paths.LoadSettings(); GpuProfiles.Apply(settings, new(0, "NVIDIA GeForce RTX 5070 Ti", 16303, 14000, "test"));
    Assert(!settings.UseRecommendedProfile && settings.ContextTokens == 32768 && settings.Tuning.MaxOutput == 4096, "El detector sobrescribió una personalización.");
    settings.UseRecommendedProfile = true; GpuProfiles.Apply(settings, new(0, "NVIDIA GeForce RTX 5080", 16303, 14000, "test"));
    Assert(settings.ContextTokens == 65536 && settings.Tuning.MaxOutput == 8192 && settings.HardwareProfileId == "rtx5080-desktop-16gb", "No se restauró el perfil de la placa detectada.");
    return Task.CompletedTask;
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
