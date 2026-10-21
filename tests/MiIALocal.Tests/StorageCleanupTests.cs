using System.Diagnostics;
using MiIALocal.Core;

static class StorageCleanupTests
{
    public static async Task RunAsync(string root, Action<bool, string> assert, Func<string, Func<Task>, Task> test)
    {
        async Task<string> Put(string file) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, "conservar o eliminar según selección"); return file; }
        void Blocked(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("Se permitió una limpieza protegida."); }
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
