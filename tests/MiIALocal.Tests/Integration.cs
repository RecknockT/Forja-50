using System.Diagnostics;
using System.Text.Json;
using MiIALocal.Core;

internal static class Integration
{
    public static async Task<int> RunAsync(string[] args)
    {
        string Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Falta " + name); }
        var paths = new AppPaths(Arg("--data-root"));
        var reference = Path.GetFullPath(Arg("--reference"));
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        await using var supervisor = new RuntimeSupervisor(paths, http);
        try
        {
            var settings = paths.LoadSettings();
            settings.ImportedModel = Path.Combine(reference, "models", ReleaseProfile.Model.FileName);
            settings.LicenseAccepted = true; // Developer test uses the already installed reference model.
            foreach (var asset in new[] { ReleaseProfile.Engine, ReleaseProfile.Agent })
            {
                var original = Path.Combine(reference, "downloads", asset.FileName);
                var destination = Path.Combine(paths.Downloads, asset.FileName);
                if (!File.Exists(destination)) File.Copy(original, destination);
            }
            var progress = new Progress<PreparationProgress>(p => { if (p.Percentage is null) Console.WriteLine(p.Stage); });
            await new Provisioner(paths, new VerifiedDownloader(http)).PrepareAsync(settings, progress, CancellationToken.None);
            var project = Path.Combine(paths.Root, "verification", "Proyecto de prueba á"); Directory.CreateDirectory(project);
            settings.LastProject = project; paths.SaveSettings(settings);
            var git = paths.GitExe;
            if (!Directory.Exists(Path.Combine(project, ".git")))
            {
                await Run(git, ["init", "--initial-branch=principal"], project);
                await Run(git, ["config", "user.name", "Prueba Mi IA Local"], project);
                await Run(git, ["config", "user.email", "mi-ia-local-test@localhost"], project);
                await File.WriteAllTextAsync(Path.Combine(project, "resultado.txt"), "pendiente\n");
                await File.WriteAllTextAsync(Path.Combine(project, "README.md"), "Prueba aislada. Cambiar resultado.txt de pendiente a listo-5080.\n");
                await Run(git, ["add", "README.md", "resultado.txt"], project);
                await Run(git, ["commit", "-m", "Preparar prueba aislada"], project);
            }
            if (args.Contains("--prepare-only")) { Console.WriteLine("PASS componentes y modelo preparados para la aplicación"); return 0; }
            var hardware = await HardwareDetector.DetectAsync(default);
            if (hardware.Problem is { } problem) throw new InvalidOperationException(problem);
            var gpu = hardware.SelectedGpu!;
            if (gpu.FreeMiB < 13000) throw new InvalidOperationException("Detené temporalmente la instalación anterior para realizar la prueba de GPU; la prueba no la cerrará por su cuenta.");
            var uri = await supervisor.StartAsync(settings, gpu, project, default);
            Console.WriteLine("PASS motor y agente propios listos: " + uri);
            var env = new Dictionary<string, string>
            {
                ["XDG_DATA_HOME"] = Path.Combine(paths.Root, "test-client", "data"), ["XDG_CONFIG_HOME"] = Path.Combine(paths.Root, "test-client", "config"),
                ["XDG_CACHE_HOME"] = Path.Combine(paths.Root, "test-client", "cache"), ["XDG_STATE_HOME"] = Path.Combine(paths.Root, "test-client", "state"),
                ["OPENCODE_DISABLE_AUTOUPDATE"] = "true", ["OPENCODE_DISABLE_DEFAULT_PLUGINS"] = "true",
                ["PATH"] = Path.GetDirectoryName(paths.GitExe) + ";" + Path.GetDirectoryName(paths.NodeExe) + ";" + Environment.GetEnvironmentVariable("PATH")
            };
            var output = await Run(paths.AgentExe, ["run", "--attach", $"http://127.0.0.1:{supervisor.AgentPort}", "--username", "opencode", "--password", supervisor.AgentPassword, "--dir", project, "--model", "local/qwen-local", "--format", "json", "Esta es una prueba autorizada en un repositorio aislado. Usa herramientas reales para leer README.md y resultado.txt, ejecutar git status, crear la rama prueba-aplicacion-local si no existe, cambiar resultado.txt a una unica linea listo-5080, verificar su contenido, ejecutar git diff --check, agregar solo resultado.txt y crear un commit con mensaje Verificar aplicacion local. No accedas a otros directorios ni uses subagentes. Si ya esta hecho verifica el resultado. Responde breve en espanol."], project, env, TimeSpan.FromMinutes(5));
            await File.WriteAllTextAsync(Path.Combine(paths.Logs, "integration-agent.jsonl"), output);
            var branch = (await Run(git, ["branch", "--show-current"], project)).Trim();
            var contents = (await File.ReadAllTextAsync(Path.Combine(project, "resultado.txt"))).Trim();
            var status = (await Run(git, ["status", "--porcelain"], project)).Trim();
            if (branch != "prueba-aplicacion-local" || contents != "listo-5080" || status.Length != 0) throw new InvalidDataException("La prueba no terminó con la rama, archivo y árbol limpio previstos.");
            Console.WriteLine("PASS agente: lectura, edición, PowerShell, rama, commit y árbol limpio");
            await supervisor.StopAsync(); Console.WriteLine("PASS detención de procesos propios");
            await supervisor.StartAsync(settings, gpu, project, default); await supervisor.StopAsync();
            Console.WriteLine("PASS reinicio de modelo y agente");
            AppPaths.AtomicWrite(Path.Combine(paths.Logs, "integration-result.json"), JsonSerializer.Serialize(new { date = DateTimeOffset.Now, hardware = gpu.Name, branch, contents, status = "PASS", scope = "isolated-test-repository" }, AppPaths.JsonOptions));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static async Task<string> Run(string exe, IEnumerable<string> args, string cwd, IReadOnlyDictionary<string, string>? env = null, TimeSpan? timeout = null)
    {
        var info = new ProcessStartInfo(exe) { WorkingDirectory = cwd, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in args) info.ArgumentList.Add(argument);
        if (env is not null) foreach (var (key, value) in env) info.Environment[key] = value;
        using var process = Process.Start(info) ?? throw new IOException("No se pudo iniciar el comando de prueba.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(timeout ?? TimeSpan.FromSeconds(20)); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var result = await stdout; var error = await stderr;
        if (process.ExitCode != 0) throw new IOException($"El comando terminó con código {process.ExitCode}: {error}");
        return result;
    }
}
