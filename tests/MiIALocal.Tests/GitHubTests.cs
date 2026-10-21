using MiIALocal.Core;
using System.Text.Json;

static class GitHubTests
{
    public static async Task RunAsync(string root, Action<bool, string> assert, Func<string, Func<Task>, Task> test)
    {
        await test("GitHub: OAuth aislado, cuenta persistida y entorno HTTPS sin secretos", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "github-login")); var loggedIn = false;
            Directory.CreateDirectory(paths.GitHubCredentialStore); Directory.CreateDirectory(Path.GetDirectoryName(paths.GitHubCredentialExe)!);
            File.WriteAllText(paths.GitHubCredentialExe, "fixture");
            var connection = new GitHubConnection(paths, (arguments, _) =>
            {
                if (arguments.SequenceEqual(new[] { "github", "login", "--browser" })) { loggedIn = true; return Task.FromResult(new GitHubCommandResult(0, "")); }
                assert(arguments.SequenceEqual(new[] { "github", "list" }), "Se invocó un comando de credenciales no esperado.");
                return Task.FromResult(new GitHubCommandResult(0, loggedIn ? "fixture-user\n" : ""));
            });
            assert(await connection.LoginAsync(default) == "fixture-user" && GitHubConnection.SavedAccount(paths)?.Login == "fixture-user", "No se confirmó la cuenta.");
            var environment = GitHubConnection.GitEnvironment(paths, "2");
            assert(environment["GIT_CONFIG_COUNT"] == "5" && environment["GIT_CONFIG_KEY_2"] == "credential.https://github.com.helper" && environment["GIT_CONFIG_VALUE_2"] == "", "No se aisló la cadena de helpers HTTPS.");
            assert(environment["GCM_CREDENTIAL_STORE"] == "dpapi" && environment["GCM_DPAPI_STORE_PATH"] == paths.GitHubCredentialStore && environment["GCM_INTERACTIVE"] == "never", "Se reutilizará el almacén de otra app o habrá prompts automáticos.");
            assert(!environment.ContainsKey("PATH") && !environment.Keys.Any(k => k.Contains("TOKEN")), "Se alteró PATH o se expuso un token.");
            assert(File.ReadAllText(paths.GitHubAccountFile) == JsonSerializer.Serialize(new GitHubAccount("fixture-user")), "Se guardó más que el nombre público de cuenta.");
            try { await connection.LoginAsync(default); throw new Exception("Se permitió una segunda cuenta sin cerrar la anterior."); } catch (InvalidOperationException) { }
            try { GitHubConnection.GitEnvironment(paths, "-1"); throw new Exception("Se aceptó configuración heredada inválida."); } catch (InvalidDataException) { }
        });
        await test("GitHub: fallo/cancelación no guardan tokens ni mensajes del gestor", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "github-failed"));
            var failed = new GitHubConnection(paths, (arguments, _) => Task.FromResult(new GitHubCommandResult(arguments.Contains("login") ? 1 : 0, arguments.Contains("login") ? "token-que-no-debe-aparecer" : "")));
            try { await failed.LoginAsync(default); throw new Exception("No falló el login."); }
            catch (IOException error) { assert(!error.Message.Contains("token-que"), "Se expuso salida privada."); }
            assert(!File.Exists(paths.GitHubAccountFile), "Se guardó una cuenta tras fallar.");
            var canceled = new GitHubConnection(paths, (_, _) => Task.FromCanceled<GitHubCommandResult>(new CancellationToken(true)));
            try { await canceled.LoginAsync(default); throw new Exception("No se canceló."); } catch (OperationCanceledException) { }
            assert(!File.Exists(paths.GitHubAccountFile) && !Directory.EnumerateFiles(paths.Logs).Any(), "Se escribieron credenciales o registros.");
        });
        await test("GitHub: cerrar sesión elimina solo datos propios y protege proyectos", async () =>
        {
            var paths = new AppPaths(Path.Combine(root, "github-cleanup")); var settings = new AppSettings();
            Directory.CreateDirectory(paths.GitHubCredentialStore); File.WriteAllText(Path.Combine(paths.GitHubCredentialStore, "encrypted.fixture"), "fixture");
            File.WriteAllText(paths.GitHubAccountFile, JsonSerializer.Serialize(new GitHubAccount("fixture-user")));
            paths.SaveSettings(settings); var model = paths.ActiveModelPath(settings); Directory.CreateDirectory(Path.GetDirectoryName(model)!); File.WriteAllText(model, "fixture");
            new GitHubConnection(paths).Disconnect(settings);
            assert(!Directory.Exists(paths.GitHubCredentialStore) && !File.Exists(paths.GitHubAccountFile) && File.Exists(paths.SettingsFile) && File.Exists(model), "Cerrar sesión borró datos ajenos.");
            assert(GitHubConnection.GitEnvironment(paths).Count == 0, "Se activó un helper sin sesión.");
            Directory.CreateDirectory(paths.GitHubCredentialStore); settings.LastProject = paths.GitHubCredentialStore;
            try { new GitHubConnection(paths).Disconnect(settings); throw new Exception("Se borró una carpeta de proyecto."); } catch (IOException) { }
            foreach (var invalid in new[] { "", "-user", "user\nhelper", "ghp_fake_token", new string('a', 40) }) assert(!GitHubConnection.ValidLogin(invalid), "Se aceptó un identificador inválido.");
            await Task.CompletedTask;
        });
    }
}
