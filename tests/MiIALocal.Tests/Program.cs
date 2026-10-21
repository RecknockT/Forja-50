using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MiIALocal.Core;

if (args.Contains("--early-exit-fixture"))
{
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine("Revisión correcta");
    return 0;
}
if (args.Contains("--integration")) return await Integration.RunAsync(args);
if (args.Contains("--context-probe")) return await ContextProbe.RunAsync(args);
if (args.Contains("--github-component-probe")) return await GitHubProbe.RunAsync();

if (args.Contains("--update-probe"))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    foreach (var portable in new[] { false, true })
    {
        var update = await AppUpdates.CheckAsync(client, "0.1.3-dev", portable, default);
        if (update is null) throw new Exception("La publicación pública no se detectó.");
        Console.WriteLine($"PASS GitHub público: {update.Version} / {update.Asset.FileName} / SHA-256 disponible");
    }
    return 0;
}
if (args.Contains("--download-probe"))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    await Task.WhenAll(Enumerable.Range(0, 4).Select(async index => {
        var start = index * 65536L;
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseProfile.Model.Url);
        request.Headers.Range = new(start, start + 65535);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != start || response.Content.Headers.ContentRange?.To != start + 65535 || response.Content.Headers.ContentRange?.Length != ReleaseProfile.Model.Bytes)
            throw new Exception("El servidor no devolvió el fragmento solicitado; no se leerá el cuerpo.");
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var body = await response.Content.ReadAsStreamAsync(ct.Token);
        await body.ReadExactlyAsync(new byte[65536], ct.Token);
        Console.WriteLine($"PASS Hugging Face: fragmento {index + 1}, HTTP 206, 64 KiB en {clock.Elapsed.TotalSeconds:0.0}s");
    }));
    return 0;
}
if (args.Contains("--download-speed-probe"))
{
    foreach (var connections in new[] { 1, 4, 8, 16 })
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, connections).Select(async index => {
            var start = index * 1048576L;
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseProfile.NinferModel.Url);
            request.Headers.Range = new(start, start + 1048575);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != start || response.Content.Headers.ContentRange?.To != start + 1048575)
                throw new Exception("El servidor no respetó el fragmento; no se leerá el cuerpo.");
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await using var body = await response.Content.ReadAsStreamAsync(ct.Token);
            await body.ReadExactlyAsync(new byte[1048576], ct.Token);
        }));
        Console.WriteLine($"PROBE {connections} conexiones: {connections * 1048576 / clock.Elapsed.TotalSeconds / 1e6:0.00} MB/s · muestra acotada de {connections} MiB, no benchmark del archivo completo");
    }
    return 0;
}
if (args.Contains("--download-throughput-probe"))
{
    const long total = 64L * 1024 * 1024;
    foreach (var connections in new[] { 1, 16 })
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, connections).Select(async index => {
            var size = total / connections; var start = index * size;
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseProfile.NinferModel.Url);
            request.Headers.Range = new(start, start + size - 1);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != start || response.Content.Headers.ContentRange?.To != start + size - 1)
                throw new Exception("El servidor no respetó el fragmento; no se leerá el cuerpo.");
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await using var body = await response.Content.ReadAsStreamAsync(ct.Token);
            var buffer = new byte[1048576]; long read = 0;
            while (read < size) { var count = await body.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,size-read)),ct.Token); if(count == 0) throw new IOException("Fragmento incompleto."); read += count; }
        }));
        Console.WriteLine($"THROUGHPUT {connections} conexiones: {total / clock.Elapsed.TotalSeconds / 1e6:0.00} MB/s · mismos 64 MiB, {clock.Elapsed.TotalSeconds:0.00}s, sin guardar pesos ni iniciar GPU");
    }
    return 0;
}
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

await DownloadTests.RunAsync(root, Assert, Test);
await StorageCleanupTests.RunAsync(root, Assert, Test);
await Rtx3090Tests.RunAsync(root, Assert, Test);
await GitHubTests.RunAsync(root, Assert, Test);

await Test("Hardware: admitir solo las tres placas de escritorio con su memoria", () =>
{
    var names = new[] { "NVIDIA GeForce RTX 5080", "NVIDIA GeForce RTX 5070 Ti", "NVIDIA GeForce RTX 5080 Laptop GPU", "NVIDIA GeForce RTX 5070 Ti Laptop GPU", "NVIDIA GeForce RTX 5070", "NVIDIA GeForce RTX 5090", "NVIDIA GeForce RTX 3090", "NVIDIA GeForce RTX 3090 Ti" };
    var rows = names.Select((n, i) => $"{i}, {n}, {(i >= 6 ? 24576 : 16303)}, 14000, 617.42");
    var gpus = HardwareDetector.ParseGpus(string.Join('\n', rows));
    Assert(gpus.Count == 8 && gpus.Count(g => g.IsSupported) == 3, "Se admitió hardware fuera del alcance.");
    Assert(new HardwareReport(gpus, 31.2).Problem is null, "RAM reservada de un equipo de 32 GB no debe bloquear.");
    Assert(new HardwareReport(gpus, 15.8).Problem is not null, "No se detectó RAM insuficiente.");
    Assert(new HardwareReport([new(0, names[0], 8000, 7000, "617.42")], 32).Problem is not null, "No se detectó VRAM insuficiente.");
    Assert(new HardwareReport([new(2, names[6], 16384, 15000, "test")], 32).Problem is not null, "Se aceptó una 3090 sin 24 GB.");
    Assert(new HardwareReport([gpus[6]], 32).SelectedGpu?.Index == 6, "No se seleccionó la 3090.");
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
await Test("Datos: instalación nueva usa Forja 50 sin tocar carpetas anteriores", () =>
{
    var local = Path.Combine(root, "brand-new"); Directory.CreateDirectory(local);
    var selected = AppPaths.DefaultDataRoot(local);
    Assert(selected == Path.Combine(local, "Forja 50"), "Nombre de datos anterior en instalación nueva.");
    var paths = new AppPaths(selected);
    Assert(paths.ModelDirectoryPath(new()) == Path.Combine(selected, "models"), "Modelo fuera de la nueva carpeta.");
    return Task.CompletedTask;
});
await Test("Datos: actualización conserva descargas y ajustes anteriores sin duplicarlos", () =>
{
    var local = Path.Combine(root, "brand-upgrade");
    var legacy = new AppPaths(Path.Combine(local, "MiIALocal"));
    legacy.SaveSettings(new() { ModelDirectory = "models" });
    Directory.CreateDirectory(legacy.ModelDirectoryPath(new()));
    File.WriteAllText(Path.Combine(legacy.ModelDirectoryPath(new()), "descarga.part"), "parcial conservado");
    Directory.CreateDirectory(Path.Combine(local, "Forja 50"));
    var selected = new AppPaths(AppPaths.DefaultDataRoot(local));
    Assert(selected.Root == legacy.Root && File.Exists(selected.SettingsFile), "La actualización perdió los ajustes anteriores.");
    Assert(File.ReadAllText(Path.Combine(selected.ModelDirectoryPath(new()), "descarga.part")) == "parcial conservado", "Se perdió la descarga parcial.");
    return Task.CompletedTask;
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
    var paths = new AppPaths(Path.Combine(root, "storage")); var settings = new AppSettings { EngineMode = EngineMode.KvMem, ModelDirectory = "Z:\\models" };
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
await Test("Actualizador: versiones, canales, paquetes y origen", () =>
{
    string Release(string version, bool preview = true, bool draft = false, string? url = null, string? digest = null) => System.Text.Json.JsonSerializer.Serialize(new {
        tag_name = "v" + version, prerelease = preview, draft,
        assets = new[] { new { name = $"Mi-IA-Local-{version}-Setup.exe", size = 1024, digest = digest ?? "sha256:" + new string('a', 64), browser_download_url = url ?? $"{AppUpdates.ReleasesUrl}/download/v{version}/Mi-IA-Local-{version}-Setup.exe" },
                         new { name = $"Mi-IA-Local-{version}-win-x64.zip", size = 2048, digest = "sha256:" + new string('b', 64), browser_download_url = $"{AppUpdates.ReleasesUrl}/download/v{version}/Mi-IA-Local-{version}-win-x64.zip" } }
    });
    var releases = "[" + string.Join(',', Release("0.1.9-dev"), Release("0.1.10-dev"), Release("0.1.11-dev", draft: true)) + "]";
    Assert(AppUpdates.Select(releases, "0.1.5-dev", false)?.Version == "0.1.10-dev", "Orden numérico incorrecto o se publicó un borrador.");
    Assert(AppUpdates.Select(releases, "0.1.5-dev", true)?.Asset.FileName.EndsWith("win-x64.zip") == true, "Portable eligió Setup.");
    Assert(AppUpdates.Select(releases, "0.1.5", false) is null, "Estable siguió versiones preliminares.");
    Assert(AppUpdates.Select(releases, "0.1.10-dev", false) is null, "Se ofreció la misma versión o un downgrade.");
    Assert(AppUpdates.Select("[" + Release("0.1.10", false) + "]", "0.1.10-dev", false)?.Version == "0.1.10", "No se promovió dev a estable.");
    Assert(AppUpdates.Select("[" + Release("0.1.10-dev", url: "https://example.invalid/evil.exe") + "]", "0.1.5-dev", false) is null, "Se aceptó un ejecutable externo.");
    Assert(AppUpdates.Select("[" + Release("0.1.10-dev", digest: "sha256:bad") + "]", "0.1.5-dev", false) is null, "Se aceptó un paquete sin hash válido.");
    var renamed = releases.Replace("Mi-IA-Local-", "Forja-50-").Replace("-win-x64.zip", "-Portable-win-x64.zip");
    Assert(AppUpdates.Select(renamed, "0.1.5-dev", false)?.Asset.FileName == "Forja-50-0.1.10-dev-Setup.exe", "No se reconoció el Setup renombrado.");
    Assert(AppUpdates.Select(renamed, "0.1.5-dev", true)?.Asset.FileName == "Forja-50-0.1.10-dev-Portable-win-x64.zip", "No se reconoció el portable renombrado.");
    Assert(AppUpdates.Select(releases.Replace(AppUpdates.ReleasesUrl, AppUpdates.LegacyReleasesUrl), "0.1.5-dev", false) is not null, "Se perdió la compatibilidad con paquetes anteriores.");
    return Task.CompletedTask;
});
await Test("Actualizador: consulta GitHub sin credenciales y maneja acceso privado", async () =>
{
    using var client = new HttpClient(new Handler(request => {
        Assert(request.Headers.Authorization is null, "Se enviaron credenciales.");
        Assert(request.Headers.UserAgent.Count > 0 && request.RequestUri?.AbsoluteUri == AppUpdates.ApiUrl, "Consulta incorrecta.");
        return new(HttpStatusCode.NotFound);
    }));
    await Throws<InvalidOperationException>(() => AppUpdates.CheckAsync(client, "0.1.5-dev", false, default));
});
if (OperatingSystem.IsWindows())
{
    await Test("Proceso: salida rápida 0 y Unicode sin falso error", async () =>
    {
        var executable = Environment.ProcessPath!;
        string[] arguments = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [typeof(Program).Assembly.Location, "--early-exit-fixture"] : ["--early-exit-fixture"];
        await using (var process = OwnedProcess.Start(executable, arguments, root, Path.Combine(root, "early")))
        { var deadline = DateTime.UtcNow.AddSeconds(15); while (!process.HasExited && DateTime.UtcNow < deadline) await Task.Delay(100); Assert(process.HasExited, "El proceso de prueba no terminó."); }
        Assert((await File.ReadAllTextAsync(Path.Combine(root, "early.out.log"))).Contains("Revisión correcta"), "Se dañaron los caracteres.");
    });
}
await Test("NInfer-5080: artefacto oficial fijado y componentes por modo", () =>
{
    var asset = ReleaseProfile.NinferModel;
    Assert(asset.Url.Contains("huggingface.co/ninfer-5080/Qwen3.8-27B-RTX5080/resolve/e23a3301eed611ac3196e3389c3d572bc5ddeb9a/") && asset.Sha256 == "c4a7e9ab593a7f42d58208fa0065d67a82d61921107686cc9f6ed1ec6b050e21" && asset.Bytes == 16461267456, "El artefacto no está fijado a la revisión validada.");
    Assert(ReleaseProfile.ModelFor(EngineMode.NInfer5080) == asset && ReleaseProfile.ModelFor(EngineMode.KvMem) == ReleaseProfile.Model, "El modo no selecciona su modelo.");
    Assert(ReleaseProfile.ComponentsFor(EngineMode.NInfer5080).Length == 4 && ReleaseProfile.ComponentsFor(EngineMode.NInfer5080).Contains(ReleaseProfile.NativeEngine) && !ReleaseProfile.ComponentsFor(EngineMode.NInfer5080).Contains(ReleaseProfile.Engine) && ReleaseProfile.ComponentsFor(EngineMode.KvMem).Length == 4, "El modo nativo debe descargar su propio motor.");
    return Task.CompletedTask;
});
await Test("NInfer-5080: argumentos del perfil nativo Windows", async () =>
{
    var arguments = RuntimeSupervisor.Ninfer5080Arguments("qwen3_8_27b.ninfer", 18080, "clave", 131072, true);
    string Value(string flag) => arguments[Array.IndexOf(arguments, flag) + 1];
    Assert(arguments[0] == "qwen3_8_27b.ninfer" && Value("--host") == "127.0.0.1" && Value("--port") == "18080", "Falta el destino local del motor.");
    Assert(Value("--api-key") == "clave" && Value("--model-id") == "qwen-local", "El motor no usa la clave efímera o el identificador esperado.");
    Assert(Value("--max-context") == "131072" && Value("--kv-capacity") == "131072" && Value("--kv-dtype") == "q4", "El contexto o el KV no reproducen el perfil.");
    Assert(Value("--spec") == "mtp" && Value("--draft-tokens") == "3" && Value("--prefill-chunk") == "896", "Falta MTP-3 o el prefill del perfil.");
    Assert(arguments.Contains("--embedding-host") && arguments.Contains("--vision") && Value("--vision-max-tokens") == "2048", "Faltan los embebidos en host o la visión.");
    Assert(Value("--prefix-checkpoint-policy") == "rolling-tool" && Value("--max-pending-requests") == "16" && Value("--max-concurrency") == "1", "Faltan el reuso de prefijos o los límites de cola.");
    Assert(!RuntimeSupervisor.Ninfer5080Arguments("m.ninfer", 1, "k", 65536, false).Contains("--vision"), "La visión se activó cuando se pidió desactivarla.");
    await Throws<InvalidDataException>(() => { RuntimeSupervisor.Ninfer5080Arguments("m.ninfer", 1, "k", 32768); return Task.CompletedTask; });
});
await Test("NInfer-5080: modelo y motor siguen la carpeta de datos", () =>
{
    var paths = new AppPaths(Path.Combine(root, "ninfer-paths"));
    var settings = new AppSettings { EngineMode = EngineMode.NInfer5080 };
    Assert(Path.GetFileName(paths.NinferModelPath(settings)) == ReleaseProfile.NinferModel.FileName && paths.ActiveModelPath(settings) == paths.NinferModelPath(settings), "El modo NInfer-5080 no usa su propio artefacto.");
    settings.ImportedNinferModel = Path.Combine(root, "otro", "qwen3_8_27b.ninfer");
    Assert(paths.ActiveModelPath(settings) == Path.GetFullPath(settings.ImportedNinferModel, paths.Root), "No se respetó el artefacto importado.");
    Assert(paths.ModelPath(new AppSettings()) == Path.Combine(paths.ModelDirectoryPath(new AppSettings()), ReleaseProfile.Model.FileName) && !paths.HasNinferEngine(new AppSettings()), "El modo Forja dejó de usar su modelo o declaró un motor importado.");
    return Task.CompletedTask;
});
await Test("NInfer-5080: motor automático e importación opcional", async () =>
{
    var paths = new AppPaths(Path.Combine(root, "ninfer-prep"));
    var settings = new AppSettings { EngineMode = EngineMode.NInfer5080 };
    using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)));
    var provisioner = new Provisioner(paths, new VerifiedDownloader(client));
    Assert(!provisioner.IsPrepared(settings), "Se declaró preparado sin motor ni modelo.");
    foreach (var file in new[] { paths.AgentExe, paths.GitExe, paths.NodeExe, paths.NinferModelPath(settings) }) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, "x"); }
    Assert(!provisioner.IsPrepared(settings), "Se declaró preparado sin el motor ninfer-serve.");
    var managedEngine = paths.NinferEngineExe(settings);
    Directory.CreateDirectory(Path.GetDirectoryName(managedEngine)!); await File.WriteAllTextAsync(managedEngine, "x");
    Assert(provisioner.IsPrepared(settings) && managedEngine == Path.Combine(paths.Runtime, "ninfer5080", "ninfer-serve.exe"), "No se reconoció el motor administrado.");
    var engine = Path.Combine(paths.Root, "ninfer-serve.exe"); await File.WriteAllTextAsync(engine, "x"); settings.NinferEnginePath = engine;
    Assert(provisioner.IsPrepared(settings), "No se reconoció el motor importado con el artefacto presente.");
    await Throws<InvalidOperationException>(() => provisioner.PrepareAsync(new AppSettings { EngineMode = EngineMode.NInfer5080, LicenseAccepted = true, NinferEnginePath = Path.Combine(paths.Root, "missing.exe") }, null, default));
});
await Test("NInfer-5080: agente 128K y aviso en la 5070 Ti", () =>
{
    using var json = System.Text.Json.JsonDocument.Parse(AgentConfiguration.Build(12345, "key", "instructions.md", 131072, null, "Qwen3.8-27B · NInfer-5080"));
    var model = json.RootElement.GetProperty("provider").GetProperty("local").GetProperty("models").GetProperty("qwen-local");
    Assert(model.GetProperty("limit").GetProperty("context").GetInt32() == 131072 && model.GetProperty("name").GetString()!.Contains("NInfer-5080"), "El agente no refleja el contexto o el nombre del modo.");
    Assert(Ninfer5080Profile.WarningFor("NVIDIA GeForce RTX 5070 Ti", 131072) is not null && Ninfer5080Profile.WarningFor("NVIDIA GeForce RTX 5080", 131072) is not null && Ninfer5080Profile.WarningFor("NVIDIA GeForce RTX 5070 Ti", 65536) is null, "El aviso del 128K en la 5070 Ti es incorrecto.");
    Assert(Ninfer5080Profile.Contexts is [65536, 131072], "Los contextos admitidos del modo cambiaron.");
    return Task.CompletedTask;
});
await Test("NInfer-5080: conservar 128K y normalizar contexto no admitido", () =>
{
    var paths = new AppPaths(Path.Combine(root, "ninfer-settings"));
    paths.SaveSettings(new AppSettings { EngineMode = EngineMode.NInfer5080, ContextTokens = 131072 });
    var loaded = paths.LoadSettings();
    Assert(loaded.EngineMode == EngineMode.NInfer5080 && loaded.ContextTokens == 131072 && paths.SettingsRecoveryMessage is null, "Se descartó un 128K válido del modo NInfer-5080.");
    File.WriteAllText(paths.SettingsFile, "{\"EngineMode\":1,\"ContextTokens\":90000}");
    var normalized = paths.LoadSettings();
    Assert(normalized.EngineMode == EngineMode.NInfer5080 && normalized.ContextTokens == 65536, "No se normalizó un contexto no admitido del modo NInfer-5080.");
    return Task.CompletedTask;
});
await Test("Nativo: detectar GPU, diferenciar perfiles y conservar ajustes manuales", async () =>
{
    var gpu5080 = new GpuInfo(2, "NVIDIA GeForce RTX 5080", 16303, 15000, "617.14");
    var gpu5070 = new GpuInfo(1, "NVIDIA GeForce RTX 5070 Ti", 16303, 15000, "617.14");
    var a = Ninfer5080Profile.For(gpu5080); var b = Ninfer5080Profile.For(gpu5070);
    Assert(a.Id != b.Id && a.PrefillChunk == 896 && b.PrefillChunk == 512, "No se diferenciaron las placas.");
    var tuning = new NativeEngineTuning { PrefillChunk = 256, ThinkingTokens = 0, CudaGraph = false, DraftTokens = 2 };
    var args = RuntimeSupervisor.Ninfer5080Arguments("m.ninfer", 1234, "key", 65536, false, gpu5080.Index, a.PrefillChunk, tuning);
    string Value(string name) => args[Array.IndexOf(args, name) + 1];
    Assert(Value("--device") == "2" && Value("--prefill-chunk") == "256" && Value("--draft-tokens") == "2", "Se ignoró la GPU o la configuración manual.");
    Assert(args.Contains("--no-thinking") && args.Contains("--no-cuda-graph") && !args.Contains("--vision"), "No se respetaron los modos de memoria y razonamiento.");
    var settings = new AppSettings { ContextTokens = 131072, NativeTuning = tuning }; GpuProfiles.Apply(settings, gpu5070);
    Assert(settings.ContextTokens == 131072 && settings.NativeTuning.PrefillChunk == 256, "La detección borró la personalización nativa.");
    await Throws<InvalidDataException>(() => { new NativeEngineTuning { PrefillChunk = 511 }.Validate(65536); return Task.CompletedTask; });
    await Throws<InvalidDataException>(() => { Ninfer5080Profile.For(gpu5080 with { Name = "NVIDIA GeForce RTX 5080 Laptop GPU" }); return Task.CompletedTask; });
});
await Test("Espacio nativo: descontar el parcial al reanudar", async () =>
{
    var paths = new AppPaths(Path.Combine(root, "native-storage")); var settings = new AppSettings();
    var drive = Path.GetPathRoot(paths.Root)!;
    var before = PreparationStorage.Requirements(paths, settings)[drive];
    var model = paths.ActiveModelPath(settings); Directory.CreateDirectory(Path.GetDirectoryName(model)!);
    await File.WriteAllBytesAsync(model + ".part", new byte[1048576]);
    Assert(PreparationStorage.Requirements(paths, settings)[drive] == before - 1048576, "Se exige descargar de nuevo el espacio ya guardado.");
});
Console.WriteLine($"RESULTADO: {failures} fallos. Registros: {root}");
return failures == 0 ? 0 : 1;

sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
}
