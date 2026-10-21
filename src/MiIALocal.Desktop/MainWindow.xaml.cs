using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using MiIALocal.Core;

namespace MiIALocal.Desktop;

public partial class MainWindow : Window
{
    private readonly AppPaths paths;
    private readonly AppSettings settings;
    private readonly HttpClient http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Provisioner provisioner;
    private readonly RuntimeSupervisor runtime;
    private readonly DispatcherTimer healthTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private HardwareReport? hardware;
    private CancellationTokenSource? operation;
    private TaskCompletionSource? operationFinished;
    private string? pendingInstaller;
    private bool preparing, preparationPaused;
    private bool busy, closing, exiting, browserReady, synchronizingProfile = true;
    public MainWindow(AppPaths paths)
    {
        InitializeComponent(); NativeWindowTheme.Attach(this); this.paths = paths; settings = paths.LoadSettings();
        _ = ContextProfile.For(settings.ContextTokens);
        ContextChoice.SelectedIndex = settings.ContextTokens == 65536 ? 1 : 0;
        provisioner = new(paths, new VerifiedDownloader(http)); runtime = new(paths, http);
        LicenseCheck.IsChecked = settings.LicenseAccepted;
        runtime.StateChanged += (_, message) => Dispatcher.Invoke(() => { StatusLabel.Text = message; UpdateConfigurationSummary(); });
        UpdateProject(); ModelLocation.Text = string.IsNullOrWhiteSpace(settings.ImportedModel) ? paths.ModelDirectoryPath(settings) : paths.ModelPath(settings);
        Loaded += async (_, _) =>
        {
            await InspectHardwareAsync();
            if (App.Argument("--smoke-close") is { } closeResult)
            {
                var pauseDownload = App.Arguments.Contains("--pause-download");
                var duringOperation = App.Arguments.Contains("--close-during-operation") || pauseDownload;
                var rejectContext = App.Arguments.Contains("--reject-invalid-context");
                var cancellationObserved = false;
                if (rejectContext)
                {
                    settings.UseRecommendedProfile = false; settings.ContextTokens = 65536; settings.Tuning.HistoryTokens = 32768;
                    ContextChoice.SelectedIndex = 1; ContextChoice.SelectedIndex = 0;
                }
                Closed += (_, _) =>
                {
                    if (!File.Exists(closeResult)) AppPaths.AtomicWrite(closeResult, System.Text.Json.JsonSerializer.Serialize(new { status = duringOperation && !cancellationObserved || rejectContext && (settings.ContextTokens != 65536 || ContextChoice.SelectedIndex != 1) ? "FAIL" : "PASS", scenario = rejectContext ? "invalid-context-rejected" : duringOperation ? "cancel-operation-and-close" : App.Arguments.Contains("--repeat-close") ? "repeated-close" : "first-close", date = DateTimeOffset.Now }, AppPaths.JsonOptions));
                };
                if (pauseDownload) preparing = true;
                if (duringOperation) _ = RunOperationAsync(async ct =>
                {
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException) { cancellationObserved = true; throw; }
                });
                _ = Dispatcher.BeginInvoke(new Action(async () =>
                {
                    if (pauseDownload)
                    {
                        var hadPauseButton = Equals(CancelButton.Content, "Pausar");
                        CancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        await operationFinished!.Task; preparing = false; RefreshControls();
                        if (!hadPauseButton || !preparationPaused || !Equals(PrepareButton.Content, "Reanudar preparación"))
                            AppPaths.AtomicWrite(closeResult, "{\"status\":\"FAIL\",\"error\":\"Controles de pausa incorrectos\"}");
                    }
                    if (App.Arguments.Contains("--repeat-close")) { Close(); Close(); }
                    else SystemCommands.CloseWindow(this);
                }));
                return;
            }
            if (App.Argument("--preview-advanced") is { } advancedImage)
            {
                var dialog = new AdvancedSettingsWindow(settings) { Owner = this }; dialog.Show(); await Task.Delay(300); dialog.UpdateLayout();
                var visual = (FrameworkElement)dialog.Content;
                var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(visual), null, new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
                bitmap.Render(drawing); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(advancedImage))!); using (var output = File.Create(advancedImage)) png.Save(output);
                dialog.Close(); exiting = true; Close(); return;
            }
            if (App.Argument("--smoke-chat") is { } smokeFolder)
            {
                await SmokeChatAsync(smokeFolder);
                return;
            }
            if (App.Argument("--preview") is { } image)
            {
                await Task.Delay(500); UpdateLayout();
                var visual = (FrameworkElement)Content;
                var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(image))!); using (var output = File.Create(image)) png.Save(output);
                exiting = true; Close();
            }
        };
        healthTimer.Tick += async (_, _) =>
        {
            if (!busy && !closing && runtime.State == RuntimeState.Ready && !runtime.ProcessesHealthy)
            {
                Chat.Visibility = Visibility.Collapsed; WelcomePane.Visibility = Visibility.Visible;
                await runtime.StopAsync(); StatusLabel.Text = "Un componente de la IA se detuvo. Podés revisar los registros y volver a iniciar."; RefreshControls();
            }
        };
        healthTimer.Start(); Closing += Window_Closing;
        UpdateConfigurationSummary(); synchronizingProfile = false;
    }
    private async Task SmokeChatAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        try
        {
            if (hardware?.Problem is { } problem) throw new InvalidOperationException(problem);
            var gpu = hardware?.SelectedGpu ?? throw new InvalidOperationException("Falta GPU compatible.");
            if (gpu.FreeMiB < 13000) throw new InvalidOperationException("Falta liberar la GPU antes de la prueba de interfaz.");
            var url = await runtime.StartAsync(settings, gpu, settings.LastProject, CancellationToken.None);
            await OpenChatAsync(url);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            string body = "";
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1000);
                try { body = System.Text.Json.JsonSerializer.Deserialize<string>(await Chat.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText : ''").WaitAsync(TimeSpan.FromSeconds(5))) ?? ""; }
                catch (TimeoutException) { continue; }
                if (body.Contains("Qwen 27B") && (body.Contains("Nueva sesión") || body.Contains("New session") || body.Contains("Pregunta lo que quieras"))) break;
            }
            await File.WriteAllTextAsync(Path.Combine(folder, "chat-visible.txt"), body);
            await File.WriteAllTextAsync(Path.Combine(folder, "chat-location.txt"), Chat.Source?.AbsoluteUri ?? "sin dirección");
            await using (var capture = File.Create(Path.Combine(folder, "chat.png"))) await Chat.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture).WaitAsync(TimeSpan.FromSeconds(10));
            if (!body.Contains("Qwen 27B") || body.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("El chat no mostró el modelo local autenticado.");
            AppPaths.AtomicWrite(Path.Combine(folder, "resultado.json"), System.Text.Json.JsonSerializer.Serialize(new { status = "PASS", gpu = gpu.Name, context = settings.ContextTokens, bodyCharacters = body.Length, date = DateTimeOffset.Now }, AppPaths.JsonOptions));
        }
        catch (Exception ex)
        {
            LogError(ex); Environment.ExitCode = 1;
            AppPaths.AtomicWrite(Path.Combine(folder, "resultado.json"), System.Text.Json.JsonSerializer.Serialize(new { status = "FAIL", error = ex.Message }, AppPaths.JsonOptions));
        }
        finally { await runtime.StopAsync(); await runtime.DisposeAsync(); healthTimer.Stop(); Chat.Dispose(); http.Dispose(); exiting = true; Close(); }
    }
    private async Task InspectHardwareAsync()
    {
        try
        {
            hardware = await HardwareDetector.DetectAsync(CancellationToken.None);
            ApplyHardwareProfile();
            GpuLabel.Text = hardware.SelectedGpu?.Profile ?? hardware.Gpus.FirstOrDefault()?.Name ?? "No se detectó una placa NVIDIA";
            RamLabel.Text = $"{hardware.RamGiB:0.0} GiB de RAM · Windows {Environment.OSVersion.Version.Build}";
            StatusLabel.Text = hardware.Problem ?? (provisioner.IsPrepared(settings) ? "Tu IA está preparada. Elegí un proyecto e iniciá." : "Equipo compatible con el perfil. Prepará la IA para empezar.");
            DetailLabel.Text = hardware.Problem is null ? $"RTX 5070 Ti y RTX 5080 de escritorio · contexto {settings.ContextTokens / 1024}K" : "La descarga permanece deshabilitada en equipos fuera del alcance.";
            if (paths.SettingsRecoveryMessage is { } recovery) DetailLabel.Text = recovery;
        }
        catch (Exception ex) { StatusLabel.Text = ex.Message; GpuLabel.Text = "No se pudo comprobar la placa"; LogError(ex); }
        RefreshControls();
    }
    private void RefreshControls()
    {
        var compatible = hardware?.Problem is null && hardware?.SelectedGpu is not null && Environment.OSVersion.Version.Build >= 22000;
        var available = !busy && !closing;
        StartButton.IsEnabled = available && compatible && provisioner.IsPrepared(settings) && Directory.Exists(settings.LastProject);
        StartButton.Content = runtime.State == RuntimeState.Ready ? "Abrir proyecto" : "Iniciar IA";
        StopButton.IsEnabled = available && runtime.State != RuntimeState.Stopped;
        PrepareButton.IsEnabled = available && compatible && runtime.State != RuntimeState.Ready;
        PrepareButton.Content = preparationPaused || File.Exists(paths.ModelPath(settings) + ".part") ? "Reanudar preparación" : "Preparar IA";
        ProjectButton.IsEnabled = available;
        ContextChoice.IsEnabled = available && runtime.State is RuntimeState.Stopped or RuntimeState.Error;
        AdvancedButton.IsEnabled = ContextChoice.IsEnabled;
        UpdateButton.IsEnabled = available;
        ModelFolderButton.IsEnabled = ImportButton.IsEnabled = ContextChoice.IsEnabled;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Content = preparing ? "Pausar" : "Cancelar";
    }
    private void ContextChoice_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings is null || paths is null) return; // XAML may raise this during InitializeComponent.
        var requestedContext = ContextChoice.SelectedIndex == 1 ? 65536 : 32768;
        try { settings.Tuning.Validate(requestedContext); }
        catch (InvalidDataException ex)
        {
            ContextChoice.SelectedIndex = settings.ContextTokens == 65536 ? 1 : 0;
            DetailLabel.Text = ex.Message + " Revisá Configuración avanzada antes de cambiar de contexto.";
            return;
        }
        settings.ContextTokens = requestedContext;
        if (!synchronizingProfile) settings.UseRecommendedProfile = false;
        paths.SaveSettings(settings);
        DetailLabel.Text = settings.ContextTokens == 65536 ? "64K: pruebas básicas aprobadas en RTX 5080; uso prolongado y 5070 Ti pendientes de validar." : "32K: perfil de referencia. El historial se resume cuando se acerca al límite.";
        UpdateConfigurationSummary();
    }
    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        if (busy || closing || runtime.State is not (RuntimeState.Stopped or RuntimeState.Error)) return;
        var dialog = new AdvancedSettingsWindow(settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        settings.Tuning = dialog.Result; settings.ContextTokens = dialog.ContextTokens;
        settings.UseRecommendedProfile = hardware?.SelectedGpu is { } gpu && GpuProfiles.For(gpu).Matches(settings.ContextTokens, settings.Tuning);
        synchronizingProfile = true;
        ContextChoice.SelectedIndex = settings.ContextTokens == 65536 ? 1 : 0;
        synchronizingProfile = false;
        paths.SaveSettings(settings); UpdateConfigurationSummary();
        StatusLabel.Text = "Configuración guardada. Se aplicará al iniciar la IA.";
    }
    private void UpdateConfigurationSummary()
    {
        if (settings is null || ConfigurationSummary is null) return;
        var t = settings.Tuning;
        ConfigurationSummary.Text = $"Perfil: {hardware?.SelectedGpu?.Profile ?? "por detectar"}\nAjustes: {(settings.UseRecommendedProfile ? "automáticos" : "personalizados")}\nQwen 27B · Swift 1.5 · IQ3_S\nNInfer / KVMem 0.18.0\nOpenCode 1.19.3\nGit 2.53.0.3 · Node 24.19.0\nContexto: {settings.ContextTokens:N0} tokens\nHistorial GPU: {t.HistoryTokens:N0}\nReserva GPU: {t.GenerationReserve:N0}\nCaché RAM: {t.EffectiveHostMiB(settings.ContextTokens):N0} MiB\nKV: {t.KvType} · GDN: FP16\nInicio / reciente: {t.SinkTokens} / {t.RecentTokens}\nPrefill: {t.PrefillChunk} · MTP: {t.DraftTokens} borradores\nNgram: {t.NgramDraftTokens} · mínimo {t.NgramMinMatch}\nMTP adaptativo: desactivado\nVentana MTP: historial completo\nRazonamiento: {t.ThinkingTokens:N0} tokens\nSalida máxima: {t.MaxOutput:N0} tokens\nTemperatura motor / agente: {t.Temperature} / {t.AgentTemperature}\nTop P: {t.TopP} · Top K: {t.TopK}\nMin P: {t.MinP}\n1 sesión · 1 petición a la vez\nVisión / caché SSD: desactivadas\nPerfil automático: desactivado\nRed: solo 127.0.0.1\nHistorial: resumen automático";
    }
    private void ApplyHardwareProfile()
    {
        if (hardware?.SelectedGpu is not { } gpu) return;
        _ = GpuProfiles.Apply(settings, gpu);
        synchronizingProfile = true;
        ContextChoice.SelectedIndex = settings.ContextTokens == 65536 ? 1 : 0;
        synchronizingProfile = false;
        paths.SaveSettings(settings); UpdateConfigurationSummary();
    }
    private async Task RunOperationAsync(Func<CancellationToken, Task> action)
    {
        if (busy || closing) return; busy = true; operation = new(); operationFinished = new(TaskCreationOptions.RunContinuationsAsynchronously); Progress.Visibility = Visibility.Visible; Progress.IsIndeterminate = true; RefreshControls();
        try { await action(operation.Token); }
        catch (OperationCanceledException) { StatusLabel.Text = preparationPaused ? "Preparación pausada. Pulsá Reanudar preparación para continuar." : "Acción cancelada. La descarga parcial se conserva para reanudar."; }
        catch (Exception ex) { LogError(ex); StatusLabel.Text = ex.Message; DetailLabel.Text = "Podés revisar Diagnóstico y reintentar. No se muestra un error de una operación anterior."; }
        finally { operation.Dispose(); operation = null; busy = false; Progress.Visibility = Visibility.Collapsed; RefreshControls(); operationFinished.TrySetResult(); }
    }
    private void UpdateProject()
    {
        ProjectName.Text = string.IsNullOrWhiteSpace(settings.LastProject) ? "Elegí dónde trabajar" : Path.GetFileName(settings.LastProject.TrimEnd(Path.DirectorySeparatorChar));
        ProjectPathLabel.Text = string.IsNullOrWhiteSpace(settings.LastProject) ? "Tus archivos permanecen en su carpeta original." : settings.LastProject;
    }
    private void ChooseProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Elegí la carpeta de tu repositorio", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        settings.LastProject = dialog.FolderName; paths.SaveSettings(settings); UpdateProject(); RefreshControls();
        if (runtime.State == RuntimeState.Ready) DetailLabel.Text = "Pulsá Abrir proyecto para cambiar el chat al proyecto seleccionado.";
    }
    private void ChooseModelDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Dónde querés guardar el modelo", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        settings.ModelDirectory = dialog.FolderName; settings.ImportedModel = ""; paths.SaveSettings(settings); ModelLocation.Text = settings.ModelDirectory; RefreshControls();
    }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Seleccioná el modelo Qwen de esta versión", Filter = "Modelo NInfer (*.ninfer)|*.ninfer" };
        if (dialog.ShowDialog(this) != true) return;
        await RunOperationAsync(async ct =>
        {
            StatusLabel.Text = "Verificando el modelo elegido...";
            if (!await VerifiedDownloader.MatchesAsync(dialog.FileName, ReleaseProfile.Model, ct)) throw new InvalidDataException("Ese archivo no coincide con el modelo de esta versión.");
            settings.ImportedModel = dialog.FileName; paths.SaveSettings(settings); ModelLocation.Text = dialog.FileName;
            StatusLabel.Text = "Modelo verificado. Preparar IA descargará solo los componentes que falten.";
        });
    }
    private async void Prepare_Click(object sender, RoutedEventArgs e)
    {
        if (busy || closing) return;
        if (LicenseCheck.IsChecked != true) { StatusLabel.Text = "Revisá las licencias y confirmá que las aceptás antes de preparar la IA."; return; }
        preparing = true; preparationPaused = false;
        try { await RunOperationAsync(async ct =>
        {
            hardware = await HardwareDetector.DetectAsync(ct); if (hardware.Problem is { } problem) throw new InvalidOperationException(problem);
            ApplyHardwareProfile();
            PreparationStorage.Check(paths, settings);
            settings.LicenseAccepted = true; paths.SaveSettings(settings);
            var progress = new Progress<PreparationProgress>(p =>
            {
                if (ct.IsCancellationRequested || closing) return;
                StatusLabel.Text = p.Stage; Progress.IsIndeterminate = p.Percentage is null; if (p.Percentage is { } value) Progress.Value = value;
                DetailLabel.Text = p.TransferDetails;
            });
            await provisioner.PrepareAsync(settings, progress, ct); StatusLabel.Text = "IA preparada. Elegí tu proyecto y pulsá Iniciar IA.";
        }); }
        finally { preparing = false; RefreshControls(); }
    }
    private async void Start_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async ct =>
    {
        hardware = await HardwareDetector.DetectAsync(ct); if (hardware.Problem is { } problem) throw new InvalidOperationException(problem);
        ApplyHardwareProfile();
        var gpu = hardware.SelectedGpu!;
        if (runtime.State != RuntimeState.Ready && gpu.FreeMiB < 13000) throw new InvalidOperationException("Hay otra aplicación usando mucha memoria de la GPU. Detené la otra IA o cerrá el juego y reintentá; no se cerrará ningún programa ajeno.");
        var url = await runtime.StartAsync(settings, gpu, settings.LastProject, ct);
        await OpenChatAsync(url); DetailLabel.Text = "Proyecto: " + settings.LastProject;
    });
    private async Task OpenChatAsync(string url)
    {
        try
        {
            if (!browserReady)
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(paths.Root, "browser"));
                await Chat.EnsureCoreWebView2Async(environment);
                await Chat.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("if (location.hostname === '127.0.0.1') { localStorage.setItem('opencode-color-scheme', 'dark'); }");
                Chat.CoreWebView2.BasicAuthenticationRequested += (_, e) =>
                {
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Host == "127.0.0.1" && uri.Port == runtime.AgentPort)
                    { e.Response.UserName = "opencode"; e.Response.Password = runtime.AgentPassword; }
                    else e.Cancel = true;
                };
                Chat.CoreWebView2.NavigationStarting += (_, e) =>
                {
                    File.AppendAllText(Path.Combine(paths.Logs, "browser-navigation.log"), DateTimeOffset.Now + " " + e.Uri + Environment.NewLine);
                    if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Host != "127.0.0.1" || uri.Port != runtime.AgentPort) e.Cancel = true;
                };
                Chat.CoreWebView2.NavigationCompleted += (_, e) => File.AppendAllText(Path.Combine(paths.Logs, "browser-navigation.log"), $"Completed success={e.IsSuccess} status={e.HttpStatusCode} error={e.WebErrorStatus}" + Environment.NewLine);
                Chat.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https") OpenExternal(uri.AbsoluteUri); };
                Chat.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                Chat.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                browserReady = true;
            }
            Chat.CoreWebView2.Navigate(url); WelcomePane.Visibility = Visibility.Collapsed; Chat.Visibility = Visibility.Visible;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            await runtime.StopAsync(); throw new InvalidOperationException("Falta Microsoft Edge WebView2 Runtime. Esta versión de desarrollo requiere instalarlo desde Microsoft; el instalador final lo preparará automáticamente.");
        }
    }
    private async void Stop_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async _ => { await runtime.StopAsync(); Chat.Visibility = Visibility.Collapsed; WelcomePane.Visibility = Visibility.Visible; });
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (preparing) { preparationPaused = true; StatusLabel.Text = "Pausando y guardando el progreso..."; }
        operation?.Cancel();
    }
    private void Logs_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = '"' + paths.Logs + '"' });
    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync(async ct =>
        {
            StatusLabel.Text = "Buscando actualizaciones...";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var portable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
            var update = await AppUpdates.CheckAsync(http, ReleaseProfile.Version, portable, timeout.Token);
            if (update is null) { StatusLabel.Text = "Ya tenés la última versión disponible: " + ReleaseProfile.Version; return; }
            if (MessageBox.Show(this, $"Está disponible {update.Version}. ¿Descargarla?\n\nLa descarga se verifica con SHA-256. Tus modelos, chats y ajustes se conservan.\n" + (portable ? "La edición portable se reemplaza manualmente." : "Al terminar se detendrá la IA y se abrirá el instalador."), "Actualizar Forja 50", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var destination = Path.Combine(paths.Root, "updates", update.Asset.FileName);
            await new VerifiedDownloader(http).DownloadAsync(update.Asset, destination, new Progress<PreparationProgress>(p =>
            {
                if (ct.IsCancellationRequested || closing) return;
                StatusLabel.Text = p.Stage; Progress.IsIndeterminate = p.Percentage is null;
                DetailLabel.Text = p.TransferDetails;
                if (p.Percentage is { } value) Progress.Value = value;
            }), ct);
            ct.ThrowIfCancellationRequested();
            if (portable)
            {
                StatusLabel.Text = "Portable descargado y verificado.";
                DetailLabel.Text = "Cerrá la aplicación, extraé el ZIP en una carpeta nueva y copiá la carpeta datos de tu portable anterior. No borres los datos anteriores hasta comprobar que funciona.";
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = "/select,\"" + destination + "\"" });
            }
            else
            {
                await runtime.StopAsync();
                Chat.Visibility = Visibility.Collapsed; WelcomePane.Visibility = Visibility.Visible;
                pendingInstaller = destination;
            }
        });
        if (pendingInstaller is not null && !closing) Close();
    }
    private void Versions_Click(object sender, RoutedEventArgs e) => OpenExternal(AppUpdates.ReleasesUrl);
    private void Licenses_Click(object sender, RoutedEventArgs e) => OpenExternal("https://huggingface.co/fyb1214/Swift-1.5-Qwen3.8-27B-GSQ-RCO-NInfer/tree/7da3e38b9ee7a6c8888cf2112d3c5a48a952a2a8");
    private static void OpenExternal(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void LogError(Exception error) => File.AppendAllText(Path.Combine(paths.Logs, "application-errors.log"), DateTimeOffset.Now + " " + error + Environment.NewLine);
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (exiting) return; e.Cancel = true;
        if (closing) return;
        closing = true; RefreshControls();
        // Leave WPF's native Closing callback before dialogs, disposal or a second Close.
        _ = Dispatcher.BeginInvoke(new Action(async () => await FinishClosingAsync()));
    }
    private async Task FinishClosingAsync()
    {
        if (App.Argument("--smoke-close") is null && (busy || runtime.State is RuntimeState.Ready or RuntimeState.Loading) && MessageBox.Show(this, "¿Detener la IA y salir? Tus chats y archivos se conservan.", "Forja 50", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        { closing = false; RefreshControls(); return; }
        operation?.Cancel(); healthTimer.Stop();
        try
        {
            if (operationFinished is { } pending) await pending.Task;
            await runtime.DisposeAsync();
        }
        catch (Exception ex)
        {
            LogError(ex); closing = false; healthTimer.Start(); RefreshControls();
            StatusLabel.Text = "No se pudo terminar de detener la IA. Revisá Diagnóstico y volvé a intentar cerrar.";
            return;
        }
        try { Chat.Dispose(); } catch (Exception ex) { LogError(ex); }
        if (pendingInstaller is { } installer)
        {
            try
            {
                var start = new ProcessStartInfo(installer) { UseShellExecute = true };
                start.ArgumentList.Add("/DIR=" + AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
                Process.Start(start);
            }
            catch (Exception ex)
            {
                LogError(ex);
                MessageBox.Show(this, "La actualización está verificada pero no se pudo abrir el instalador. Podés ejecutarlo manualmente:\n" + installer, "Forja 50");
            }
        }
        http.Dispose(); exiting = true; Close();
    }
}
