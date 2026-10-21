using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiIALocal.Core;

public sealed record GitHubCommandResult(int ExitCode, string Output);
public sealed record GitHubAccount(string Login);

/// <summary>Browser OAuth through GCM with an isolated Windows DPAPI store. No credential logs.</summary>
public sealed class GitHubConnection(AppPaths paths, Func<IReadOnlyList<string>, CancellationToken, Task<GitHubCommandResult>>? command = null)
{
    public bool ToolsReady => File.Exists(paths.GitExe) && File.Exists(paths.GitHubCredentialExe);
    public static bool ValidLogin(string? login) => login is not null && Regex.IsMatch(login, @"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?\z", RegexOptions.CultureInvariant);
    public static GitHubAccount? SavedAccount(AppPaths paths)
    {
        try
        {
            if (!File.Exists(paths.GitHubAccountFile) || !Directory.Exists(paths.GitHubCredentialStore)) return null;
            var account = JsonSerializer.Deserialize<GitHubAccount>(File.ReadAllText(paths.GitHubAccountFile));
            return ValidLogin(account?.Login) ? account : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    public static Dictionary<string, string> CredentialEnvironment(AppPaths paths, bool interactive) => new()
    {
        ["GCM_NAMESPACE"] = "forja", ["GCM_CREDENTIAL_STORE"] = "dpapi",
        ["GCM_DPAPI_STORE_PATH"] = paths.GitHubCredentialStore,
        ["GCM_INTERACTIVE"] = interactive ? "always" : "never", ["GCM_GITHUB_AUTHMODES"] = "browser",
        ["GCM_TRACE"] = "0", ["GCM_TRACE_SECRETS"] = "0",
        ["PATH"] = Path.GetDirectoryName(paths.GitExe) + ";" + Environment.GetEnvironmentVariable("PATH")
    };
    public static IReadOnlyDictionary<string, string> GitEnvironment(AppPaths paths, string? inheritedCount = null)
    {
        var account = SavedAccount(paths);
        if (account is null || !File.Exists(paths.GitHubCredentialExe)) return new Dictionary<string, string>();
        var countText = inheritedCount ?? Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT") ?? "0";
        if (!int.TryParse(countText, out var count) || count is < 0 or > 100) throw new InvalidDataException("La configuración heredada de Git no es válida.");
        var result = CredentialEnvironment(paths, false); result.Remove("PATH");
        result["GIT_CONFIG_COUNT"] = (count + 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
        result[$"GIT_CONFIG_KEY_{count}"] = "credential.https://github.com.helper";
        result[$"GIT_CONFIG_VALUE_{count}"] = "";
        result[$"GIT_CONFIG_KEY_{count + 1}"] = "credential.https://github.com.helper";
        result[$"GIT_CONFIG_VALUE_{count + 1}"] = "!'" + paths.GitHubCredentialExe.Replace('\\', '/').Replace("'", "'\\''") + "'";
        result[$"GIT_CONFIG_KEY_{count + 2}"] = "credential.https://github.com.username";
        result[$"GIT_CONFIG_VALUE_{count + 2}"] = account.Login;
        return result;
    }
    public async Task<IReadOnlyList<string>> AccountsAsync(CancellationToken ct)
    {
        if (command is null && !ToolsReady) return SavedAccount(paths) is { } saved ? [saved.Login] : [];
        var result = await RunAsync(["github", "list"], ct);
        if (result.ExitCode != 0) throw new IOException("No se pudo consultar la sesión de GitHub. Podés volver a preparar las herramientas.");
        var accounts = result.Output.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (accounts.Length > 20 || accounts.Any(a => !ValidLogin(a))) throw new IOException("El gestor devolvió una lista de cuentas no reconocida.");
        if (accounts.Length == 1) AppPaths.AtomicWrite(paths.GitHubAccountFile, JsonSerializer.Serialize(new GitHubAccount(accounts[0])));
        else if (File.Exists(paths.GitHubAccountFile)) File.Delete(paths.GitHubAccountFile);
        return accounts;
    }
    public async Task<string> LoginAsync(CancellationToken ct)
    {
        if ((await AccountsAsync(ct)).Count != 0) throw new InvalidOperationException("Primero cerrá la sesión de Forja para cambiar de cuenta.");
        var result = await RunAsync(["github", "login", "--browser"], ct);
        if (result.ExitCode != 0) throw new IOException("No se completó la autorización de GitHub. Podés reintentar; no ingreses tu contraseña en Forja.");
        var accounts = await AccountsAsync(ct);
        if (accounts.Count != 1) throw new IOException("No se pudo confirmar una única cuenta de GitHub. Cerrá la sesión de Forja y reintentá.");
        return accounts[0];
    }
    public void Disconnect(AppSettings settings) => new StorageCleanup(paths, settings).Delete([StorageCategory.GitHub]);
    private Task<GitHubCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct) => command is null ? RunPrivateAsync(arguments, ct) : command(arguments, ct);
    private async Task<GitHubCommandResult> RunPrivateAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        if (!ToolsReady) throw new IOException("Primero prepará las herramientas de GitHub.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(arguments.Contains("login") ? TimeSpan.FromMinutes(8) : TimeSpan.FromSeconds(20));
        var info = new ProcessStartInfo(paths.GitHubCredentialExe)
        {
            WorkingDirectory = paths.Root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var (key, value) in CredentialEnvironment(paths, arguments.Contains("login"))) info.Environment[key] = value;
        info.Environment["GIT_CONFIG_GLOBAL"] = "NUL"; info.Environment["GIT_CONFIG_SYSTEM"] = "NUL";
        using var process = Process.Start(info) ?? throw new IOException("No se pudo abrir el gestor de GitHub.");
        // GCM reserves stdout for Git's credential protocol; its account list is written to stderr.
        var listAccounts = arguments.SequenceEqual(new[] { "github", "list" });
        var output = ReadBoundedAsync(process.StandardOutput, listAccounts, timeout.Token);
        var error = ReadBoundedAsync(process.StandardError, listAccounts, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token); await error;
            return new(process.ExitCode, (await output) + (await error));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("La autorización de GitHub tardó demasiado. Podés reintentar."); }
        finally
        {
            // Only stop GCM itself: the user's browser must remain open.
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(CancellationToken.None); }
            try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
        }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, bool retain, CancellationToken ct)
    {
        var result = new StringBuilder(); var buffer = new char[2048];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), ct);
            if (count == 0) return result.ToString();
            if (retain && result.Length + count <= 16384) result.Append(buffer, 0, count);
        }
    }
}
