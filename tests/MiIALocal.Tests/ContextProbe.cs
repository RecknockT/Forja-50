using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MiIALocal.Core;

internal static class ContextProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        string Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Falta " + name); }
        var paths = new AppPaths(Arg("--data-root"));
        var settings = paths.LoadSettings();
        try
        {
            var hardware = await HardwareDetector.DetectAsync(default);
            if (hardware.Problem is { } problem) throw new InvalidOperationException(problem);
            var gpu = hardware.SelectedGpu!;
            if (gpu.FreeMiB < 13000) throw new InvalidOperationException("La GPU no tiene memoria libre para esta prueba.");
            if (!await VerifiedDownloader.MatchesAsync(paths.ModelPath(settings), ReleaseProfile.Model, default)) throw new InvalidDataException("Modelo incorrecto.");
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(8) };
            var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var port = RuntimeSupervisor.FreePort();
            var clock = Stopwatch.StartNew();
            await using var engine = OwnedProcess.Start(paths.EngineExe, RuntimeSupervisor.EngineArguments(paths.ModelPath(settings), gpu.Index, port, key, 65536), Path.GetDirectoryName(paths.EngineExe)!, Path.Combine(paths.Logs, "context64-model"));
            await engine.WaitForReadyAsync(client, new($"http://127.0.0.1:{port}/health"), "Bearer " + key, TimeSpan.FromMinutes(4), default);
            var loadSeconds = clock.Elapsed.TotalSeconds;
            Console.WriteLine($"PASS motor 64K listo en {loadSeconds:0.0}s");
            client.DefaultRequestHeaders.Authorization = new("Bearer", key);
            var results = new List<object>();
            // More than 32K real tokens, with independently generated facts outside the question.
            foreach (var rows in new[] { 1800, 2200 })
            {
                var values = Enumerable.Range(0, 3).Select(_ => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))).ToArray();
                var prompt = new StringBuilder("Documento de prueba. Conserva los tres valores especiales para responder al final.\nCLAVE_INICIO=" + values[0] + "\n");
                for (var i = 0; i < rows; i++)
                {
                    if (i == rows / 2) prompt.AppendLine("CLAVE_MEDIO=" + values[1]);
                    prompt.AppendLine($"Registro {i:D5}: proyecto local, archivo ordinario sin datos especiales. Estado revisado correctamente.");
                }
                prompt.AppendLine("CLAVE_FINAL=" + values[2]);
                prompt.AppendLine("Devuelve exactamente CLAVE_INICIO, CLAVE_MEDIO y CLAVE_FINAL con sus valores. No inventes ni resumas. Sin explicaciones.");
                clock.Restart();
                using var response = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/v1/chat/completions", new { model = "qwen-local", messages = new[] { new { role = "user", content = prompt.ToString() } }, max_tokens = 1024, thinking_budget = 128, temperature = 0, stream = false });
                var body = await response.Content.ReadAsStringAsync();
                await File.WriteAllTextAsync(Path.Combine(paths.Logs, $"context64-response-{rows}.json"), body);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(body);
                var tokens = json.RootElement.GetProperty("usage").GetProperty("prompt_tokens").GetInt32();
                var answer = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                var matched = values.Select(answer.Contains).ToArray();
                results.Add(new { rows, promptTokens = tokens, seconds = clock.Elapsed.TotalSeconds, recalled = matched, answer });
                AppPaths.AtomicWrite(Path.Combine(paths.Logs, "context64-result.json"), JsonSerializer.Serialize(new { date = DateTimeOffset.Now, gpu = gpu.Name, context = 65536, loadSeconds, results }, AppPaths.JsonOptions));
                Console.WriteLine($"Entrada {tokens} tokens; recuperación {matched.Count(x => x)}/3; {clock.Elapsed.TotalSeconds:0.0}s");
                if (tokens >= 65536 || tokens < 32768) throw new InvalidDataException("La entrada no quedó dentro del rango de prueba esperado (32K..64K).");
                if (matched.Any(x => !x)) throw new InvalidDataException("La ventana ampliada respondió pero no recuperó todos los datos. No promover el perfil a validado.");
            }
            Console.WriteLine("PASS dos entradas mayores de 32K, recuperación de inicio/medio/final. El motor se detiene al salir.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
