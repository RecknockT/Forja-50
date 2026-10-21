using System.Diagnostics;
using System.Text;
using MiIALocal.Core;

static class GitHubProbe
{
    public static async Task<int> RunAsync()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "MiIALocal-authprobe-" + Guid.NewGuid().ToString("N"), "espacios á ' $()"));
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        await new Provisioner(paths, new VerifiedDownloader(http)).PrepareGitHubAsync(null, default);
        if (File.Exists(paths.ActiveModelPath(new()))) throw new Exception("Se descargó un modelo de IA.");
        var environment = GitHubConnection.CredentialEnvironment(paths, false);
        environment["GCM_PROVIDER"] = "generic";
        environment["GIT_CONFIG_GLOBAL"] = "NUL"; environment["GIT_CONFIG_SYSTEM"] = "NUL";
        const string credential = "protocol=https\nhost=github.com\nusername=forja-fixture-user\npassword=forja-fixture-not-a-token\n\n";
        await Run(paths.GitHubCredentialExe, ["store"], environment, credential, paths.Root);
        var connection = new GitHubConnection(paths);
        var accounts = await connection.AccountsAsync(default);
        if (accounts.Count != 1 || accounts[0] != "forja-fixture-user") throw new Exception("GCM no recuperó la cuenta ficticia desde el almacén aislado.");
        foreach (var file in Directory.EnumerateFiles(paths.GitHubCredentialStore, "*", SearchOption.AllDirectories))
            if (Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file)).Contains("forja-fixture-not-a-token")) throw new Exception("El almacén no cifró la credencial ficticia.");
        var gitEnvironment = new Dictionary<string, string>(GitHubConnection.GitEnvironment(paths));
        gitEnvironment["GIT_CONFIG_GLOBAL"] = "NUL"; gitEnvironment["GIT_CONFIG_SYSTEM"] = "NUL";
        var filled = await Run(paths.GitExe, ["credential", "fill"], gitEnvironment, "protocol=https\nhost=github.com\n\n", paths.Root);
        if (!filled.Contains("username=forja-fixture-user") || !filled.Contains("password=forja-fixture-not-a-token")) throw new Exception("Git portable no utilizó la sesión aislada.");
        connection.Disconnect(new());
        if ((await connection.AccountsAsync(default)).Count != 0 || Directory.Exists(paths.GitHubCredentialStore) || File.Exists(paths.GitHubAccountFile)) throw new Exception("La sesión ficticia no se borró.");
        Console.WriteLine("PASS gestor oficial: descarga verificada, DPAPI cifrado, Git portable y cierre de sesión aislado; sin OAuth ni acceso a repositorios");
        Console.WriteLine("FIXTURE " + paths.Root);
        return 0;
    }
    private static async Task<string> Run(string exe, string[] arguments, IReadOnlyDictionary<string, string> environment, string input, string directory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var (key, value) in environment) info.Environment[key] = value;
        using var process = Process.Start(info) ?? throw new Exception("No se pudo iniciar el componente de prueba.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.StandardInput.WriteAsync(input); process.StandardInput.Close();
        try { await process.WaitForExitAsync(timeout.Token); await error; if (process.ExitCode != 0) throw new Exception("El componente de prueba devolvió un error; su salida privada no se registra."); return await output; }
        finally { if (!process.HasExited) process.Kill(); }
    }
}
