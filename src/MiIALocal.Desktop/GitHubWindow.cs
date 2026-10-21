using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiIALocal.Core;

namespace MiIALocal.Desktop;

public sealed class GitHubWindow : Window
{
    private readonly GitHubConnection connection;
    private readonly Provisioner provisioner;
    private readonly AppSettings settings;
    private readonly TextBlock account = new() { FontSize = 20, Margin = new(0, 20, 0, 10) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 18) };
    private readonly Button connect = new() { Content = "Conectar con GitHub" };
    private readonly Button disconnect = new() { Content = "Cerrar sesión" };
    private readonly Button refresh = new() { Content = "Actualizar estado" };
    private readonly Button cancel = new() { Content = "Cancelar", Visibility = Visibility.Collapsed };
    private readonly ProgressBar progress = new() { Height = 4, Margin = new(0, 8, 0, 8), Visibility = Visibility.Collapsed };
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private Task? pending;
    private bool working, closing, hasSession;
    public Task Ready { get; private set; } = Task.CompletedTask;
    public bool IsSignedIn => hasSession;
    public bool CanConnect => connect.IsEnabled;
    public string AccountText => account.Text;
    public GitHubWindow(AppPaths paths, AppSettings settings, HttpClient http, GitHubConnection? connection = null)
    {
        this.settings = settings; this.connection = connection ?? new(paths);
        provisioner = new(paths, new VerifiedDownloader(http));
        NativeWindowTheme.Attach(this);
        Title = "Forja 50 · GitHub"; Width = 660; Height = 500; MinWidth = 540; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Color("#101820"); Foreground = Color("#E2ECF1"); FontFamily = new("Segoe UI");
        var panel = new StackPanel { Margin = new(28) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Tu cuenta de GitHub", FontSize = 26, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Opcional para trabajar localmente. Conectá tu cuenta para usar repositorios privados o subir cambios por HTTPS.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) });
        panel.Children.Add(account); panel.Children.Add(status); panel.Children.Add(progress);
        var actions = new WrapPanel(); foreach (var button in new[] { connect, disconnect, refresh, cancel }) actions.Children.Add(button); panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = "La autorización se completa en tu navegador. Forja no pide tu contraseña. Las credenciales se guardan cifradas por Windows, separadas de otras aplicaciones.", TextWrapping = TextWrapping.Wrap, Foreground = Color("#9FB1BD"), Margin = new(0, 24, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Conectar no sube archivos. El agente mantiene la solicitud de permiso para git push. Si tu repositorio usa SSH, conserva su autenticación SSH.", TextWrapping = TextWrapping.Wrap, Foreground = Color("#9FB1BD"), Margin = new(0, 12, 0, 0) });
        connect.Click += async (_, _) => await RunAsync(async ct =>
        {
            status.Text = "Preparando Git y el gestor de sesión. No se descargarán modelos de IA.";
            await provisioner.PrepareGitHubAsync(new Progress<PreparationProgress>(p => { status.Text = p.Stage; progress.IsIndeterminate = p.Percentage is null; progress.Value = p.Percentage ?? 0; }), ct);
            if ((await this.connection.AccountsAsync(ct)).Count == 0)
            {
                status.Text = "Completá la autorización en el navegador. Podés cancelar desde esta ventana.";
                await this.connection.LoginAsync(ct);
            }
            await RefreshAsync(ct);
        });
        disconnect.Click += async (_, _) => await RunAsync(async ct => { this.connection.Disconnect(settings); await RefreshAsync(ct); });
        refresh.Click += async (_, _) => await RunAsync(RefreshAsync);
        cancel.Click += (_, _) => operation?.Cancel();
        Loaded += (_, _) => Ready = RunAsync(RefreshAsync);
        Closing += ClosingAsync;
        Closed += (_, _) => lifetime.Dispose();
    }
    private static SolidColorBrush Color(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    private async Task RefreshAsync(CancellationToken ct)
    {
        var accounts = await connection.AccountsAsync(ct);
        hasSession = accounts.Count != 0;
        account.Text = hasSession ? "Cuenta guardada: " + string.Join(", ", accounts) : "Sin cuenta conectada";
        status.Text = hasSession ? "Sesión local guardada. GitHub verificará los permisos al acceder a cada repositorio. Para cambiar de cuenta, cerrá esta sesión primero." : "Al conectar se descargarán Git y su gestor de sesión si faltan. No hace falta preparar ni iniciar la IA.";
    }
    private Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (working || closing) return Task.CompletedTask;
        return pending = ExecuteAsync(action);
    }
    private async Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        working = true; operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        UpdateControls();
        try { await action(operation.Token); }
        catch (OperationCanceledException) { status.Text = "Operación cancelada. Podés volver a intentarlo."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException or Win32Exception)
        { status.Text = ex is Win32Exception ? "No se pudo abrir el gestor de GitHub. Volvé a preparar sus herramientas." : ex.Message; }
        finally { working = false; operation.Dispose(); operation = null; UpdateControls(); }
    }
    private void UpdateControls()
    {
        connect.IsEnabled = !working && (!hasSession || !connection.ToolsReady);
        connect.Content = hasSession && !connection.ToolsReady ? "Restaurar conexión de GitHub" : "Conectar con GitHub";
        disconnect.IsEnabled = !working && hasSession; refresh.IsEnabled = !working;
        cancel.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        progress.Visibility = working ? Visibility.Visible : Visibility.Collapsed; progress.IsIndeterminate = working;
    }
    private async void ClosingAsync(object? sender, CancelEventArgs e)
    {
        if (!working) return;
        e.Cancel = true; if (closing) return;
        closing = true; lifetime.Cancel();
        if (pending is not null) await pending;
        Close();
    }
}
