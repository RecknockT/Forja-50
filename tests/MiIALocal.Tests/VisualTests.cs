using System.Net;
using System.Text;
using System.Text.Json;
using MiIALocal.Core;

static class VisualTests
{
    public static async Task RunAsync(string root, Action<bool, string> assert, Func<string, Func<Task>, Task> test)
    {
        await test("Visual: extracción 7z real y rechazo previo de rutas que escapan", () =>
        {
            var destination = Path.Combine(root, "visual-7z");
            VisualProvisioner.ExtractSevenZip(Path.Combine(AppContext.BaseDirectory, "Fixtures", "empty-stream.7z"), destination, default);
            assert(File.Exists(Path.Combine(destination, "000")) && Directory.Exists(Path.Combine(destination, "NewFolder")), "No se extrajo el archivo 7z.");
            var archive = Path.Combine(root, "visual-malicious.7z");
            using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create)) { zip.CreateEntry("valid.txt"); zip.CreateEntry("../outside.txt"); }
            var rejected = Path.Combine(root, "visual-rejected");
            try { VisualProvisioner.ExtractSevenZip(archive, rejected, default); throw new Exception("Se aceptó un paquete que escapa."); } catch (InvalidDataException) { }
            assert(!File.Exists(Path.Combine(rejected, "valid.txt")), "Se escribieron entradas antes de validar todo el paquete.");
            return Task.CompletedTask;
        });
        await test("Visual: modos y ajustes independientes conservan configuración anterior", () =>
        {
            var paths = new AppPaths(Path.Combine(root, "visual-settings"));
            File.WriteAllText(paths.SettingsFile, "{\"ContextTokens\":65536,\"EngineMode\":1,\"LastProject\":\"C:\\\\proyecto\"}");
            var settings = paths.LoadSettings();
            assert(settings.Mode == AppMode.Programming && settings.Visual.Width == 1024, "La migración alteró el modo inicial.");
            settings.Mode = AppMode.Visual; settings.Visual.IncludeAnimation = true; paths.SaveSettings(settings);
            var loaded = paths.LoadSettings();
            assert(loaded.Mode == AppMode.Visual && loaded.ContextTokens == 65536 && loaded.LastProject == settings.LastProject && loaded.Visual.IncludeAnimation, "Se mezclaron ajustes de los modos.");
            return Task.CompletedTask;
        });
        await test("Visual: catálogo completo fijado, animación opcional y sin Qwen/OpenCode", () =>
        {
            assert(VisualCatalog.Images.Length == 3 && VisualCatalog.For(true).Length == 6, "Faltan codificadores o VAE.");
            foreach (var model in VisualCatalog.All)
            {
                assert(model.Asset.Bytes > 0 && model.Asset.Sha256.Length == 64 && !model.Asset.Url.Contains("/main/"), "Modelo sin integridad o revisión fija.");
                assert(!model.Asset.FileName.EndsWith(".ninfer"), "Se requiere el Qwen de programación.");
            }
            assert(VisualCatalog.Runtime.Url.Contains("v0.42.1") && VisualCatalog.DownloadBytes(false) < VisualCatalog.DownloadBytes(true), "Se descarga todo obligatoriamente.");
            var paths = new AppPaths(Path.Combine(root, "visual-config"));
            new VisualProvisioner(paths, null!).WriteConfiguration();
            assert(!File.Exists(paths.AgentExe) && !File.Exists(paths.ActiveModelPath(new())), "La preparación visual exige programación.");
            var guard = File.ReadAllText(Path.Combine(paths.ComfyRoot, "ComfyUI", "custom_nodes", "forja_guard", "__init__.py"));
            assert(guard.Contains("hmac.compare_digest") && guard.Contains("Authorization") && guard.Contains("Origin"), "Motor sin protección local.");
            return Task.CompletedTask;
        });
        await test("Visual: editar transmite la referencia y crear imagen nueva la omite", () =>
        {
            var edit = JsonSerializer.Serialize(VisualWorkflows.Build(new(VisualTask.Edit, "Solo el brazo izquierdo rojo", 1024, 768, 42), new string('a', 32), "referencia.png"));
            var create = JsonSerializer.Serialize(VisualWorkflows.Build(new(VisualTask.Image, "Robot", 1024, 1024, 42), new string('b', 32)));
            assert(edit.Contains("ReferenceLatent") && edit.Contains("referencia.png") && edit.Contains("Solo el brazo"), "No se pasó la edición condicionada.");
            assert(!create.Contains("ReferenceLatent") && !create.Contains("LoadImage"), "Imagen nueva hereda la anterior.");
            return Task.CompletedTask;
        });
        await test("Visual: animación produce MP4 y GIF con una única difusión", () =>
        {
            var graph = VisualWorkflows.Build(new(VisualTask.Animation, "Saluda", 768, 448, 42), new string('c', 32), "ref.png");
            var json = JsonSerializer.Serialize(graph);
            assert(json.Contains("Wan22ImageToVideoLatent") && json.Contains("ForjaSaveGif") && json.Contains("SaveVideo") && json.Contains("start_image"), "Falta un formato o referencia.");
            assert(graph.Values.Count(n => JsonSerializer.Serialize(n).Contains("\"class_type\":\"KSampler\"")) == 1, "GIF requiere una generación extra.");
            return Task.CompletedTask;
        });
        await test("Visual: rechazar parámetros, rutas e identificadores ajenos", () =>
        {
            void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Se aceptó una solicitud inválida."); }
            Reject(() => VisualWorkflows.Build(new(VisualTask.Edit, "x", 1024, 1024, 1), new string('a', 32)));
            Reject(() => VisualWorkflows.Build(new(VisualTask.Edit, "x", 1024, 1024, 1), new string('a', 32), "../secret.png"));
            Reject(() => VisualWorkflows.Build(new(VisualTask.Animation, "x", 1024, 1024, 1), new string('a', 32)));
            Reject(() => VisualWorkflows.Build(new(VisualTask.Image, "x", 1024, 1024, 1), "../../proyecto"));
            Reject(() => new VisualRequest(VisualTask.Animation, "x", 768, 448, 1, Frames: 120).Validate());
            try { _ = new ComfyClient(new(), new("http://example.com/"), "secret"); throw new Exception("Se admitió un motor externo."); } catch (ArgumentException) { }
            return Task.CompletedTask;
        });
        await test("Visual: perfiles RTX 30/50, solo proceso local y sin aceleraciones obligatorias FP8/FP4", () =>
        {
            var paths = new AppPaths(Path.Combine(root, "visual-profiles"));
            foreach (var (name, memory) in new[] { ("RTX 3090", 24576), ("RTX 5080", 16384), ("RTX 5070 Ti", 16384) })
            {
                var arguments = VisualRuntime.Arguments(paths, new(2, "NVIDIA GeForce " + name, memory, memory, "fixture"), 12345);
                assert(arguments.Contains("127.0.0.1") && arguments.Contains("--offline") && arguments.Contains("--lowvram") && !arguments.Contains("--fast"), "Arranque incompatible o externo.");
                assert(arguments[Array.IndexOf(arguments, "--cuda-device") + 1] == "2" && arguments.Contains("forja_guard"), "GPU o protección incorrecta.");
            }
            return Task.CompletedTask;
        });
        await test("Visual: recuperación conserva resultados, referencias y errores de trabajos", () =>
        {
            var paths = new AppPaths(Path.Combine(root, "visual-jobs")); var store = new VisualJobStore(paths);
            var job = new VisualJob { Status = "Generando", Request = new(VisualTask.Image, "Robot á", 1024, 1024, 42) }; store.Save(job);
            File.WriteAllText(Path.Combine(paths.VisualJobs, "invalid.json"), "{bad");
            store.RecoverInterrupted(); assert(store.Read().Single().Status == "Interrumpido", "Se relanzó o perdió un trabajo interrumpido.");
            return Task.CompletedTask;
        });
        await test("Visual: galería sigue a los datos al mover la edición portable", () =>
        {
            var before = new AppPaths(Path.Combine(root, "visual-portable-before")); Directory.CreateDirectory(before.VisualOutputs);
            var file = Path.Combine(before.VisualOutputs, "resultado.png"); File.WriteAllText(file, "conservar");
            new VisualJobStore(before).Save(new VisualJob { Status = "Completado", Files = [file] });
            var target = Path.Combine(root, "visual-portable-after"); Directory.Move(before.Root, target);
            var after = new AppPaths(target); var loaded = new VisualJobStore(after).Read().Single();
            assert(loaded.Files.Single() == Path.Combine(after.VisualOutputs, "resultado.png") && File.Exists(loaded.Files.Single()), "La galería guardó rutas absolutas de otra ubicación.");
            return Task.CompletedTask;
        });
        await test("Visual: limpiar modelos e historial conserva imágenes y referencias", () =>
        {
            var paths = new AppPaths(Path.Combine(root, "visual-cleanup"));
            Directory.CreateDirectory(paths.VisualOutputs); Directory.CreateDirectory(paths.VisualInputs); Directory.CreateDirectory(paths.VisualJobs);
            File.WriteAllText(Path.Combine(paths.VisualOutputs, "obra.png"), "conservar"); File.WriteAllText(Path.Combine(paths.VisualInputs, "ref.png"), "conservar");
            foreach (var model in VisualCatalog.All) { var file = VisualCatalog.PathFor(paths, model); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "model"); File.WriteAllText(file + ".part", "partial"); }
            new StorageCleanup(paths, new()).Delete(Enum.GetValues<StorageCategory>());
            assert(File.Exists(Path.Combine(paths.VisualOutputs, "obra.png")) && File.Exists(Path.Combine(paths.VisualInputs, "ref.png")), "Se borraron creaciones del usuario.");
            assert(VisualCatalog.All.All(m => !File.Exists(VisualCatalog.PathFor(paths, m)) && !File.Exists(VisualCatalog.PathFor(paths, m) + ".part")), "Quedaron pesos o parciales conocidos.");
            return Task.CompletedTask;
        });
        await test("Visual: contrato HTTP, autenticación y resultado de trabajo sin GPU", async () =>
        {
            var id = new string('d', 32); var promptId = Guid.NewGuid().ToString(); var output = Path.Combine(root, "visual-http"); Directory.CreateDirectory(output);
            var file = Path.Combine(output, id + "_00001_.png"); File.WriteAllText(file, "fixture"); var submitted = false;
            var graph = VisualWorkflows.Build(new(VisualTask.Image, "Robot", 1024, 1024, 42), id);
            var nodes = graph.Values.Select(n => JsonSerializer.SerializeToElement(n).GetProperty("class_type").GetString()!).Distinct().ToDictionary(x => x, _ => new { });
            using var http = new HttpClient(new AsyncHandler(async (request, _) =>
            {
                assert(request.Headers.Authorization?.Parameter == "private-fixture", "Falta autenticación por solicitud.");
                if (request.RequestUri!.AbsolutePath == "/object_info") return Json(nodes);
                if (request.RequestUri.AbsolutePath == "/prompt")
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                    assert(body.RootElement.GetProperty("prompt").GetProperty("4").GetProperty("inputs").GetProperty("text").GetString() == "Robot", "Se perdió el pedido.");
                    submitted = true; return Json(new { prompt_id = promptId });
                }
                return Json(new Dictionary<string, object> { [promptId] = new { status = new { completed = true, status_str = "success" }, outputs = new { final = new { images = new[] { new { filename = Path.GetFileName(file), subfolder = "", type = "output" } } } } } });
            }));
            var files = await new ComfyClient(http, new("http://127.0.0.1:12345/"), "private-fixture").GenerateAsync(new(VisualTask.Image, "Robot", 1024, 1024, 42), id, null, output, null, default, false);
            assert(submitted && files.Single() == file, "Se perdió la salida.");
        });
        await test("Visual: no aceptar salidas fuera de la carpeta o de otro trabajo", () =>
        {
            var output = Path.Combine(root, "visual-escape"); Directory.CreateDirectory(output); var id = new string('e', 32);
            foreach (var name in new[] { "../secret.png", "otro_00001_.png" })
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { outputs = new { final = new { images = new[] { new { filename = name, type = "output" } } } } }));
                try { ComfyClient.ReadFiles(json.RootElement, output, id); throw new Exception("Salida inválida aceptada."); } catch (InvalidDataException) { }
            }
            return Task.CompletedTask;
        });
        await test("Visual: un motor incompleto rechaza el trabajo antes de generar", async () =>
        {
            var submitted = false;
            using var http = new HttpClient(new AsyncHandler((request, _) => { if (request.Method == HttpMethod.Post) submitted = true; return Task.FromResult(Json(new { })); }));
            try { await new ComfyClient(http, new("http://127.0.0.1:12345/"), "fixture").GenerateAsync(new(VisualTask.Image, "Robot", 1024, 1024, 1), new string('a', 32), null, root, null, default, false); throw new Exception("Se aceptó un motor incompleto."); }
            catch (InvalidDataException) { }
            assert(!submitted, "Se inició una generación sin los nodos requeridos.");
        });
        await test("Visual: cancelación interrumpe la espera de generación sin inventar resultados", async () =>
        {
            var request = new VisualRequest(VisualTask.Image, "Robot", 1024, 1024, 1); var id = new string('a', 32);
            var graph = VisualWorkflows.Build(request, id);
            var nodes = graph.Values.Select(n => JsonSerializer.SerializeToElement(n).GetProperty("class_type").GetString()!).Distinct().ToDictionary(x => x, _ => new { });
            using var http = new HttpClient(new AsyncHandler((message, _) => Task.FromResult(message.RequestUri!.AbsolutePath switch { "/object_info" => Json(nodes), "/prompt" => Json(new { prompt_id = Guid.NewGuid().ToString() }), _ => Json(new { }) })));
            using var timeout = new CancellationTokenSource(150);
            try { await new ComfyClient(http, new("http://127.0.0.1:12345/"), "fixture").GenerateAsync(request, id, null, root, null, timeout.Token, false); throw new Exception("Se ignoró la cancelación."); }
            catch (OperationCanceledException) { }
        });
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken); }
}
