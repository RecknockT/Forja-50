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
    private AppSettings settings;
    private readonly HttpClient http = new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None, MaxConnectionsPerServer = 32, ConnectTimeout = TimeSpan.FromSeconds(15) }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Provisioner provisioner;
    private readonly RuntimeSupervisor runtime;
    private readonly VisualPane visualPane;
    private bool switchingWorkspace;
    private readonly DispatcherTimer healthTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer memoryTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly CancellationTokenSource memoryLifetime = new();
    private bool readingMemory;
    private HardwareReport? hardware;
    private CancellationTokenSource? operation;
    private TaskCompletionSource? operationFinished;
    private string? pendingInstaller;
    private Func<bool>? closeConfirmationForTest;
    private bool preparing, preparationPaused;
    private bool busy, closing, exiting, browserReady, synchronizingProfile = true, populatingContext, populatingMode;
    public MainWindow(AppPaths paths)
    {
        InitializeComponent(); System.Windows.Controls.ToolTipService.SetShowOnDisabled(StartButton, true); NativeWindowTheme.Attach(this); this.paths = paths; settings = paths.LoadSettings();
        provisioner = new(paths, new VerifiedDownloader(http)); runtime = new(paths, http);
        visualPane = new(paths, () => settings, http, () => hardware?.SelectedGpu, RunOperationAsync,
            message => StatusLabel.Text = message, () => UpdateConfigurationSummary(), p =>
            { if (closing) return; StatusLabel.Text = p.Stage.Split(" · hasta", StringSplitOptions.None)[0]; Progress.IsIndeterminate = p.Percentage is null; if (p.Percentage is { } percent) Progress.Value = percent; DetailLabel.Text = p.Stage.StartsWith("Generando") ? "Tu creación se está procesando en este equipo." : p.TransferDetails; });
        VisualContainer.Children.Add(visualPane);
        LicenseCheck.IsChecked = settings.LicenseAccepted;
        runtime.StateChanged += (_, message) => Dispatcher.Invoke(() => { StatusLabel.Text = message; UpdateConfigurationSummary(); });
        UpdateProject(); ApplyModeToUi();
        ApplyWorkspaceToUi();
        Loaded += async (_, _) =>
        {
            if (App.Argument("--smoke-visual") is { } visualReport && App.Argument("--data-root") is not null)
            { SmokeVisualModes(visualReport); exiting = true; Close(); return; }
            if (App.Argument("--smoke-cleanup-all") is { } cleanupReport && App.Argument("--data-root") is not null)
            {
                await SmokeCleanupAllAsync(cleanupReport);
                exiting = true; Close(); return;
            }
            if (App.Argument("--smoke-github") is { } githubReport && App.Argument("--data-root") is not null)
            {
                await SmokeGitHubAsync(githubReport);
                exiting = true; Close(); return;
            }
            if (App.Argument("--smoke-gpu-profiles") is { } profileReport && App.Argument("--data-root") is not null)
            {
                SmokeGpuProfiles(profileReport);
                exiting = true; Close(); return;
            }
            await InspectHardwareAsync();
            if (settings.Mode == AppMode.Visual) ApplyWorkspaceToUi();
            if (App.Arguments.Contains("--preview-visual")) { settings.Mode = AppMode.Visual; ApplyWorkspaceToUi(); RefreshControls(); }
            if (App.Argument("--smoke-browser-cleanup") is { } browserReport && App.Argument("--data-root") is not null)
            {
                try
                {
                    var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(paths.Root, "browser"));
                    await Chat.EnsureCoreWebView2Async(environment); browserReady = true;
                    await ReleaseBrowserForCleanupAsync();
                    new StorageCleanup(paths, settings).Delete([StorageCategory.Conversations]);
                    if (Directory.Exists(Path.Combine(paths.Root, "browser"))) throw new IOException("El caché del navegador sigue presente.");
                    AppPaths.AtomicWrite(browserReport, "{\"status\":\"PASS\",\"browserClosed\":true,\"cacheDeleted\":true,\"modelStarted\":false}");
                    Close();
                }
                catch (Exception error) { AppPaths.AtomicWrite(browserReport, System.Text.Json.JsonSerializer.Serialize(new { status = "FAIL", error = error.Message })); Close(); }
                return;
            }
            if (App.Argument("--smoke-close") is { } closeResult)
            {
                var pauseDownload = App.Arguments.Contains("--pause-download");
                var declineClose = App.Arguments.Contains("--decline-close");
                var declineObserved = false;
                var confirmationCalls = 0;
                closeConfirmationForTest = () => !declineClose || ++confirmationCalls > 1;
                var duringOperation = App.Arguments.Contains("--close-during-operation") || pauseDownload;
                var rejectContext = App.Arguments.Contains("--reject-invalid-context");
                var cancellationObserved = false;
                if (rejectContext)
                {
                    settings.EngineMode = EngineMode.KvMem; ApplyModeToUi(); settings.UseRecommendedProfile = false; settings.ContextTokens = 65536; settings.Tuning.HistoryTokens = 32768;
                    ContextChoice.SelectedIndex = 1; ContextChoice.SelectedIndex = 0;
                }
                Closed += (_, _) =>
                {
                    if (!File.Exists(closeResult)) AppPaths.AtomicWrite(closeResult, System.Text.Json.JsonSerializer.Serialize(new { status = declineClose && !declineObserved || duringOperation && !cancellationObserved || rejectContext && (settings.ContextTokens != 65536 || ContextChoice.SelectedIndex != 1) ? "FAIL" : "PASS", scenario = declineClose ? "decline-then-confirm" : rejectContext ? "invalid-context-rejected" : duringOperation ? "cancel-operation-and-close" : App.Arguments.Contains("--repeat-close") ? "repeated-close" : "first-close", date = DateTimeOffset.Now }, AppPaths.JsonOptions));
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
                    if (declineClose)
                    {
                        Close(); await Task.Delay(100);
                        declineObserved = IsVisible && !closing && !exiting && !cancellationObserved && confirmationCalls == 1;
                        Close();
                    }
                    else if (App.Arguments.Contains("--repeat-close")) { Close(); Close(); }
                    else SystemCommands.CloseWindow(this);
                }));
                return;
            }
            if (App.Argument("--preview-advanced") is { } advancedImage)
            {
                Window dialog = settings.EngineMode == EngineMode.NInfer5080 ? new NativeSettingsWindow(settings) : new AdvancedSettingsWindow(settings);
                dialog.Owner = this; dialog.Show(); await Task.Delay(300); dialog.UpdateLayout();
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
            if (!busy && !closing && visualPane.IsActive && !visualPane.IsReady)
            { await visualPane.StopAsync(); StatusLabel.Text = "El motor visual se detuvo. Tus creaciones están guardadas; revisá Diagnóstico antes de iniciar otra vez."; RefreshControls(); }
            if (!busy && !closing && runtime.State == RuntimeState.Ready && !runtime.ProcessesHealthy)
            {
                Chat.Visibility = Visibility.Collapsed; WelcomePane.Visibility = Visibility.Visible;
                await runtime.StopAsync(); StatusLabel.Text = "Un componente de la IA se detuvo. Podés revisar los registros y volver a iniciar."; RefreshControls();
            }
        };
        healthTimer.Start(); Closing += Window_Closing;
        memoryTimer.Tick += async (_, _) => await RefreshMemoryAsync(); memoryTimer.Start();
        Closed += (_, _) => { memoryTimer.Stop(); memoryLifetime.Cancel(); memoryLifetime.Dispose(); };
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
    private async Task SmokeGitHubAsync(string report)
    {
        try
        {
            var fixture = new AppPaths(Path.Combine(paths.Root, "github-fixture"));
            Directory.CreateDirectory(fixture.GitHubCredentialStore);
            foreach (var file in new[] { fixture.GitExe, fixture.GitHubCredentialExe }) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "fixture"); }
            var connection = new GitHubConnection(fixture, (_, _) => Task.FromResult(new GitHubCommandResult(0, "fixture-user\n")));
            var window = new GitHubWindow(fixture, new(), http, connection) { Owner = this };
            window.Show(); await window.Ready;
            if (!window.IsSignedIn || window.CanConnect || !window.AccountText.Contains("fixture-user")) throw new IOException("Estado de cuenta incorrecto.");
            window.Close();
            AppPaths.AtomicWrite(report, "{\"status\":\"PASS\",\"oauthStarted\":false,\"gpuStarted\":false}");
        }
        catch (Exception error) { AppPaths.AtomicWrite(report, System.Text.Json.JsonSerializer.Serialize(new { status = "FAIL", error = error.ToString() })); }
    }
    private async Task SmokeCleanupAllAsync(string report)
    {
        try
        {
            settings.LastProject = Path.Combine(paths.Root, "proyecto-preservado"); Directory.CreateDirectory(settings.LastProject);
            var projectFile = Path.Combine(settings.LastProject, "trabajo.txt"); File.WriteAllText(projectFile, "conservar");
            paths.SaveSettings(settings);
            foreach (var file in new[] { paths.ActiveModelPath(settings), paths.NinferEngineExe(settings), Path.Combine(paths.GitHubCredentialStore, "fixture"), paths.GitHubAccountFile })
            { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "fixture"); }
            if (App.Arguments.Contains("--initialize-browser"))
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(paths.Root, "browser"));
                await Chat.EnsureCoreWebView2Async(environment); browserReady = true;
            }
            await ReleaseBrowserForCleanupAsync();
            new StorageCleanup(paths, settings).Delete(Enum.GetValues<StorageCategory>());
            RefreshAfterStorageCleanup(true);
            if (!IsVisible || closing || !File.Exists(projectFile) || !string.IsNullOrEmpty(settings.LastProject) || LicenseCheck.IsChecked == true || WelcomePane.Visibility != Visibility.Visible)
                throw new IOException("La limpieza cerró la aplicación o perdió el proyecto/estado inicial.");
            if (App.Arguments.Contains("--initialize-browser"))
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(paths.Root, "browser"));
                await Chat.EnsureCoreWebView2Async(environment); browserReady = true;
                await ReleaseBrowserForCleanupAsync();
            }
            AppPaths.AtomicWrite(report, "{\"status\":\"PASS\",\"applicationStayedOpen\":true,\"projectPreserved\":true,\"gpuStarted\":false}");
        }
        catch (Exception error) { AppPaths.AtomicWrite(report, System.Text.Json.JsonSerializer.Serialize(new { status = "FAIL", error = error.ToString() })); }
    }
    private void SmokeGpuProfiles(string report)
    {
        try
        {
            foreach (var (name, memory, prefill) in new[] { ("NVIDIA GeForce RTX 5080", 16303, 896), ("NVIDIA GeForce RTX 5070 Ti", 16303, 512), ("NVIDIA GeForce RTX 3090", 24576, 512) })
            {
                settings.EngineMode = EngineMode.NInfer5080; settings.UseRecommendedProfile = true; settings.ContextTokens = 65536;
                hardware = new([new(0, name, memory, memory, "fixture")], 32);
                ApplyHardwareProfile(); RefreshControls();
                if (hardware.Problem is not null || !ConfigurationSummary.Text.Contains(hardware.SelectedGpu!.Profile)) throw new InvalidDataException("Falta el perfil detectado.");
                if (name.EndsWith("3090"))
                {
                    if (ModeChoice.SelectedIndex != 0 || ((System.Windows.Controls.ComboBoxItem)ModeChoice.Items[1]).IsEnabled || settings.ContextTokens != 131072 || ContextChoice.Items.Count != 3)
                        throw new InvalidDataException("La 3090 no eligió KVMem/128K o permite el motor RTX 50.");
                    ModeChoice.SelectedIndex = 1;
                    if (settings.EngineMode != EngineMode.KvMem || ModeChoice.SelectedIndex != 0) throw new InvalidDataException("Se aceptó un motor incompatible.");
                    foreach (var tokens in new[] { 65536, 32768, 131072 })
                    {
                        ContextChoice.SelectedIndex = Array.IndexOf(ContextOptions, tokens);
                        if (settings.ContextTokens != tokens) throw new InvalidDataException("No se pudo cambiar el contexto de la 3090.");
                        settings.Tuning.Validate(tokens);
                    }
                    if (settings.Tuning.PrefillChunk != prefill || settings.Tuning.HistoryTokens != 98304) throw new InvalidDataException("Se perdió el presupuesto de la 3090.");
                    var advanced = new AdvancedSettingsWindow(settings);
                    if (advanced.ContextTokens != 131072) throw new InvalidDataException("La configuración avanzada perdió 128K.");
                    advanced.Close();
                }
                else if (ModeChoice.SelectedIndex != 1 || settings.ContextTokens != 65536 || !ConfigurationSummary.Text.Contains($"Prefill: {prefill}")) throw new InvalidDataException("Regresión en el perfil RTX 50.");
            }
            AppPaths.AtomicWrite(report, "{\"status\":\"PASS\",\"profiles\":3,\"gpuStarted\":false}");
        }
        catch (Exception error) { AppPaths.AtomicWrite(report, System.Text.Json.JsonSerializer.Serialize(new { status = "FAIL", error = error.ToString() })); }
    }
    private async Task InspectHardwareAsync()
    {
        try
        {
            hardware = await HardwareDetector.DetectAsync(CancellationToken.None);
            ApplyHardwareProfile();
            GpuLabel.Text = hardware.SelectedGpu?.Profile ?? hardware.Gpus.FirstOrDefault()?.Name ?? "No se detectó una placa NVIDIA";
            RamLabel.Text = $"{hardware.RamGiB:0.0} GiB de RAM · Windows {Environment.OSVersion.Version.Build}";
            StatusLabel.Text = hardware.Problem ?? (settings.Mode == AppMode.Visual ? visualPane.IsPrepared ? "Modelos listos. Pulsá Iniciar IA arriba a la derecha." : "Descargá los modelos para empezar a crear." : provisioner.IsPrepared(settings) ? "Tu IA está preparada. Elegí un proyecto e iniciá." : "Equipo compatible con el perfil. Prepará la IA para empezar.");
            DetailLabel.Text = hardware.Problem is null
                ? $"{hardware.SelectedGpu!.Profile} · contexto {settings.ContextTokens / 1024}K · {(hardware.SelectedGpu.IsRtx3090 ? "perfil pendiente de validación física" : "perfil de desarrollo") }"
                : "La descarga permanece deshabilitada en equipos fuera del alcance.";
            if (paths.SettingsRecoveryMessage is { } recovery) DetailLabel.Text = recovery;
            if (settings.EngineMode == EngineMode.NInfer5080 && Ninfer5080Profile.WarningFor(hardware.SelectedGpu?.Name ?? "", settings.ContextTokens) is { } ninferWarning) DetailLabel.Text = ninferWarning;
        }
        catch (Exception ex) { StatusLabel.Text = ex.Message; GpuLabel.Text = "No se pudo comprobar la placa"; LogError(ex); }
        RefreshControls();
        await RefreshMemoryAsync();
    }
    private async Task RefreshMemoryAsync()
    {
        if (readingMemory || closing || exiting || memoryLifetime.IsCancellationRequested || hardware?.SelectedGpu is not { } gpu) return;
        readingMemory = true;
        try
        {
            var sample = await HardwareDetector.ReadMemoryAsync(gpu.Index, memoryLifetime.Token);
            if (closing) return;
            VramUsage.Text = $"En uso: {sample.UsedMiB / 1024d:0.0} GiB · Libre: {sample.FreeMiB / 1024d:0.0} GiB";
            VramBar.Value = sample.UsedPercent; VramBar.Visibility = Visibility.Visible;
            VramTotal.Text = $"Total: {sample.TotalMiB / 1024d:0.0} GiB · incluye todas las aplicaciones";
            if (hardware is not null) hardware = hardware with { Gpus = hardware.Gpus.Select(item => item.Index == gpu.Index ? item with { FreeMiB = sample.FreeMiB } : item).ToArray() };
            RefreshControls();
        }
        catch (OperationCanceledException) when (closing || memoryLifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        { if (!closing) { VramUsage.Text = "Medición de VRAM no disponible"; VramBar.Visibility = Visibility.Collapsed; VramTotal.Text = "Se volverá a intentar automáticamente."; } }
        finally { readingMemory = false; }
    }
    private void RefreshControls()
    {
        var compatible = hardware?.Problem is null && hardware?.SelectedGpu is not null && Environment.OSVersion.Version.Build >= 22000;
        var available = !busy && !closing;
        StartButton.IsEnabled = available && compatible && provisioner.IsPrepared(settings) && Directory.Exists(settings.LastProject)
            && (runtime.State == RuntimeState.Ready || settings.LicenseAccepted && hardware!.SelectedGpu!.FreeMiB >= GpuProfiles.MinimumFreeMiB(settings, hardware.SelectedGpu));
        StartButton.ToolTip = !compatible ? "No se detectó un equipo compatible." : !provisioner.IsPrepared(settings) ? "Prepará los modelos y herramientas de programación."
            : !Directory.Exists(settings.LastProject) ? "Elegí la carpeta de tu proyecto."
            : runtime.State != RuntimeState.Ready && !settings.LicenseAccepted ? "Revisá y aceptá las licencias antes de iniciar."
            : runtime.State != RuntimeState.Ready && hardware!.SelectedGpu!.FreeMiB < GpuProfiles.MinimumFreeMiB(settings, hardware.SelectedGpu) ? "Liberá memoria de GPU para iniciar con el perfil elegido." : null;
        StartButton.Content = runtime.State == RuntimeState.Ready ? "Abrir proyecto" : "Iniciar IA";
        StopButton.IsEnabled = available && (runtime.State is not (RuntimeState.Stopped or RuntimeState.Error) || visualPane.IsActive);
        PrepareButton.IsEnabled = available && compatible && runtime.State != RuntimeState.Ready;
        PrepareButton.Content = preparationPaused || VerifiedDownloader.HasPartial(ReleaseProfile.ModelFor(settings.EngineMode), paths.ActiveModelPath(settings)) ? "Reanudar preparación" : "Preparar IA";
        ProjectButton.IsEnabled = available;
        ContextChoice.IsEnabled = available && runtime.State is RuntimeState.Stopped or RuntimeState.Error;
        AdvancedButton.IsEnabled = ContextChoice.IsEnabled;
        GitHubButton.IsEnabled = ContextChoice.IsEnabled;
        ModeChoice.IsEnabled = ContextChoice.IsEnabled;
        if (ModeChoice.Items[1] is System.Windows.Controls.ComboBoxItem nativeChoice) nativeChoice.IsEnabled = !GpuProfiles.Is3090(settings);
        StorageButton.IsEnabled = ContextChoice.IsEnabled;
        NinferEngineButton.IsEnabled = available && runtime.State is RuntimeState.Stopped or RuntimeState.Error;
        ResetNinferEngineButton.IsEnabled = NinferEngineButton.IsEnabled;
        NinferVisionCheck.IsEnabled = ContextChoice.IsEnabled;
        UpdateButton.IsEnabled = available;
        ModelFolderButton.IsEnabled = ImportButton.IsEnabled = ContextChoice.IsEnabled;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Content = preparing || visualPane.IsPreparing ? "Pausar" : "Cancelar";
        var visual = settings.Mode == AppMode.Visual;
        var bothStopped = runtime.State is RuntimeState.Stopped or RuntimeState.Error && !visualPane.IsActive;
        WorkspaceChoice.IsEnabled = available;
        visualPane.SetAvailable(available, compatible);
        if (visual)
        {
            StartButton.IsEnabled = available && compatible && !visualPane.IsReady && visualPane.StartProblem.Length == 0;
            StartButton.ToolTip = visualPane.StartProblem is { Length: > 0 } reason ? reason : null;
            StartButton.Content = visualPane.IsReady ? "IA iniciada" : "Iniciar IA";
            StopButton.IsEnabled = available && (visualPane.IsActive || runtime.State is not (RuntimeState.Stopped or RuntimeState.Error));
            ContextChoice.IsEnabled = AdvancedButton.IsEnabled = GitHubButton.IsEnabled = false;
        }
        StartButton.Background = new SolidColorBrush(StartButton.IsEnabled ? Color.FromRgb(8, 127, 112) : Color.FromRgb(38, 55, 67));
        StartButton.Foreground = Brushes.White;
        StorageButton.IsEnabled = available && bothStopped;
    }
    private void SmokeVisualModes(string report)
    {
        try
        {
            if (RenderOptions.ProcessRenderMode != System.Windows.Interop.RenderMode.SoftwareOnly)
                throw new Exception("La interfaz no inició con el renderizado compatible.");
            hardware = new([new(0, "NVIDIA GeForce RTX 5080", 16384, 15000, "fixture")], 32);
            var context = settings.ContextTokens; var engine = settings.EngineMode;
            WorkspaceChoice.SelectedIndex = 1; RefreshControls();
            if (settings.Mode != AppMode.Visual || VisualContainer.Visibility != Visibility.Visible || WelcomePane.Visibility == Visibility.Visible || ProjectArea.Visibility != Visibility.Collapsed
                || GitHubButton.Visibility != Visibility.Collapsed || StartButton.Visibility != Visibility.Visible || visualPane.StartProblem.Length == 0 || !ConfigurationSummary.Text.Contains("HiDream")) throw new Exception("No se aisló el espacio visual con inicio visible y una explicación de lo que falta.");
            if (StartButton.IsEnabled || ((SolidColorBrush)StartButton.Background).Color != Color.FromRgb(38, 55, 67)) throw new Exception("Iniciar IA se destaca o habilita con modelos pendientes.");
            busy = true; RefreshControls();
            if (CancelButton.Visibility != Visibility.Visible || WelcomePane.IsAncestorOf(CancelButton) || !Equals(CancelButton.Content, "Cancelar")) throw new Exception("La cancelación no es accesible desde el encabezado visual.");
            busy = false; RefreshControls();
            visualPane.VerifyInterfaceForTests();
            WorkspaceChoice.SelectedIndex = 0; RefreshControls();
            if (settings.Mode != AppMode.Programming || VisualContainer.Visibility == Visibility.Visible || WelcomePane.Visibility != Visibility.Visible || settings.ContextTokens != context || settings.EngineMode != engine) throw new Exception("Se alteró programación al volver del modo visual.");
            settings.Mode = AppMode.Visual; paths.SaveSettings(settings);
            if (paths.LoadSettings().Mode != AppMode.Visual || File.Exists(paths.AgentExe) || File.Exists(paths.ComfyPython)) throw new Exception("Se requiere instalar un motor para seleccionar modo.");
            AppPaths.AtomicWrite(report, "{\"status\":\"PASS\",\"independentModes\":true,\"referenceEditing\":true,\"softwareRendering\":true,\"gpuStarted\":false}");
        }
        catch (Exception error) { AppPaths.AtomicWrite(report, System.Text.Json.JsonSerializer.Serialize(new { status = "FAIL", error = error.ToString() })); }
    }
    private void ApplyWorkspaceToUi()
    {
        var visual = settings.Mode == AppMode.Visual;
        switchingWorkspace = true; WorkspaceChoice.SelectedIndex = visual ? 1 : 0; switchingWorkspace = false;
        ProjectArea.Visibility = visual ? Visibility.Collapsed : Visibility.Visible;
        ProjectSectionLabel.Visibility = visual ? Visibility.Collapsed : Visibility.Visible;
        ConfigurationExpander.IsExpanded = !visual;
        GitHubButton.Visibility = AdvancedButton.Visibility = visual ? Visibility.Collapsed : Visibility.Visible;
        WelcomePane.Visibility = !visual && Chat.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        if (visual) Chat.Visibility = Visibility.Collapsed;
        VisualContainer.Visibility = visual ? Visibility.Visible : Visibility.Collapsed;
        StartButton.Visibility = Visibility.Visible;
        WorkspaceTitle.Text = visual ? "Tu espacio para crear imágenes" : "Tu espacio para construir";
        WorkspaceDescription.Text = visual ? "Imágenes, cambios y animaciones, con la IA en tu PC." : "Archivos, herramientas y Git, con la IA en tu PC.";
        DetailLabel.Text = visual ? "Creá imágenes, editá detalles o animá una imagen. Todo se procesa en tu equipo." : "Elegí un proyecto para trabajar con tus archivos y Git local.";
        visualPane.ReloadSettings(); UpdateConfigurationSummary();
    }
    private async void WorkspaceChoice_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings is null || visualPane is null || switchingWorkspace || WorkspaceChoice.SelectedIndex < 0) return;
        var requested = WorkspaceChoice.SelectedIndex == 1 ? AppMode.Visual : AppMode.Programming;
        if (requested == settings.Mode) return;
        if (busy || closing) { ApplyWorkspaceToUi(); return; }
        if (runtime.State == RuntimeState.Ready || visualPane.IsActive)
        {
            if (MessageBox.Show(this, "Para cambiar de modo se detendrá la IA actual y se liberará su memoria. Tus archivos y resultados se conservan. ¿Continuar?", "Cambiar de modo", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            { ApplyWorkspaceToUi(); return; }
            await RunOperationAsync(async _ => { await runtime.StopAsync(); await visualPane.StopAsync(); });
            if (runtime.State == RuntimeState.Ready || visualPane.IsActive) { ApplyWorkspaceToUi(); return; }
        }
        settings.Mode = requested; paths.SaveSettings(settings); ApplyWorkspaceToUi(); RefreshControls();
        StatusLabel.Text = requested == AppMode.Visual ? "Modo visual seleccionado. Prepará sus modelos y pulsá Iniciar IA." : "Modo programación seleccionado. Elegí tu proyecto e iniciá la IA.";
    }
    private void ContextChoice_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings is null || paths is null || populatingContext) return; // XAML may raise this during InitializeComponent.
        var options = ContextOptions;
        var index = ContextChoice.SelectedIndex;
        if (index < 0 || index >= options.Length) return;
        var requestedContext = options[index];
        EngineTuning? recommended = null;
        try
        {
            if (settings.EngineMode == EngineMode.NInfer5080) Ninfer5080Profile.Validate(requestedContext);
            else
            {
                if (hardware?.SelectedGpu is { } gpu)
                {
                    var preset = GpuProfiles.For(gpu);
                    if (settings.UseRecommendedProfile || gpu.IsRtx3090 && preset.MatchesTuning(settings.ContextTokens, settings.Tuning)) recommended = preset.CreateTuning(requestedContext);
                }
                (recommended ?? settings.Tuning).Validate(requestedContext);
            }
        }
        catch (InvalidDataException ex)
        {
            PopulateContextChoices();
            DetailLabel.Text = ex.Message + " Revisá Configuración avanzada antes de cambiar de contexto.";
            return;
        }
        settings.ContextTokens = requestedContext;
        if (recommended is not null) settings.Tuning = recommended;
        if (!synchronizingProfile) settings.UseRecommendedProfile = false;
        paths.SaveSettings(settings);
        UpdateContextDetail();
        UpdateConfigurationSummary();
    }
    private int[] ContextOptions => settings.EngineMode == EngineMode.NInfer5080 ? Ninfer5080Profile.Contexts : GpuProfiles.KvMemContexts(settings);
    private string ContextLabel(EngineMode mode, int tokens) => tokens switch
    {
        131072 => mode == EngineMode.NInfer5080 ? "128K · NInfer-5080 · requiere la GPU casi libre" : "128K · RTX 3090 · inicial, validación física pendiente",
        65536 => mode == EngineMode.NInfer5080 ? "64K · NInfer-5080 (recomendado)" : GpuProfiles.Is3090(settings) ? "64K · RTX 3090 · menor uso de memoria" : "64K · experimental, pruebas básicas en 5080",
        _ => "32K · perfil de referencia"
    };
    private void PopulateContextChoices()
    {
        var guard = populatingContext; populatingContext = true;
        try
        {
            ContextChoice.Items.Clear();
            var options = ContextOptions;
            foreach (var tokens in options) ContextChoice.Items.Add(ContextLabel(settings.EngineMode, tokens));
            var index = Array.IndexOf(options, settings.ContextTokens);
            ContextChoice.SelectedIndex = index >= 0 ? index : 0;
        }
        finally { populatingContext = guard; }
    }
    private void ApplyModeToUi()
    {
        var ninfer = settings.EngineMode == EngineMode.NInfer5080;
        var guard = populatingMode; populatingMode = true;
        try
        {
            ModeChoice.SelectedIndex = ninfer ? 1 : 0;
            NinferVisionCheck.IsChecked = settings.NinferVision;
            ModeHint.Text = ninfer
                ? "Modelo oficial de Todd Ballinger con motor nativo de Windows. La configuración se adapta a tu placa; el motor se descarga al preparar la IA."
                : GpuProfiles.Is3090(settings) ? "RTX 3090: motor KVMem para RTX 30 y Qwen Swift. Perfil inicial 128K para aprovechar los 24 GB; validación física pendiente." : "KVMem 0.18.0: Qwen Swift, contexto 32K o 64K y caché KV en RAM. Las pruebas históricas corresponden a una RTX 5080.";
            NinferEngineButton.Visibility = NinferEngineLabel.Visibility = NinferVisionCheck.Visibility = ninfer ? Visibility.Visible : Visibility.Collapsed;
            ResetNinferEngineButton.Visibility = ninfer && !string.IsNullOrWhiteSpace(settings.NinferEnginePath) ? Visibility.Visible : Visibility.Collapsed;
            NinferEngineLabel.Text = paths.HasNinferEngine(settings) ? "Motor: " + paths.NinferEngineExe(settings) : string.IsNullOrWhiteSpace(settings.NinferEnginePath) ? "Motor: se descargará y verificará al preparar la IA." : "Motor importado no encontrado. Elegí otro o restaurá el automático.";
            DownloadNote.Text = ninfer
                ? "Qwen3.8-27B pesa 16,5 GB. Descarga a máxima velocidad disponible con 16 conexiones, pausa, reanudación y verificación SHA-256."
                : "El modelo pesa unos 12,8 GB. Elegí dónde guardarlo: la descarga se puede reanudar y se verifica antes de usarla.";
            ContextHint.Text = ninfer
                ? "64K es el perfil inicial en ambas placas. 128K es experimental en Windows nativo y puede no entrar en la VRAM libre."
                : GpuProfiles.Is3090(settings) ? "128K inicial: 96K de historial seleccionado y 16K de reserva en GPU, con hasta 8 GiB de caché en RAM. KVMem recupera historial: no equivale a atención completa sobre 128K en GPU. Podés elegir 64K o 32K con la IA detenida." : "64K permite un historial más largo mediante KVMem y reserva hasta 4 GiB de caché en RAM. No equivale a atender 64K completos en GPU. Cambialo con la IA detenida.";
            ModelLocation.Text = string.IsNullOrWhiteSpace(paths.ImportedModelPath(settings)) ? paths.ModelDirectoryPath(settings) : paths.ActiveModelPath(settings);
            ImportButton.Content = ninfer ? "Usar artefacto NInfer-5080 ya descargado" : "Usar modelo ya descargado";
            ModeFootnote.Text = ninfer
                ? "Port nativo de Windows basado en ninfer-5080 de Todd Ballinger. Modelo Apache-2.0. La prueba de inferencia de estos perfiles en Forja sigue pendiente."
                : "La variante Swift tiene condiciones propias para uso comercial. Visión desactivada para reservar memoria al código. La 5070 Ti y la 3090 todavía requieren validación física en Forja.";
            PopulateContextChoices();
        }
        finally { populatingMode = guard; }
        UpdateConfigurationSummary();
    }
    private void UpdateContextDetail()
    {
        var warning = settings.EngineMode == EngineMode.NInfer5080 ? Ninfer5080Profile.WarningFor(hardware?.SelectedGpu?.Name ?? "", settings.ContextTokens) : null;
        DetailLabel.Text = warning ?? (settings.EngineMode == EngineMode.NInfer5080
            ? (settings.ContextTokens == 131072 ? "128K: experimental en Windows nativo; todavía no validamos el contexto completo." : "64K: contexto recomendado del modo NInfer-5080.")
            : GpuProfiles.Is3090(settings) ? $"RTX 3090 · {settings.ContextTokens / 1024}K · motor sm86 · perfil inicial pendiente de validación física."
            : (settings.ContextTokens == 65536 ? "64K: pruebas básicas aprobadas en RTX 5080; uso prolongado y 5070 Ti pendientes de validar." : "32K: perfil de referencia. El historial se resume cuando se acerca al límite."));
    }
    private void ModeChoice_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings is null || paths is null || populatingMode) return;
        if (busy || closing || runtime.State is not (RuntimeState.Stopped or RuntimeState.Error) || GpuProfiles.Is3090(settings) && ModeChoice.SelectedIndex == 1) { ApplyModeToUi(); return; }
        settings.EngineMode = ModeChoice.SelectedIndex == 1 ? EngineMode.NInfer5080 : EngineMode.KvMem; settings.LicenseAccepted = false; LicenseCheck.IsChecked = false;
        if (settings.EngineMode == EngineMode.NInfer5080) { if (settings.ContextTokens is not (65536 or 131072)) settings.ContextTokens = Ninfer5080Profile.DefaultContext; }
        else if (!GpuProfiles.KvMemContexts(settings).Contains(settings.ContextTokens)) settings.ContextTokens = ReleaseProfile.Context;
        paths.SaveSettings(settings);
        ApplyModeToUi(); RefreshControls(); UpdateContextDetail();
        StatusLabel.Text = settings.EngineMode == EngineMode.NInfer5080 ? "Motor nativo seleccionado. Pulsá Preparar IA para descargar lo necesario." : "Modo Forja seleccionado. Prepará la IA para empezar.";
    }
    private void ChooseNinferEngine_Click(object sender, RoutedEventArgs e)
    {
        if (busy || closing || runtime.State is not (RuntimeState.Stopped or RuntimeState.Error)) return;
        var dialog = new OpenFileDialog { Title = "Elegí el motor ninfer-serve compilado del proyecto ninfer-5080", Filter = "Motor ninfer-serve (ninfer-serve.exe)|ninfer-serve.exe|Ejecutable (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) != true) return;
        if (!string.Equals(Path.GetFileName(dialog.FileName), "ninfer-serve.exe", StringComparison.OrdinalIgnoreCase)) { StatusLabel.Text = "Elegí el ejecutable ninfer-serve.exe compilado del proyecto ninfer-5080."; return; }
        settings.NinferEnginePath = dialog.FileName; paths.SaveSettings(settings);
        ApplyModeToUi(); RefreshControls();
        StatusLabel.Text = "Motor ninfer-serve elegido. Prepará el artefacto para descargarlo.";
    }
    private void ResetNinferEngine_Click(object sender, RoutedEventArgs e)
    {
        if (busy || closing || runtime.State is not (RuntimeState.Stopped or RuntimeState.Error)) return;
        settings.NinferEnginePath = ""; paths.SaveSettings(settings);
        ApplyModeToUi(); RefreshControls(); UpdateConfigurationSummary();
        StatusLabel.Text = "Motor automático restaurado. Prepará la IA para descargarlo y verificarlo.";
    }
    private void NinferVision_Click(object sender, RoutedEventArgs e)
    {
        if (settings is null || populatingMode) return;
        if (runtime.State is not (RuntimeState.Stopped or RuntimeState.Error)) { NinferVisionCheck.IsChecked = settings.NinferVision; return; }
        settings.NinferVision = NinferVisionCheck.IsChecked == true; paths.SaveSettings(settings); UpdateConfigurationSummary();
    }
    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        if (busy || closing || runtime.State is not (RuntimeState.Stopped or RuntimeState.Error)) return;
        new GitHubWindow(paths, settings, http) { Owner = this }.ShowDialog();
        RefreshControls();
    }
    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        if (busy || closing || runtime.State is not (RuntimeState.Stopped or RuntimeState.Error)) return;
        if (settings.EngineMode == EngineMode.NInfer5080)
        {
            var native = new NativeSettingsWindow(settings) { Owner = this };
            if (native.ShowDialog() != true) return;
            settings.NativeTuning = native.Result; settings.ContextTokens = native.ContextTokens;
            settings.UseRecommendedProfile = false;
            paths.SaveSettings(settings); ApplyModeToUi(); RefreshControls(); UpdateContextDetail();
            StatusLabel.Text = "Configuración nativa guardada. Se aplicará al iniciar la IA.";
            return;
        }
        var dialog = new AdvancedSettingsWindow(settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        settings.Tuning = dialog.Result; settings.ContextTokens = dialog.ContextTokens;
        settings.UseRecommendedProfile = hardware?.SelectedGpu is { } gpu && GpuProfiles.For(gpu).Matches(settings.ContextTokens, settings.Tuning);
        synchronizingProfile = true;
        PopulateContextChoices();
        synchronizingProfile = false;
        paths.SaveSettings(settings); UpdateConfigurationSummary();
        StatusLabel.Text = "Configuración guardada. Se aplicará al iniciar la IA.";
    }
    private void UpdateConfigurationSummary()
    {
        if (settings is null || ConfigurationSummary is null) return;
        if (settings.Mode == AppMode.Visual && visualPane is not null) { ConfigurationSummary.Text = visualPane.Summary; return; }
        var t = settings.Tuning;
        if (settings.EngineMode == EngineMode.NInfer5080)
        {
            var native = settings.NativeTuning;
            var prefill = native.EffectivePrefill(hardware?.SelectedGpu is { } detected ? Ninfer5080Profile.For(detected).PrefillChunk : Ninfer5080Profile.PrefillChunk);
            ConfigurationSummary.Text = $"Perfil: {hardware?.SelectedGpu?.Profile ?? "por detectar"}\n"
                + "NInfer-5080 · port nativo de YukinoKaorisuna\n"
                + "Qwen3.8-27B · artefacto oficial (16,5 GB)\n"
                + (paths.HasNinferEngine(settings) ? "Motor: ninfer-serve listo" : "Motor: descarga automática") + "\n"
                + "OpenCode 1.19.3 · Git 2.53.0.3 · Node 24.19.0\n"
                + $"Contexto / KV: {settings.ContextTokens:N0} tokens\n"
                + $"KV: {Ninfer5080Profile.KvDtype} · MTP: {native.DraftTokens} borradores\n"
                + $"Prefill: {prefill} · {(native.PrefillChunk == 0 ? "automático por placa" : "manual")}\n"
                + $"Visión: {(settings.NinferVision ? $"activada · {native.VisionMaxTokens} tokens" : "desactivada")}\n"
                + $"Razonamiento: {native.ThinkingTokens:N0} · salida: {native.MaxOutput:N0}\n"
                + $"Caché de prefijos: {Ninfer5080Profile.CheckpointPolicy}\n"
                + $"Embeddings en RAM · CUDA Graph: {(native.CudaGraph ? "activado" : "desactivado")}\n"
                + "1 sesión · 1 petición a la vez\n"
                + "Red: solo 127.0.0.1\nPrueba en GPU de Forja: pendiente";
            return;
        }
        ConfigurationSummary.Text = $"Perfil: {hardware?.SelectedGpu?.Profile ?? "por detectar"}\nAjustes: {(settings.UseRecommendedProfile ? "automáticos" : "personalizados")}\nQwen 27B · Swift 1.5 · IQ3_S\nNInfer / KVMem 0.18.0\nOpenCode 1.19.3\nGit 2.53.0.3 · Node 24.19.0\nContexto: {settings.ContextTokens:N0} tokens\nHistorial GPU: {t.HistoryTokens:N0}\nReserva GPU: {t.GenerationReserve:N0}\nCaché RAM: {t.EffectiveHostMiB(settings.ContextTokens):N0} MiB\nKV: {t.KvType} · GDN: FP16\nInicio / reciente: {t.SinkTokens} / {t.RecentTokens}\nPrefill: {t.PrefillChunk} · MTP: {t.DraftTokens} borradores\nNgram: {t.NgramDraftTokens} · mínimo {t.NgramMinMatch}\nMTP adaptativo: desactivado\nVentana MTP: historial completo\nRazonamiento: {t.ThinkingTokens:N0} tokens\nSalida máxima: {t.MaxOutput:N0} tokens\nTemperatura motor / agente: {t.Temperature} / {t.AgentTemperature}\nTop P: {t.TopP} · Top K: {t.TopK}\nMin P: {t.MinP}\n1 sesión · 1 petición a la vez\nVisión / caché SSD: desactivadas\nPerfil automático: desactivado\nRed: solo 127.0.0.1\nHistorial: resumen automático";
    }
    private void ApplyHardwareProfile()
    {
        if (hardware?.SelectedGpu is not { } gpu) return;
        _ = GpuProfiles.Apply(settings, gpu);
        LicenseCheck.IsChecked = settings.LicenseAccepted;
        synchronizingProfile = true;
        ApplyModeToUi();
        synchronizingProfile = false;
        paths.SaveSettings(settings); UpdateConfigurationSummary();
    }
    private async Task RunOperationAsync(Func<CancellationToken, Task> action)
    {
        if (!preparing && !visualPane.IsPreparing) preparationPaused = false;
        if (busy || closing) return; busy = true; operation = new(); operationFinished = new(TaskCreationOptions.RunContinuationsAsynchronously); Progress.Visibility = Visibility.Visible; Progress.IsIndeterminate = true; RefreshControls();
        try
        {
            await action(operation.Token);
            if (settings.Mode == AppMode.Visual) DetailLabel.Text = visualPane.IsReady ? "La IA está lista para tu próxima creación." : "Podés iniciar la IA desde el botón de arriba a la derecha.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = preparationPaused ? settings.Mode == AppMode.Visual ? "Descarga pausada. Abrí Modelos y descargas para continuar." : "Preparación pausada. Pulsá Preparar / reanudar para continuar." : settings.Mode == AppMode.Visual ? "Acción visual cancelada. Las creaciones anteriores se conservan." : "Acción cancelada. La descarga parcial se conserva para reanudar.";
            if (settings.Mode == AppMode.Visual) DetailLabel.Text = visualPane.IsReady ? "La IA sigue disponible para tu próximo pedido." : "Tus archivos se conservan. Podés volver a iniciar la IA cuando quieras.";
        }
        catch (Exception ex) { LogError(ex); StatusLabel.Text = ex.Message; DetailLabel.Text = "Podés revisar Diagnóstico y reintentar. No se muestra un error de una operación anterior."; }
        finally { operation.Dispose(); operation = null; busy = false; Progress.Visibility = Visibility.Collapsed; RefreshControls(); operationFinished.TrySetResult(); }
    }
    private void Storage_Click(object sender, RoutedEventArgs e)
    {
        if (busy || closing || visualPane.IsActive || runtime.State is not (RuntimeState.Stopped or RuntimeState.Error)) return;
        var window = new StorageWindow(paths, settings, releaseBrowser: ReleaseBrowserForCleanupAsync) { Owner = this };
        window.ShowDialog();
        if (window.DataDeleted) RefreshAfterStorageCleanup(window.SettingsDeleted);
        else { ApplyModeToUi(); RefreshControls(); }
    }
    private void RefreshAfterStorageCleanup(bool settingsDeleted)
    {
        preparationPaused = false;
        if (settingsDeleted)
        {
            settings = new(); LicenseCheck.IsChecked = false;
            ApplyHardwareProfile();
        }
        Chat.Visibility = Visibility.Collapsed; WelcomePane.Visibility = Visibility.Visible;
        UpdateProject(); ApplyModeToUi(); ApplyWorkspaceToUi(); RefreshControls();
        StatusLabel.Text = "Limpieza completada. Forja sigue abierta; podés preparar la IA de nuevo.";
        DetailLabel.Text = settingsDeleted ? "Se restauraron los ajustes iniciales. Los archivos de tus proyectos se conservaron." : "Se eliminaron solo las categorías confirmadas.";
    }
    private async Task ReleaseBrowserForCleanupAsync()
    {
        if (!browserReady || Chat.CoreWebView2 is null) return;
        var environment = Chat.CoreWebView2.Environment;
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs args) => exited.TrySetResult();
        environment.BrowserProcessExited += OnExited;
        try { Chat.Dispose(); browserReady = false; await exited.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally
        {
            environment.BrowserProcessExited -= OnExited;
            if (Chat.Parent is System.Windows.Controls.Panel parent)
            {
                var index = parent.Children.IndexOf(Chat);
                parent.Children.Remove(Chat);
                UnregisterName("Chat");
                Chat = new Microsoft.Web.WebView2.Wpf.WebView2 { Name = "Chat", Visibility = Visibility.Collapsed };
                RegisterName("Chat", Chat); parent.Children.Insert(index, Chat);
            }
        }
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
        var ninfer = settings.EngineMode == EngineMode.NInfer5080;
        var asset = ReleaseProfile.ModelFor(settings.EngineMode);
        var dialog = new OpenFileDialog { Title = ninfer ? "Seleccioná el artefacto qwen3_8_27b.ninfer" : "Seleccioná el modelo Qwen de esta versión", Filter = "Modelo NInfer (*.ninfer)|*.ninfer" };
        if (dialog.ShowDialog(this) != true) return;
        await RunOperationAsync(async ct =>
        {
            StatusLabel.Text = "Verificando el modelo elegido...";
            if (!await VerifiedDownloader.MatchesAsync(dialog.FileName, asset, ct)) throw new InvalidDataException(ninfer ? "Ese archivo no es el artefacto oficial qwen3_8_27b.ninfer (SHA-256 c4a7e9…b050e21)." : "Ese archivo no coincide con el modelo de esta versión.");
            if (ninfer) settings.ImportedNinferModel = dialog.FileName; else settings.ImportedModel = dialog.FileName;
            paths.SaveSettings(settings); ModelLocation.Text = dialog.FileName;
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
        if (settings.Mode == AppMode.Visual)
        {
            if (visualPane.StartProblem is { Length: > 0 } reason)
            {
                StatusLabel.Text = reason;
                DetailLabel.Text = "Revisá Modelos y descargas en el panel visual; lo descargado se reutiliza.";
                return;
            }
            await runtime.StopAsync(); await visualPane.StartAsync(ct); UpdateConfigurationSummary(); return;
        }
        await visualPane.StopAsync();
        ApplyHardwareProfile();
        var gpu = hardware.SelectedGpu!;
        GpuProfiles.ValidateBackend(settings);
        var minimumFree = GpuProfiles.MinimumFreeMiB(settings, gpu);
        if (runtime.State != RuntimeState.Ready && gpu.FreeMiB < minimumFree) throw new InvalidOperationException(settings.EngineMode == EngineMode.NInfer5080
            ? $"Este perfil necesita al menos unos {minimumFree:N0} MiB libres antes de iniciar. Cerrá otras aplicaciones que usen la GPU y reintentá; no se cerrará ningún programa ajeno."
            : $"Este perfil pide al menos {minimumFree:N0} MiB libres para intentar el inicio. Cerrá otras aplicaciones que usen la GPU o elegí un perfil de menor contexto; no se cerrará ningún programa ajeno.");
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
    private async void Stop_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async _ => { await runtime.StopAsync(); await visualPane.StopAsync(); Chat.Visibility = Visibility.Collapsed; ApplyWorkspaceToUi(); StatusLabel.Text = "IA detenida. Tus archivos y creaciones siguen guardados."; });
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (preparing || visualPane.IsPreparing) { preparationPaused = true; StatusLabel.Text = "Pausando y guardando el progreso..."; }
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
    private void Licenses_Click(object sender, RoutedEventArgs e) => OpenExternal(settings.EngineMode == EngineMode.NInfer5080 ? "https://huggingface.co/ninfer-5080/Qwen3.8-27B-RTX5080" : "https://huggingface.co/fyb1214/Swift-1.5-Qwen3.8-27B-GSQ-RCO-NInfer/tree/7da3e38b9ee7a6c8888cf2112d3c5a48a952a2a8");
    private static void OpenExternal(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void LogError(Exception error)
    {
        Directory.CreateDirectory(paths.Logs);
        File.AppendAllText(Path.Combine(paths.Logs, "application-errors.log"), DateTimeOffset.Now + " " + error + Environment.NewLine);
    }
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
        var confirmed = closeConfirmationForTest?.Invoke() ?? MessageBox.Show(this,
            "¿Querés cerrar Forja? Se detendrá la IA, se liberará su memoria y se pausarán las descargas. Tus proyectos y chats se conservan.",
            "Cerrar Forja 50", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!confirmed)
        { pendingInstaller = null; closing = false; RefreshControls(); return; }
        operation?.Cancel(); healthTimer.Stop(); memoryTimer.Stop(); memoryLifetime.Cancel();
        try
        {
            if (operationFinished is { } pending) await pending.Task;
            await runtime.DisposeAsync();
            await visualPane.DisposeAsync();
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
