using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiIALocal.Core;

public sealed record VisualReference(string Title, string PageUrl, string ImageUrl, string Query)
{
    public override string ToString() => Title;
}

/// <summary>Public Wikipedia discovery; remote text never becomes model instructions.</summary>
public sealed class VisualReferenceSearch : IDisposable
{
    private readonly HttpClient http;
    public VisualReferenceSearch(HttpMessageHandler? handler = null)
    {
        http = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.All });
        http.Timeout = TimeSpan.FromSeconds(25);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Forja50/0.3 (https://github.com/RecknockT/Forja-50)");
    }

    public static string SuggestSubject(string prompt)
    {
        var words = Regex.Matches(prompt, @"[\p{L}\p{N}]+(?:[-'][\p{L}\p{N}]+)*").Select(m => m.Value).ToList();
        var leading = new HashSet<string>("generar genera generá generate crear crea creá create dibujar dibuja dibujá draw hacer hace hacé haz quiero una un an imagen image foto picture of de del a por favor please me dame mostrame mostrar render".Split(' '), StringComparer.OrdinalIgnoreCase);
        while (words.Count > 0 && leading.Contains(words[0])) words.RemoveAt(0);
        var actions = new HashSet<string>("volando flying caminando walking corriendo running sentado sentada sitting saltando jumping luchando fighting bailando dancing con with sobre above en in bajo under al at estilo style".Split(' '), StringComparer.OrdinalIgnoreCase);
        var stop = words.Count > 1 ? words.FindIndex(1, words.Count - 1, word => actions.Contains(word)) : -1;
        if (stop >= 0) words.RemoveRange(stop, words.Count - stop);
        var subject = string.Join(' ', words.Take(6));
        return subject.Length > 100 ? subject[..100] : subject;
    }

    public async Task<VisualReference[]> SearchAsync(string subject, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(25)); ct = deadline.Token;
        subject = subject.Trim();
        if (subject.Length is < 1 or > 100) throw new InvalidDataException("Escribí el personaje u objeto que querés buscar (hasta 100 caracteres).");
        var url = "https://en.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&generator=search&gsrnamespace=0&gsrlimit=8&prop=pageimages%7Cinfo&inprop=url&piprop=thumbnail&pithumbsize=768&pilicense=any&gsrsearch=" + Uri.EscapeDataString(subject);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var bytes = await ReadLimitedAsync(response, 1_000_000, ct);
        using var json = JsonDocument.Parse(bytes);
        if (json.RootElement.TryGetProperty("error", out _)) throw new IOException("Wikipedia no pudo completar la búsqueda. Probá de nuevo más tarde.");
        if (!json.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return [];
        var results = new List<VisualReference>();
        foreach (var page in pages.EnumerateArray())
        {
            if (!page.TryGetProperty("thumbnail", out var thumbnail) || !thumbnail.TryGetProperty("source", out var source)
                || !page.TryGetProperty("fullurl", out var fullUrl) || !page.TryGetProperty("title", out var title)) continue;
            var image = source.GetString() ?? ""; var article = fullUrl.GetString() ?? "";
            if (!AllowedImage(image) || !AllowedPage(article)) continue;
            results.Add(new(title.GetString() ?? subject, article, image, subject));
        }
        return results.OrderByDescending(r => MatchScore(r.Title, subject)).Take(8).ToArray();
    }

    private static int MatchScore(string title, string subject)
    {
        if (title.Equals(subject + " (character)", StringComparison.OrdinalIgnoreCase)) return 300;
        if (title.Equals(subject, StringComparison.OrdinalIgnoreCase)) return 200;
        return title.StartsWith(subject + " (", StringComparison.OrdinalIgnoreCase) ? 100 : 0;
    }
    public static VisualReference? AutomaticMatch(VisualReference[] choices, string subject)
    {
        var ranked = choices.Where(r => MatchScore(r.Title, subject) >= 200).OrderByDescending(r => MatchScore(r.Title, subject)).ToArray();
        return ranked.Length > 0 && (ranked.Length == 1 || MatchScore(ranked[0].Title, subject) > MatchScore(ranked[1].Title, subject)) ? ranked[0] : null;
    }
    public static bool AllowedImage(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "upload.wikimedia.org" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);
    public static bool AllowedPage(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "en.wikipedia.org" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) && uri.AbsolutePath.StartsWith("/wiki/", StringComparison.Ordinal);

    public async Task<string> DownloadAsync(VisualReference reference, string folder, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(25)); ct = deadline.Token;
        if (!AllowedImage(reference.ImageUrl) || !AllowedPage(reference.PageUrl)) throw new InvalidDataException("Origen de referencia no permitido.");
        using var response = await http.GetAsync(reference.ImageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var media = response.Content.Headers.ContentType?.MediaType;
        var extension = media switch { "image/png" => ".png", "image/jpeg" => ".jpg", _ => throw new InvalidDataException("La referencia no es una imagen PNG/JPG compatible.") };
        var bytes = await ReadLimitedAsync(response, 8_000_000, ct);
        var signature = extension == ".png" ? bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) : bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        if (!signature) throw new InvalidDataException("La referencia recibida no contiene una imagen válida.");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, Guid.NewGuid().ToString("N") + extension);
        await File.WriteAllBytesAsync(file, bytes, ct);
        return file;
    }
    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int maximum, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("La respuesta supera el tamaño permitido.");
        await using var input = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream();
        var buffer = new byte[16384]; int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        { if (output.Length + count > maximum) throw new InvalidDataException("La respuesta supera el tamaño permitido."); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
    public void Dispose() => http.Dispose();
}
