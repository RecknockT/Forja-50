using System.Net;
using System.Text;
using System.Text.Json;
using MiIALocal.Core;

static class VisualTests
{
    public static async Task RunAsync(string root, Action<bool, string> assert, Func<string, Func<Task>, Task> test)
    {
        await test("HiDream: flujo nativo, referencias reales y asistente opcional por etapas", () =>
        {
            foreach (var kind in new[] { VisualTask.Image, VisualTask.Edit })
            foreach (var assistant in new[] { false, true })
            {
                var graph = JsonSerializer.SerializeToElement(VisualWorkflows.Build(new(kind, "El brazo rojo", 1024, 1024, 42, EnhancePrompt: assistant), new string('a', 32), "reference.png"));
                File.WriteAllText(Path.Combine(root, $"hidream-{kind}-{assistant}-graph.json"), graph.GetRawText());
                assert(graph.GetProperty("1").GetProperty("class_type").GetString() == "CheckpointLoaderSimple", "No se carga el checkpoint integrado.");
                assert(graph.GetProperty("8").GetProperty("inputs").GetProperty("steps").GetInt32() == 28, "Se cambió el calendario destilado.");
                assert(graph.GetProperty("23").GetProperty("inputs").GetProperty("images.image_1")[0].GetString() == "21", "La referencia no llega a HiDream.");
                assert(graph.TryGetProperty("31", out _) == assistant, "Asistente obligatorio o ignorado.");
                if (assistant) assert(graph.GetProperty("31").GetProperty("inputs").GetProperty("image")[0].GetString() == "21", "El asistente no ve la referencia.");
                assert(graph.GetProperty("4").GetProperty("inputs").GetProperty("text")[0].GetString() == "32", "No se registra el pedido utilizado.");
            }
            var plain = JsonSerializer.SerializeToElement(VisualWorkflows.Build(new(VisualTask.Image, "Robot", 2048, 2048, 1), new string('a', 32)));
            File.WriteAllText(Path.Combine(root, "hidream-Create-graph.json"), plain.GetRawText());
            assert(!plain.TryGetProperty("20", out _), "Se heredó una referencia.");
            assert(!new VisualSettings { LicenseAccepted = true }.HiDreamLicenseAccepted, "Se aceptan licencias durante la migración.");
            return Task.CompletedTask;
        });
        await test("HiDream: la descripción preparada se revisa y el asistente no se repite", () =>
        {
            var request = new VisualRequest(VisualTask.Image, "El brazo rojo", 1024, 1024, 42, EnhancePrompt: true);
            var reviewed = JsonSerializer.SerializeToElement(VisualWorkflows.Build(request, new string('a', 32), "reference.png", "Descripción corregida por la persona"));
            assert(!reviewed.TryGetProperty("31", out _) && !reviewed.TryGetProperty("30", out _) && !reviewed.TryGetProperty("32", out _), "El asistente se ejecutó de nuevo tras la revisión.");
            assert(reviewed.GetProperty("4").GetProperty("inputs").GetProperty("text").GetString() == "Descripción corregida por la persona", "No se envía el texto revisado.");
            assert(reviewed.GetProperty("23").GetProperty("inputs").GetProperty("images.image_1")[0].GetString() == "21", "La revisión perdió la referencia.");
            var prepare = JsonSerializer.SerializeToElement(VisualWorkflows.BuildPrepare(new(VisualTask.Edit, "Cambiá el brazo", 1024, 1024, 42, EnhancePrompt: true), "reference.png"));
            File.WriteAllText(Path.Combine(root, "hidream-prepare-graph.json"), prepare.GetRawText());
            assert(prepare.GetProperty("31").GetProperty("class_type").GetString() == "TextGenerate", "La preparación no usa el asistente.");
            assert(prepare.GetProperty("31").GetProperty("inputs").GetProperty("image")[0].GetString() == "21", "La preparación no analiza la referencia.");
            assert(!prepare.TryGetProperty("1", out _) && !prepare.TryGetProperty("11", out _) && !prepare.TryGetProperty("13", out _), "La preparación carga la difusión o escribe resultados.");
            assert(VisualWorkflows.BuildPrepare(new(VisualTask.Image, "Robot", 1024, 1024, 1, EnhancePrompt: true), null).Values.Count == 3, "La preparación sin referencia ejecuta nodos de más.");
            try { VisualWorkflows.BuildPrepare(new(VisualTask.Image, "Robot", 1024, 1024, 1), null); throw new Exception("Se preparó una descripción con el asistente desactivado."); } catch (InvalidDataException) { }
            try { VisualWorkflows.BuildPrepare(new(VisualTask.Animation, "Robot", 768, 448, 1, EnhancePrompt: true), null); throw new Exception("Se preparó una descripción para animación."); } catch (InvalidDataException) { }
            return Task.CompletedTask;
        });
        await test("HiDream: perfil de descarga por GPU e importación inválida conserva el original", async () =>
        {
            var card = new GpuInfo(0, "NVIDIA GeForce RTX 3090", 24576, 24576, "fixture");
            assert(VisualCatalog.For(false, card, false).Single() == VisualCatalog.HiDreamBf16, "3090 no usa el perfil BF16.");
            assert(VisualCatalog.For(false, null, false).Single() == VisualCatalog.HiDream, "Se descargan variantes innecesarias.");
            var paths = new AppPaths(Path.Combine(root, "hidream-import")); var wrong = Path.Combine(paths.Root, VisualCatalog.HiDream.Asset.FileName); File.WriteAllText(wrong, "invalid");
            try { await new VisualProvisioner(paths, null!).ImportModelsAsync([wrong], null, default); throw new Exception("Se importó un modelo incorrecto."); } catch (InvalidDataException) { }
            assert(!File.Exists(VisualCatalog.PathFor(paths, VisualCatalog.HiDream)) && File.Exists(wrong), "Una importación fallida cambió los archivos.");
        });
        await test("Visual: referencias en línea detectan Frieren y distinguen personaje de serie", () =>
        {
            foreach (var idea in new[] { "anime de los 90", "generar una imagen de anime de los 90", "estética anime noventera", "un paisaje con estilo retro", "1990s anime style", "un robot caminando" })
                assert(VisualReferenceSearch.AutomaticSubject(idea) is null, "Un concepto general se convierte en una obra: " + idea);
            assert(VisualReferenceSearch.AutomaticSubject("Frieren volando con estilo anime de los 90") == "Frieren", "Se perdió el personaje al pedir un estilo.");
            foreach (var prompt in new[] { "frieren la elfa del anime, volando con su baston magico", "frieren del anime, volando con su baston magico", "Frieren the elf flying with her staff" })
                assert(VisualReferenceSearch.AutomaticSubject(prompt)!.Equals("Frieren", StringComparison.OrdinalIgnoreCase), "Se confunde el nombre con su descripción: " + prompt);
            assert(VisualReferenceSearch.SuggestSubject("Sailor Moon la guerrera, volando") == "Sailor Moon", "Se recorta un nombre compuesto.");
            assert(VisualReferenceSearch.SuggestSubject("generar una imagen de frieren volando") == "frieren", "La búsqueda incluye la acción.");
            assert(VisualReferenceSearch.SuggestSubject("Create an image of Frieren flying") == "Frieren", "La sugerencia en inglés incluye el pedido.");
            assert(VisualReferenceSearch.SuggestSubject("") == "", "El pedido vacío falla.");
            var series = new VisualReference("Frieren", "https://en.wikipedia.org/wiki/Frieren", "https://upload.wikimedia.org/series.jpg", "Frieren");
            var character = series with { Title = "Frieren (character)" };
            assert(VisualReferenceSearch.AutomaticMatch([series, character], "frieren") == character, "Se usa la portada de la serie en lugar del personaje.");
            assert(!VisualReferenceSearch.AllowedImage("http://127.0.0.1/file") && !VisualReferenceSearch.AllowedImage("https://upload.wikimedia.org.evil.test/a.png") && !VisualReferenceSearch.AllowedImage("https://user@upload.wikimedia.org/a.png"), "Referencia externa insegura.");
            return Task.CompletedTask;
        });
        await test("Visual: animación conserva marco, orientación, estilo y pedido de movimiento", () =>
        {
            foreach (var format in new[] { 0, 1, 2 })
            {
                var (width, height) = VisualAnimation.Dimensions(format);
                var request = new VisualRequest(VisualTask.Animation, "que transite por el camino", width, height, 42);
                request.Validate();
                var graph = JsonSerializer.SerializeToElement(VisualWorkflows.Build(request, new string('a', 32), "car.png"));
                var prompt = graph.GetProperty("4").GetProperty("inputs").GetProperty("text").GetString()!;
                assert(prompt.Contains(request.Prompt) && prompt.Contains("visual style"), "Se pierde el movimiento original o el estilo.");
                assert(graph.GetProperty("5").GetProperty("class_type").GetString() == "CLIPTextEncode", "Wan recibe un negativo vacío.");
                assert(graph.GetProperty("21").GetProperty("class_type").GetString() == "ForjaFitReference", "La referencia se recorta en el nodo Wan.");
                assert(graph.GetProperty("6").GetProperty("inputs").GetProperty("start_image")[0].GetString() == "21", "No se usa el marco ajustado.");
                assert(width * height <= 768 * 448, "El nuevo formato aumenta el presupuesto de píxeles.");
            }
            var textOnly = JsonSerializer.SerializeToElement(VisualWorkflows.Build(new(VisualTask.Animation, "car moving", 768, 448, 1), new string('a', 32)));
            assert(!textOnly.GetProperty("6").GetProperty("inputs").TryGetProperty("start_image", out _) && VisualAnimation.Prompt("car moving", false) == "car moving", "Video sin referencia hereda una imagen.");
            return Task.CompletedTask;
        });
        await test("Visual: búsqueda real del protocolo, imagen acotada y procedencia persistente", async () =>
        {
            var sawQuery = false;
            using var search = new VisualReferenceSearch(new AsyncHandler((request, _) =>
            {
                if (request.RequestUri!.Host == "en.wikipedia.org")
                { sawQuery = request.RequestUri.Query.Contains("gsrsearch=Frieren") && request.RequestUri.Query.Contains("pithumbsize=1024") && request.Headers.Authorization is null; return Task.FromResult(Json(new { query = new { pages = new[] { new { title = "Frieren (character)", fullurl = "https://en.wikipedia.org/wiki/Frieren_(character)", thumbnail = new { source = "https://upload.wikimedia.org/frieren.png" } } } } })); }
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10]) }; response.Content.Headers.ContentType = new("image/png"); return Task.FromResult(response);
            }));
            var reference = (await search.SearchAsync("Frieren", default)).Single();
            var file = await search.DownloadAsync(reference, Path.Combine(root, "web-references"), default);
            assert(sawQuery && File.Exists(file), "No se obtuvo la referencia por el protocolo previsto.");
            var paths = new AppPaths(Path.Combine(root, "web-provenance")); var store = new VisualJobStore(paths); store.Save(new() { OnlineReference = reference });
            assert(store.Read().Single().OnlineReference == reference, "Se perdió el origen de la referencia.");
        });
        await test("Visual: formatos falsos, redirecciones y cancelación no se convierten en referencias", async () =>
        {
            var reference = new VisualReference("Fixture", "https://en.wikipedia.org/wiki/Fixture", "https://upload.wikimedia.org/a.png", "Fixture");
            using var invalid = new VisualReferenceSearch(new AsyncHandler((_, _) => Task.FromResult(Json(new { error = "not an image" }))));
            try { await invalid.DownloadAsync(reference, root, default); throw new Exception("Se aceptó texto como imagen."); } catch (InvalidDataException) { }
            using var redirected = new VisualReferenceSearch(new AsyncHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect))));
            try { await redirected.SearchAsync("Fixture", default); throw new Exception("Se aceptó una redirección."); } catch (HttpRequestException) { }
            using var waiting = new VisualReferenceSearch(new AsyncHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json(new { }); }));
            using var cancel = new CancellationTokenSource(50);
            try { await waiting.SearchAsync("Fixture", cancel.Token); throw new Exception("Se ignoró la cancelación."); } catch (OperationCanceledException) { }
        });
        await test("Visual: exportación MP4 usa DynamicCombo plano y errores distinguen memoria", () =>
        {
            var graph = JsonSerializer.SerializeToElement(VisualWorkflows.Build(new(VisualTask.Animation, "Robot", 768, 448, 1), new string('a', 32)));
            var inputs = graph.GetProperty("14").GetProperty("inputs");
            assert(inputs.GetProperty("format").GetString() == "mp4" && inputs.GetProperty("format.codec").GetString() == "h264", "Formato incompatible con la API dinámica.");
            using var typeError = JsonDocument.Parse("{\"messages\":[[\"execution_error\",{\"node_type\":\"SaveVideo\",\"exception_type\":\"TypeError\",\"exception_message\":\"missing format\"}]]}");
            var message = ComfyClient.DescribeFailure(typeError.RootElement);
            assert(message.Contains("SaveVideo") && message.Contains("missing format") && !message.Contains("sin memoria"), "Se atribuye un error de exportación a VRAM.");
            using var oom = JsonDocument.Parse("{\"messages\":[[\"execution_error\",{\"exception_type\":\"torch.OutOfMemoryError\"}]]}");
            assert(ComfyClient.DescribeFailure(oom.RootElement).Contains("sin memoria"), "Se perdió el diagnóstico de memoria.");
            AppPaths.AtomicWrite(Path.Combine(root, "video-export-graph.json"), graph.GetRawText());
            return Task.CompletedTask;
        });
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
            assert(settings.Mode == AppMode.Programming && settings.Visual.Width == 2048, "La migración alteró el modo inicial.");
            settings.Mode = AppMode.Visual; settings.Visual.IncludeAnimation = true; paths.SaveSettings(settings);
            var loaded = paths.LoadSettings();
            assert(loaded.Mode == AppMode.Visual && loaded.ContextTokens == 65536 && loaded.LastProject == settings.LastProject && loaded.Visual.IncludeAnimation, "Se mezclaron ajustes de los modos.");
            return Task.CompletedTask;
        });
        await test("Visual: catálogo completo fijado, animación opcional y sin Qwen/OpenCode", () =>
        {
            assert(VisualCatalog.Images.Length == 3 && VisualCatalog.For(true).Length == 5 && VisualCatalog.Legacy.Length == 2, "Faltan asistente, codificadores, VAE o gestión de modelos anteriores.");
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
            assert(edit.Contains("HiDreamO1ReferenceImages") && edit.Contains("referencia.png") && edit.Contains("Solo el brazo"), "No se pasó la edición condicionada.");
            assert(edit.Contains("ForjaFitReference") && !edit.Contains("ImageScale"), "La referencia de imagen se deforma para llenar el formato de salida.");
            assert(!create.Contains("HiDreamO1ReferenceImages") && !create.Contains("LoadImage"), "Imagen nueva hereda la anterior.");
            var referenced = JsonSerializer.Serialize(VisualWorkflows.Build(new(VisualTask.Image, "Frieren flying", 1024, 1024, 42), new string('b', 32), "wiki-reference.png"));
            assert(referenced.Contains("wiki-reference.png") && referenced.Contains("Create a new scene featuring the subject") && referenced.Contains("Frieren flying"), "No se aplica la referencia de identidad al crear la escena.");
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
                    assert(body.RootElement.GetProperty("prompt").GetProperty("32").GetProperty("inputs").GetProperty("text").GetString() == "Robot", "Se perdió el pedido.");
                    submitted = true; return Json(new { prompt_id = promptId });
                }
                return Json(new Dictionary<string, object> { [promptId] = new { status = new { completed = true, status_str = "success" }, outputs = new Dictionary<string, object> { ["32"] = new { text = new[] { "A white robot in a workshop." } }, ["final"] = new { images = new[] { new { filename = Path.GetFileName(file), subfolder = "", type = "output" } } } } } });
            }));
            var files = await new ComfyClient(http, new("http://127.0.0.1:12345/"), "private-fixture").GenerateAsync(new(VisualTask.Image, "Robot", 1024, 1024, 42), id, null, output, null, default, false);
            assert(submitted && files.Single() == file, "Se perdió la salida.");
        });
        await test("Visual: preparar la descripción no genera archivos y devuelve el texto del asistente", async () =>
        {
            var request = new VisualRequest(VisualTask.Image, "Robot", 1024, 1024, 42, EnhancePrompt: true);
            var prepared = VisualWorkflows.BuildPrepare(request, "reference.png");
            var nodes = prepared.Values.Select(n => JsonSerializer.SerializeToElement(n).GetProperty("class_type").GetString()!).Distinct().ToDictionary(x => x, _ => new { });
            var promptId = Guid.NewGuid().ToString(); var submitted = false;
            using var http = new HttpClient(new AsyncHandler(async (message, token) =>
            {
                if (message.RequestUri!.AbsolutePath == "/object_info") return Json(nodes);
                if (message.RequestUri.AbsolutePath == "/prompt")
                {
                    using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync());
                    var prompt = body.RootElement.GetProperty("prompt");
                    assert(prompt.TryGetProperty("31", out _) && !prompt.TryGetProperty("1", out _), "La preparación cargó el modelo de difusión.");
                    assert(prompt.GetProperty("20").GetProperty("inputs").GetProperty("image").GetString() == "reference.png", "La preparación perdió la referencia.");
                    submitted = true; return Json(new { prompt_id = promptId });
                }
                return Json(new Dictionary<string, object> { [promptId] = new { status = new { completed = true, status_str = "success" }, outputs = new Dictionary<string, object> { ["32"] = new { text = new[] { "Un robot blanco en un taller." } } } } });
            }));
            var client = new ComfyClient(http, new("http://127.0.0.1:12345/"), "fixture");
            var text = await client.PreparePromptAsync(request, "reference.png", null, default, false);
            assert(submitted && text == "Un robot blanco en un taller." && client.LastPreparedPrompt == text, "Se perdió la descripción preparada.");
        });
        await test("Visual: el texto revisado viaja a HiDream y queda en el historial", async () =>
        {
            var request = new VisualRequest(VisualTask.Image, "Robot", 1024, 1024, 42, EnhancePrompt: true);
            var id = new string('a', 32); var promptId = Guid.NewGuid().ToString(); var output = Path.Combine(root, "visual-prepared"); Directory.CreateDirectory(output);
            var file = Path.Combine(output, id + "_00001_.png"); File.WriteAllText(file, "fixture");
            var graph = VisualWorkflows.Build(request, id, null, "Texto revisado");
            var nodes = graph.Values.Select(n => JsonSerializer.SerializeToElement(n).GetProperty("class_type").GetString()!).Distinct().ToDictionary(x => x, _ => new { });
            var submitted = false;
            using var http = new HttpClient(new AsyncHandler(async (message, token) =>
            {
                if (message.RequestUri!.AbsolutePath == "/object_info") return Json(nodes);
                if (message.RequestUri.AbsolutePath == "/prompt")
                {
                    using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync());
                    var prompt = body.RootElement.GetProperty("prompt");
                    assert(prompt.GetProperty("4").GetProperty("inputs").GetProperty("text").GetString() == "Texto revisado" && !prompt.TryGetProperty("31", out _), "El motor no recibió el texto revisado.");
                    submitted = true; return Json(new { prompt_id = promptId });
                }
                return Json(new Dictionary<string, object> { [promptId] = new { status = new { completed = true, status_str = "success" }, outputs = new Dictionary<string, object> { ["13"] = new { images = new[] { new { filename = Path.GetFileName(file), subfolder = "", type = "output" } } } } } });
            }));
            var client = new ComfyClient(http, new("http://127.0.0.1:12345/"), "fixture");
            var files = await client.GenerateAsync(request, id, null, output, null, default, false, "Texto revisado");
            assert(submitted && files.Single() == file && client.LastPreparedPrompt == "Texto revisado", "Se perdió el texto revisado en el historial.");
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
