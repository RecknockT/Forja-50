using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiIALocal.Core;

public sealed record AppUpdate(string Version, string ReleaseUrl, DownloadAsset Asset);

public static class AppUpdates
{
    public const string ReleasesUrl = "https://github.com/RecknockT/Forja-50/releases";
    public const string LegacyReleasesUrl = "https://github.com/RecknockT/Mi-IA-Local/releases";
    public const string ApiUrl = "https://api.github.com/repos/RecknockT/Forja-50/releases?per_page=100";

    public static async Task<AppUpdate?> CheckAsync(HttpClient client, string current, bool portable, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
        request.Headers.UserAgent.ParseAdd("Forja-50/" + ReleaseProfile.Version);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("GitHub no permite consultar las versiones. El repositorio puede ser privado o se alcanzó el límite de consultas. Abrí Versiones en GitHub con tu sesión para descargar la actualización.");
        response.EnsureSuccessStatusCode();
        return Select(await response.Content.ReadAsStringAsync(ct), current, portable);
    }

    public static AppUpdate? Select(string json, string current, bool portable)
    {
        var currentNumber = Number(current);
        using var document = JsonDocument.Parse(json);
        AppUpdate? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var version = release.GetProperty("tag_name").GetString()!.TrimStart('v');
            if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+(-dev)?$")) continue;
            // Development builds follow development releases too; stable builds stay on stable.
            if (!current.EndsWith("-dev", StringComparison.Ordinal) && release.GetProperty("prerelease").GetBoolean()) continue;
            var number = Number(version);
            var newer = number > currentNumber || number == currentNumber && current.EndsWith("-dev", StringComparison.Ordinal) && !version.EndsWith("-dev", StringComparison.Ordinal);
            if (!newer || best is not null && (Number(best.Version) > number || Number(best.Version) == number && !best.Version.EndsWith("-dev", StringComparison.Ordinal))) continue;
            var files = new[] { $"Forja-50-{version}-" + (portable ? "Portable-win-x64.zip" : "Setup.exe"), $"Mi-IA-Local-{version}-" + (portable ? "win-x64.zip" : "Setup.exe") };
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var file = asset.GetProperty("name").GetString()!;
                if (!files.Contains(file)) continue;
                var url = asset.GetProperty("browser_download_url").GetString()!;
                var expected = ReleasesUrl + "/download/v" + version + "/" + file;
                var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
                var bytes = asset.GetProperty("size").GetInt64();
                var legacyExpected = LegacyReleasesUrl + "/download/v" + version + "/" + file;
                if ((url != expected && url != legacyExpected) || digest is null || !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$") || bytes <= 0) continue;
                if (best?.Version == version && best.Asset.FileName.StartsWith("Forja-50-", StringComparison.Ordinal) && file.StartsWith("Mi-IA-Local-", StringComparison.Ordinal)) continue;
                best = new(version, ReleasesUrl + "/tag/v" + version, new("update", "Actualización " + version, url, digest[7..], file, bytes));
            }
        }
        return best;
    }

    private static Version Number(string version) => Version.Parse(version.Split('-')[0]);
}
