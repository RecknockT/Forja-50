using System.Windows;
using MiIALocal.Core;

namespace MiIALocal.Desktop;
public partial class App : Application
{
    private Mutex? instance;
    public static string[] Arguments { get; private set; } = [];
    public static string? Argument(string name)
    {
        var index = Array.IndexOf(Arguments, name);
        return index >= 0 && index + 1 < Arguments.Length ? Arguments[index + 1] : null;
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); Arguments = e.Args;
        var portableRoot = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag")) ? Path.Combine(AppContext.BaseDirectory, "datos") : null;
        var data = new AppPaths(Argument("--data-root") ?? portableRoot);
        var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(data.Root))).Substring(0, 20);
        instance = new Mutex(true, "Local\\MiIALocal-" + identity, out var first);
        if (!first) { MessageBox.Show("Mi IA Local ya está abierta. Buscá su ventana en la barra de tareas.", "Mi IA Local"); Shutdown(); return; }
        DispatcherUnhandledException += (_, error) =>
        {
            File.AppendAllText(Path.Combine(data.Logs, "application-errors.log"), DateTimeOffset.Now + " " + error.Exception + Environment.NewLine);
            error.Handled = true;
            MessageBox.Show("No se pudo completar la acción. Podés revisar el detalle en Diagnóstico.\n\n" + error.Exception.Message, "Mi IA Local");
        };
        MainWindow = new MainWindow(data); MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
