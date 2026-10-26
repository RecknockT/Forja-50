using System.Diagnostics;
using MiIALocal.Core;

static class StorageCleanupTests
{
    public static async Task RunAsync(string root, Action<bool, string> assert, Func<string, Func<Task>, Task> test)
    {
        async Task<string> Put(string file) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, "conservar o eliminar según selección"); return file; }
        void Blocked(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("Se permitió una limpieza protegida."); }
        await test("Limpieza: packs Git de solo lectura se retiran sin tocar modelos ni archivos importados", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-readonly")); var settings = new AppSettings();
            var index = await Put(Path.Combine(paths.Root, "agent-data", "opencode", "snapshot", "fixture", "objects", "pack", "pack-fixture.idx"));
            var pack = await Put(Path.ChangeExtension(index, ".pack"));
            var model = await Put(paths.ActiveModelPath(settings));
            File.SetAttributes(index, File.GetAttributes(index) | FileAttributes.ReadOnly);
            File.SetAttributes(pack, File.GetAttributes(pack) | FileAttributes.ReadOnly);
            File.SetAttributes(model, File.GetAttributes(model) | FileAttributes.ReadOnly);
            new StorageCleanup(paths, settings).Delete([StorageCategory.Conversations]);
            assert(!File.Exists(index) && !File.Exists(pack) && File.Exists(model)
                && (File.GetAttributes(model) & FileAttributes.ReadOnly) != 0, "Se dejó un pack bloqueado o se alteró un modelo no seleccionado.");
            new StorageCleanup(paths, settings).Delete([StorageCategory.Models]);
            assert(!File.Exists(model), "No se pudo borrar un modelo seleccionado de solo lectura.");
        });
        await test("Limpieza: borrar un modelo conserva los demás modelos y modos", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-individual")); var settings = new AppSettings();
            var first = await Put(paths.ModelPath(settings)); var second = await Put(paths.NinferModelPath(settings));
            var image = await Put(VisualCatalog.PathFor(paths, VisualCatalog.HiDreamFull));
            var video = await Put(VisualCatalog.PathFor(paths, VisualCatalog.Wan));
            var service = new StorageCleanup(paths, settings); var plan = service.Inspect();
            assert(plan.Count(e => e.Category == StorageCategory.Models && e.Paths.Length > 0) == 2
                && plan.Count(e => e.Category == StorageCategory.VisualModels && e.Paths.Length > 0) == 2, "Los modelos no se presentan separados por modo y archivo.");
            var selected = plan.Single(e => e.Key == ReleaseProfile.Model.Id);
            service.DeleteEntries([selected]);
            assert(!File.Exists(first) && File.Exists(second) && File.Exists(image) && File.Exists(video), "Borrar un modelo arrastró otros modelos.");
            var visual = service.Inspect().Single(e => e.Key == VisualCatalog.HiDreamFull.Asset.Id);
            service.DeleteEntries([visual]);
            assert(!File.Exists(image) && File.Exists(second) && File.Exists(video), "La selección visual borró otro componente.");
        });
        await test("Limpieza: selección obsoleta o alterada no borra nada", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-stale")); var settings = new AppSettings();
            var first = await Put(paths.ModelPath(settings)); var second = await Put(paths.NinferModelPath(settings));
            var service = new StorageCleanup(paths, settings);
            var selection = service.Inspect().Single(e => e.Key == ReleaseProfile.Model.Id);
            Blocked(() => service.DeleteEntries([selection with { Paths = [second] }]));
            assert(File.Exists(first) && File.Exists(second), "Se aceptó una ruta alterada.");
            var other = service.Inspect().Single(e => e.Key == ReleaseProfile.NinferModel.Id);
            File.Delete(second);
            Blocked(() => service.DeleteEntries([selection, other]));
            assert(File.Exists(first), "La selección se validó después de comenzar el borrado.");
        });
        await test("Limpieza: cancelar el cálculo de espacio conserva los archivos", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-cancel"));
            var model = await Put(paths.ActiveModelPath(new()));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { new StorageCleanup(paths, new()).Inspect(cancel.Token); throw new Exception("El cálculo ignoró la cancelación."); }
            catch (OperationCanceledException) { }
            assert(File.Exists(model), "Cancelar el cálculo borró un modelo.");
        });
        await test("Limpieza: inspección sin borrar y categorías independientes", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-select")); var settings = new AppSettings();
            var model = await Put(paths.ActiveModelPath(settings)); var runtime = await Put(paths.NinferEngineExe(settings)); var config = await Put(paths.SettingsFile);
            var service = new StorageCleanup(paths, settings); var plan = service.Inspect();
            assert(plan.Single(e => e.Category == StorageCategory.Models).Bytes == new FileInfo(model).Length && File.Exists(model), "La inspección borró o calculó mal el modelo.");
            service.Delete([StorageCategory.Components]);
            assert(!File.Exists(runtime) && File.Exists(model) && File.Exists(config), "Se borraron categorías no seleccionadas.");
        });
        await test("Limpieza: caché y parciales sin borrar el modelo ni archivos desconocidos", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-partials")); var settings = new AppSettings();
            var model = await Put(paths.ActiveModelPath(settings)); var partial = await Put(model + ".part");
            var chunk = await Put(Path.Combine(model + ".chunks-test-64", "0-63.bin"));
            var archive = await Put(Path.Combine(paths.Downloads, "component.zip")); var unknown = await Put(Path.Combine(Path.GetDirectoryName(model)!, "mis-notas.txt"));
            var update = await Put(Path.Combine(paths.Root, "updates", "Forja-Setup.exe"));
            var service = new StorageCleanup(paths, settings); service.Delete([StorageCategory.Downloads]);
            assert(!File.Exists(partial) && !File.Exists(chunk) && !File.Exists(archive) && !File.Exists(update) && File.Exists(model) && File.Exists(unknown), "La limpieza de caché salió de su alcance.");
            service.Delete([StorageCategory.Downloads]);
        });
        await test("Limpieza: modelos externos administrados e importados protegidos", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-external"));
            var settings = new AppSettings { ModelDirectory = Path.Combine(root, "custom-models") };
            var managed = await Put(paths.ActiveModelPath(settings)); var imported = await Put(paths.ModelPath(settings)); settings.ImportedModel = imported;
            var neighbor = await Put(Path.Combine(settings.ModelDirectory, "otro-modelo.ninfer"));
            new StorageCleanup(paths, settings).Delete([StorageCategory.Models]);
            assert(!File.Exists(managed) && File.Exists(imported) && File.Exists(neighbor), "Se borró un modelo importado o desconocido.");
        });
        await test("Limpieza: validar todo antes de borrar si hay un repositorio anidado", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-repo")); var settings = new AppSettings();
            var model = await Put(paths.ActiveModelPath(settings)); var code = await Put(Path.Combine(paths.Runtime, "proyecto", "main.cs"));
            await Put(Path.Combine(paths.Runtime, "proyecto", ".git", "HEAD"));
            Blocked(() => new StorageCleanup(paths, settings).Delete([StorageCategory.Models, StorageCategory.Components]));
            assert(File.Exists(model) && File.Exists(code), "La validación parcial borró datos antes de detectar el repositorio.");
        });
        await test("Limpieza: proteger proyecto sin Git y archivos Git de worktree", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-project")); var settings = new AppSettings { LastProject = Path.Combine(root, "cleanup-project", "agent-data", "proyecto") };
            var code = await Put(Path.Combine(settings.LastProject, "main.txt"));
            Blocked(() => new StorageCleanup(paths, settings).Delete([StorageCategory.Conversations]));
            assert(File.Exists(code), "Se borró la carpeta del proyecto.");
            settings.LastProject = ""; await Put(Path.Combine(paths.Runtime, ".git"));
            Blocked(() => new StorageCleanup(paths, settings).Delete([StorageCategory.Components]));
        });
        await test("Limpieza: conservar importaciones dentro de los componentes", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-imported-engine")); var settings = new AppSettings();
            settings.NinferEnginePath = await Put(Path.Combine(paths.Runtime, "importado", "ninfer-serve.exe"));
            Blocked(() => new StorageCleanup(paths, settings).Delete([StorageCategory.Components]));
            assert(File.Exists(settings.NinferEnginePath), "Se borró un motor importado.");
        });
        if (OperatingSystem.IsWindows()) await test("Limpieza: rechazar junction agregada después de inspeccionar", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-link")); var settings = new AppSettings();
            var outside = Path.Combine(root, "outside-link"); var keep = await Put(Path.Combine(outside, "proyecto.txt"));
            var service = new StorageCleanup(paths, settings); _ = service.Inspect();
            var link = Path.Combine(paths.Runtime, "enlace");
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, outside }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!; await process.WaitForExitAsync();
            assert(process.ExitCode == 0, "No se pudo crear la junction de prueba.");
            try { Blocked(() => service.Delete([StorageCategory.Components])); assert(File.Exists(keep), "Se siguió la junction fuera del destino."); }
            finally { Directory.Delete(link, false); }
        });
        await test("Limpieza total: conservar proyecto, importación y archivos ajenos", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "cleanup-all")); var settings = new AppSettings { LastProject = Path.Combine(root, "project-kept") };
            var project = await Put(Path.Combine(settings.LastProject, "main.cs"));
            settings.ImportedNinferModel = await Put(Path.Combine(root, "import-kept.ninfer"));
            var managed = await Put(paths.ModelPath(settings)); var tool = await Put(paths.AgentExe);
            var chat = await Put(Path.Combine(paths.Root, "agent-data", "chat.json")); await Put(paths.SettingsFile);
            var browser = await Put(Path.Combine(paths.Root, "browser", "cache.bin"));
            var unknown = await Put(Path.Combine(paths.Root, "mi-archivo.txt"));
            new StorageCleanup(paths, settings).Delete(Enum.GetValues<StorageCategory>());
            assert(!File.Exists(managed) && !File.Exists(tool) && !File.Exists(chat) && !File.Exists(browser) && !File.Exists(paths.SettingsFile), "Quedaron datos seleccionados.");
            assert(File.Exists(project) && File.Exists(settings.ImportedNinferModel) && File.Exists(unknown), "Se borraron archivos protegidos.");
        });
    }
}
