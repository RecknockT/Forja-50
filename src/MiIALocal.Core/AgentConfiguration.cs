using System.Text;
using System.Text.Json;

namespace MiIALocal.Core;

public static class AgentConfiguration
{
    public static string ProjectUrl(int port, string project)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(project))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"http://127.0.0.1:{port}/{encoded}/session";
    }
    public static string Build(int modelPort, string apiKey, string instructionFile, int contextTokens = ReleaseProfile.Context, EngineTuning? tuning = null, string modelName = "Qwen 27B · Forja 50")
    {
        tuning ??= new();
        var model = new { name = modelName, tool_call = true, limit = new { context = contextTokens, output = tuning.MaxOutput } };
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["model"] = "local/qwen-local", ["small_model"] = "local/qwen-local", ["enabled_providers"] = new[] { "local" },
            ["autoupdate"] = false, ["share"] = "disabled",
            ["shell"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["instructions"] = new[] { instructionFile },
            ["provider"] = new { local = new { npm = "@ai-sdk/openai-compatible", name = "Forja 50", options = new { baseURL = $"http://127.0.0.1:{modelPort}/v1", apiKey, timeout = 180000, chunkTimeout = 90000 }, models = new Dictionary<string, object> { ["qwen-local"] = model } } },
            ["permission"] = new { edit = "allow", external_directory = "ask", doom_loop = "ask", task = "deny", bash = new Dictionary<string, string> { ["*"] = "allow", ["git push*"] = "ask", ["git reset --hard*"] = "ask", ["git clean*"] = "ask", ["git branch -D*"] = "ask", ["Remove-Item*"] = "ask", ["rm *"] = "ask", ["del *"] = "ask", ["rmdir *"] = "ask" } },
            ["compaction"] = new { auto = true, prune = true }, ["agent"] = new { build = new { temperature = tuning.AgentTemperature } }
        });
    }
    public const string Instructions = """
        Respondé en español claro. Antes de modificar el proyecto leé sus instrucciones AGENTS.md y las referencias que indiquen.
        Inspeccioná git status y conservá los cambios previos del usuario. Usá herramientas reales para leer, editar y ejecutar pruebas.
        Trabajás en Windows con PowerShell, rutas de Windows y Git local. Revisá el diff y no afirmes haber probado algo si no lo ejecutaste.
        No incluyas la palabra codex en nombres de ramas. No hagas push ni publiques sin instrucción explícita del usuario.
        Antes de un commit comprobá que incluya solo los archivos de la tarea. No borres cambios ajenos ni crees subagentes.
        No leas credenciales ajenas a la tarea. Mantené las tareas acotadas y evitá volcar archivos enormes al contexto.
        """;
}
