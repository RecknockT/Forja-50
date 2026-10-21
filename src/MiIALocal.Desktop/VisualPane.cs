using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MiIALocal.Core;

namespace MiIALocal.Desktop;

public sealed class VisualPane : UserControl
{
    private readonly AppPaths paths;
    private readonly Func<AppSettings> settings;
    private readonly Func<GpuInfo?> gpu;
    private readonly Func<Func<CancellationToken, Task>, Task> run;
    private readonly Action<string> status;
    private readonly Action changed;
    private readonly Action<PreparationProgress> progress;
    private readonly Action activateMode;
    private readonly Action cancelOperation;
    private readonly VisualProvisioner provisioner;
    private readonly VisualRuntime runtime;
    private readonly TextBox prompt = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxLength = 6000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ComboBox task = new() { ItemsSource = new[] { "Crear una imagen nueva", "Editar la imagen seleccionada", "Crear una animación" }, SelectedIndex = 0 };
    private readonly ComboBox size = new() { ItemsSource = new[] { "Cuadrada · 1024 × 1024", "Horizontal · 1024 × 768", "Vertical · 768 × 1024" }, SelectedIndex = 0 };
    private readonly ComboBox duration = new() { ItemsSource = new[] { "3 segundos", "5 segundos" }, SelectedIndex = 0 };
    private readonly TextBox seed = new() { MinWidth = 180, MaxLength = 16 };
    private readonly ComboBox steps = new() { ItemsSource = new[] { "10 pasos · más rápido", "20 pasos · inicial", "30 pasos · más lento" }, SelectedIndex = 1 };
    private readonly CheckBox animation = new() { Content = "Descargar también Wan para animaciones y GIF" };
    private readonly CheckBox license = new() { Content = "Revisé y acepto las licencias del modo visual" };
    private readonly Button prepare = new() { Content = "Preparar imágenes" };
    private readonly Button generate = new() { Content = "Generar", Background = new SolidColorBrush(Color.FromRgb(8, 127, 112)), Foreground = Brushes.White };
    private readonly Button attach = new() { Content = "Elegir referencia" };
    private readonly Button clear = new() { Content = "Imagen nueva" };
    private readonly Button open = new() { Content = "Abrir resultado" };
    private readonly Button export = new() { Content = "Guardar como…" };
    private readonly Button activate = new() { Content = "Activar modo imagen" };
    private readonly Button cancel = new() { Content = "Pausar descarga" };
    private readonly TextBlock setupStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    private readonly TextBlock resultTitle = new() { Text = "Tu próxima creación", FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock emptyPreview = new() { Text = "La imagen aparecerá acá\n\nCreá algo nuevo o elegí una imagen para cambiar un detalle.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 300 };
    private readonly TextBlock emptyGallery = new() { Text = "Todavía no hay creaciones. Tus resultados se guardarán aquí.", Foreground = Brushes.LightGray, Margin = new(0, 8, 0, 0) };
    private readonly Grid workspace = new();
    private readonly StackPanel editor = new();
    private readonly StackPanel results = new();
    private readonly Expander downloads = new() { Foreground = Brushes.White };
    private Button[] taskTabs = [];
    private readonly TextBlock referenceLabel = new() { Text = "Sin referencia: se creará una imagen nueva.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    private readonly TextBlock package = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    private readonly Image preview = new() { Height = 220, Stretch = Stretch.Uniform };
    private readonly ListBox gallery = new() { Height = 145 };
    private string? reference;
    private string? resultFile;
    private bool available, compatible, preparing, generating, loading, wasPrepared;
    public bool IsReady => runtime.IsReady;
    public bool IsActive => runtime.HasProcess;
    public bool IsPreparing => preparing;
    public bool IsPrepared => provisioner.IsPrepared(false);
    public string Summary => $"Imágenes y animación · independiente\nComfyUI {VisualCatalog.ComfyVersion}\nFLUX.2 Klein 4B · BF16 · 4 pasos\nCodificador interno Qwen 4B\n{(settings().Visual.IncludeAnimation ? "Wan 2.2 5B · FP16 · codificador FP8\n" : "Animaciones: descarga opcional\n")}1 imagen / trabajo a la vez\nTransferencia de componentes a RAM\nSin Qwen de programación ni OpenCode\nSolo conexión local protegida\nPruebas de inferencia: pendientes";

    public VisualPane(AppPaths paths, Func<AppSettings> settings, HttpClient http, Func<GpuInfo?> gpu,
        Func<Func<CancellationToken, Task>, Task> run, Action<string> status, Action changed, Action<PreparationProgress> progress, Action activateMode, Action cancelOperation)
    {
        Foreground = Brushes.White;
        this.paths = paths; this.settings = settings; this.gpu = gpu; this.run = run; this.status = status; this.changed = changed; this.progress = progress;
        this.activateMode = activateMode; this.cancelOperation = cancelOperation;
        provisioner = new(paths, new VerifiedDownloader(http)); runtime = new(paths, http);
        new VisualJobStore(paths).RecoverInterrupted();
        var panel = new StackPanel { Margin = new(24), MaxWidth = 1200, HorizontalAlignment = HorizontalAlignment.Stretch };
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(24, 35, 44)), CornerRadius = new(14), Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        panel.Children.Add(new TextBlock { Text = "De una idea a tu imagen", FontSize = 28, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "1  Prepará tu equipo     →     2  Creá o editá     →     3  Guardá tu resultado", Margin = new(0, 10, 0, 20), TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(91, 211, 189)) });
        var preparation = new StackPanel();
        var details = new StackPanel(); details.Children.Add(package);
        preparation.Children.Add(new TextBlock { Text = "La preparación se hace una sola vez. Después podés crear sin conexión.", Foreground = Brushes.LightGray, Margin = new(0, 0, 0, 10) });
        preparation.Children.Add(animation); animation.Margin = new(0, 0, 0, 10);
        preparation.Children.Add(new TextBlock { Text = "Solo imágenes: unos 18,2 GB. Con animaciones: unos 36,3 GB. Lo descargado se reutiliza.", Foreground = Brushes.LightGray, Margin = new(0, 0, 0, 12) });
        var licenses = new Button { Content = "Leer licencias", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 10) };
        licenses.Click += (_, _) => ShowLicenses(); preparation.Children.Add(licenses);
        preparation.Children.Add(license); license.Margin = new(0, 0, 0, 12); license.Content = "Acepto las licencias de los componentes visuales";
        preparation.Children.Add(new Expander { Header = "Ver componentes y ubicaciones", Foreground = Brushes.LightGray, Content = details, Margin = new(0, 10, 0, 0) });
        downloads.Header = "Preparación y descargas"; downloads.Content = preparation; downloads.IsExpanded = !IsPrepared;
        var setup = new StackPanel(); setup.Children.Add(new TextBlock { Text = "1 · Preparar", FontSize = 18, FontWeight = FontWeights.SemiBold });
        setup.Children.Add(setupStatus); setupStatus.Margin = new(0, 6, 0, 12); setup.Children.Add(downloads);
        var setupActions = new WrapPanel { Margin = new(0, 14, 0, 0) }; prepare.HorizontalAlignment = activate.HorizontalAlignment = cancel.HorizontalAlignment = HorizontalAlignment.Left;
        prepare.Background = activate.Background = new SolidColorBrush(Color.FromRgb(8, 127, 112)); prepare.Foreground = activate.Foreground = Brushes.White;
        setupActions.Children.Add(prepare); setupActions.Children.Add(activate); setupActions.Children.Add(cancel); setup.Children.Add(setupActions);
        panel.Children.Add(Card(setup));
        panel.Children.Add(workspace); workspace.Margin = new(0, 18, 0, 0);
        workspace.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new() { Width = new(1.35, GridUnitType.Star) });
        workspace.RowDefinitions.Add(new() { Height = GridLength.Auto }); workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var editCard = Card(editor); var resultCard = Card(results); workspace.Children.Add(editCard); workspace.Children.Add(resultCard);
        editor.Children.Add(new TextBlock { Text = "2 · ¿Qué querés crear?", FontSize = 20, FontWeight = FontWeights.SemiBold });
        editor.Children.Add(new TextBlock { Text = "Elegí una acción y contame tu idea.", Foreground = Brushes.LightGray, Margin = new(0, 6, 0, 16) });
        task.Visibility = Visibility.Collapsed; editor.Children.Add(task);
        var tabs = new WrapPanel { Margin = new(0, 0, 0, 14) };
        taskTabs = new[] { new Button { Content = "Crear imagen" }, new Button { Content = "Editar imagen" }, new Button { Content = "Animar" } };
        for (var index = 0; index < taskTabs.Length; index++) { var selected = index; taskTabs[index].Padding = new(10, 8, 10, 8); taskTabs[index].FontSize = 12; taskTabs[index].Click += (_, _) => task.SelectedIndex = selected; tabs.Children.Add(taskTabs[index]); }
        editor.Children.Add(tabs);
        editor.Children.Add(new TextBlock { Text = "Tu descripción", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) }); prompt.MinHeight = 120; editor.Children.Add(prompt);
        var example = new Button { Content = "Probar un ejemplo", HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12, Padding = new(0, 8, 0, 8), Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(91, 211, 189)) };
        example.Click += (_, _) => prompt.Text = task.SelectedIndex switch { 1 => "Cambiá solo el brazo izquierdo a rojo. Conservá los demás detalles.", 2 => "El personaje saluda suavemente con la mano. Cámara fija.", _ => "Un pequeño robot blanco en un taller acogedor, iluminación cálida y estilo de ilustración 3D." }; editor.Children.Add(example);
        editor.Children.Add(new TextBlock { Text = "Formato", FontWeight = FontWeights.SemiBold, Margin = new(0, 6, 0, 8) });
        var options = new WrapPanel(); options.Children.Add(size); options.Children.Add(duration); editor.Children.Add(options);
        var advanced = new StackPanel(); advanced.Children.Add(new TextBlock { Text = "Semilla: dejala vacía para variar cada resultado." }); advanced.Children.Add(seed);
        advanced.Children.Add(new TextBlock { Text = "Pasos de animación (no cambia los cuatro pasos de imágenes)." }); advanced.Children.Add(steps);
        editor.Children.Add(new Expander { Header = "Más opciones", Foreground = Brushes.LightGray, Content = advanced, Margin = new(0, 12, 0, 14) });
        var references = new WrapPanel(); attach.Content = "Elegir una imagen"; clear.Content = "Quitar referencia"; references.Children.Add(attach); references.Children.Add(clear); editor.Children.Add(references);
        referenceLabel.Margin = new(0, 10, 0, 18); referenceLabel.FontSize = 12; editor.Children.Add(referenceLabel);
        generate.HorizontalAlignment = HorizontalAlignment.Left; generate.MinWidth = 180; editor.Children.Add(generate);
        results.Children.Add(resultTitle); results.Children.Add(new TextBlock { Text = "3 · Revisá y guardá", Foreground = Brushes.LightGray, Margin = new(0, 6, 0, 14) });
        var canvas = new Grid { MinHeight = 280, Background = new SolidColorBrush(Color.FromRgb(13, 21, 28)), Margin = new(0, 0, 0, 14) }; preview.Height = 300; canvas.Children.Add(preview); canvas.Children.Add(emptyPreview); results.Children.Add(canvas);
        var actions = new WrapPanel(); actions.Children.Add(open); actions.Children.Add(export);
        open.Content = "Abrir"; export.Content = "Guardar…";
        foreach (var button in new[] { open, export }) { button.FontSize = 12; button.Padding = new(12, 9, 12, 9); }
        var folder = new Button { Content = "Ver carpeta", FontSize = 12, Padding = new(12, 9, 12, 9) }; folder.Click += (_, _) => { Directory.CreateDirectory(paths.VisualOutputs); OpenPath(paths.VisualOutputs); }; actions.Children.Add(folder); results.Children.Add(actions);
        var history = new StackPanel(); history.Children.Add(new TextBlock { Text = "Tus creaciones", FontSize = 18, FontWeight = FontWeights.SemiBold });
        history.Children.Add(new TextBlock { Text = "Seleccioná una imagen anterior para seguir editándola.", Foreground = Brushes.LightGray, Margin = new(0, 6, 0, 10) }); history.Children.Add(emptyGallery); history.Children.Add(gallery); panel.Children.Add(Card(history));
        gallery.Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)); gallery.Foreground = Brushes.White; gallery.BorderBrush = Brushes.DarkSlateGray;
        gallery.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBox'><Border Background='{TemplateBinding Background}' Padding='8' CornerRadius='8'><ScrollViewer Background='{TemplateBinding Background}' Focusable='False'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate>");
        SizeChanged += (_, _) => { var narrow = ActualWidth < 960; workspace.ColumnDefinitions[1].Width = narrow ? new(0) : new(1.35, GridUnitType.Star); Grid.SetRow(resultCard, narrow ? 1 : 0); Grid.SetColumn(resultCard, narrow ? 0 : 1); editCard.Margin = new(0, 0, narrow ? 0 : 16, narrow ? 16 : 0); };
        panel.Children.Add(new TextBlock { Text = "Animaciones experimentales: GIF y MP4. La primera carga puede tardar; la velocidad depende de tu placa y memoria libre. No se garantiza un bucle continuo.", Margin = new(0, 14, 0, 0), Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        activate.Click += (_, _) => this.activateMode(); cancel.Click += (_, _) => this.cancelOperation();
        animation.Click += (_, _) => { settings().Visual.IncludeAnimation = animation.IsChecked == true; Save(); UpdatePackage(); };
        license.Click += (_, _) => { settings().Visual.LicenseAccepted = license.IsChecked == true; Save(); };
        task.SelectionChanged += (_, _) => { Refresh(); if (task.SelectedIndex == 2 && !provisioner.IsPrepared(true)) status("Para animar, detené la IA, abrí Preparación y descargas y marcá la descarga de Wan. Después pulsá Descargar y preparar."); };
        prepare.Click += async (_, _) =>
        {
            if (!available || !compatible) return;
            if (license.IsChecked != true) { status("Revisá y aceptá las licencias visuales antes de descargar."); return; }
            preparing = true;
            try { await run(async ct => { await provisioner.PrepareAsync(settings().Visual, new Progress<PreparationProgress>(progress), ct); status("Descarga terminada. Pulsá Activar modo imagen para empezar."); }); }
            finally { preparing = false; Refresh(); }
        };
        attach.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg", Title = "Elegir imagen de referencia" };
            if (picker.ShowDialog(Window.GetWindow(this)) == true) { SetReference(picker.FileName); if (task.SelectedIndex != 2) task.SelectedIndex = 1; }
        };
        clear.Click += (_, _) => { reference = null; referenceLabel.Text = "Sin referencia: se creará una imagen nueva."; task.SelectedIndex = 0; Refresh(); };
        generate.Click += async (_, _) => await GenerateAsync();
        open.Click += (_, _) => { if (resultFile is not null && File.Exists(resultFile)) OpenPath(resultFile); };
        export.Click += (_, _) => Export();
        gallery.SelectionChanged += (_, _) =>
        {
            if (gallery.SelectedItem is not GalleryItem item) return;
            var file = item.Job.Files.FirstOrDefault(IsOwnedExistingResult);
            if (file is null) { status("Este trabajo no tiene un resultado disponible; sus datos siguen en el historial."); return; }
            ShowResult(file);
            if (Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase)) { SetReference(file); task.SelectedIndex = 1; }
        };
        ReloadSettings(); LoadGallery();
    }
    private static Border Card(UIElement content) => new() { Child = content, Background = new SolidColorBrush(Color.FromRgb(28, 41, 51)), BorderBrush = new SolidColorBrush(Color.FromRgb(42, 59, 71)), BorderThickness = new(1), CornerRadius = new(12), Padding = new(20), Margin = new(0, 0, 0, 16) };
    public void ReloadSettings()
    {
        var visual = settings().Visual;
        animation.IsChecked = visual.IncludeAnimation; license.IsChecked = visual.LicenseAccepted;
        size.SelectedIndex = visual.Width > visual.Height ? 1 : visual.Height > visual.Width ? 2 : 0;
        duration.SelectedIndex = visual.AnimationFrames == 121 ? 1 : 0;
        steps.SelectedIndex = visual.AnimationSteps switch { 10 => 0, 30 => 2, _ => 1 };
        UpdatePackage(); LoadGallery(); Refresh();
    }
    public void SetAvailable(bool available, bool compatible)
    { this.available = available; this.compatible = compatible; Refresh(); }
    private void Save() { paths.SaveSettings(settings()); changed(); }
    private void UpdatePackage() => package.Text = $"Descarga completa: {VisualCatalog.DownloadBytes(settings().Visual.IncludeAnimation) / 1e9:0.0} GB (motor y modelos; lo descargado se reutiliza).\nUbicación: {paths.VisualModels}\nCreaciones: {paths.VisualOutputs}";
    private void Refresh()
    {
        var prepared = IsPrepared;
        prepare.IsEnabled = available && compatible && !IsActive;
        prepare.Content = preparing ? "Descargando y preparando…" : !prepared ? "Descargar y preparar" : settings().Visual.IncludeAnimation && !provisioner.IsPrepared(true) ? "Agregar animaciones" : "Comprobar / reparar";
        activate.Visibility = prepared && !IsReady ? Visibility.Visible : Visibility.Collapsed;
        activate.IsEnabled = available && compatible && prepared;
        cancel.Visibility = preparing || generating || loading ? Visibility.Visible : Visibility.Collapsed;
        cancel.Content = preparing ? "Pausar descarga" : "Cancelar";
        setupStatus.Text = preparing ? "Estamos preparando todo. Podés pausar y continuar después." : loading ? "Activando el modo imagen…" : IsReady ? "Todo listo. Ya podés crear o editar." : prepared ? "Descarga terminada. Activá el modo imagen para empezar." : "Descargá lo necesario para crear imágenes en tu equipo.";
        if (prepared && !wasPrepared && !preparing) downloads.IsExpanded = false;
        wasPrepared = prepared;
        generate.IsEnabled = available && compatible && IsReady && (task.SelectedIndex != 1 || reference is not null)
            && (task.SelectedIndex != 2 || provisioner.IsPrepared(true));
        prompt.IsEnabled = available; task.IsEnabled = available;
        for (var index = 0; index < taskTabs.Length; index++) { taskTabs[index].IsEnabled = available; taskTabs[index].Background = new SolidColorBrush(index == task.SelectedIndex ? Color.FromRgb(8, 127, 112) : Color.FromRgb(38, 55, 67)); }
        animation.IsEnabled = license.IsEnabled = available && !IsActive;
        attach.IsEnabled = clear.IsEnabled = gallery.IsEnabled = available;
        size.IsEnabled = available && task.SelectedIndex != 2; duration.IsEnabled = available && task.SelectedIndex == 2;
        seed.IsEnabled = available; steps.IsEnabled = available && task.SelectedIndex == 2;
        size.Visibility = task.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
        duration.Visibility = task.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        open.IsEnabled = export.IsEnabled = resultFile is not null && File.Exists(resultFile);
        generate.Content = task.SelectedIndex switch { 1 => "Aplicar cambio", 2 => "Crear animación", _ => "Crear imagen" };
    }
    public async Task StartAsync(CancellationToken ct)
    {
        var detected = gpu() ?? throw new InvalidOperationException("No se detectó una placa compatible.");
        if (detected.FreeMiB < 11000) throw new InvalidOperationException("El modo visual necesita al menos 11 GB libres para intentar iniciar. Cerrá otras aplicaciones que usen la GPU.");
        loading = true; Refresh(); status("Comprobando modelos e iniciando el modo visual…");
        try { await runtime.StartAsync(settings().Visual, detected, ct); status("Modo visual listo. Escribí una descripción o elegí una referencia."); }
        finally { loading = false; Refresh(); }
    }
    public async Task StopAsync() { await runtime.StopAsync(); Refresh(); }
    public async ValueTask DisposeAsync() => await runtime.DisposeAsync();
    private async Task GenerateAsync()
    {
        generating = true;
        try { await run(async ct =>
        {
            var visual = settings().Visual;
            var (width, height) = size.SelectedIndex switch { 1 => (1024, 768), 2 => (768, 1024), _ => (1024, 1024) };
            var kind = (VisualTask)task.SelectedIndex;
            var frames = duration.SelectedIndex == 1 ? 121 : 73;
            if (kind == VisualTask.Animation) { width = 768; height = 448; }
            if (kind != VisualTask.Animation) { visual.Width = width; visual.Height = height; }
            visual.AnimationFrames = frames; visual.AnimationSteps = steps.SelectedIndex switch { 0 => 10, 2 => 30, _ => 20 }; Save();
            var chosenSeed = (long)RandomNumberGenerator.GetInt32(int.MaxValue);
            if (!string.IsNullOrWhiteSpace(seed.Text) && !long.TryParse(seed.Text, out chosenSeed)) throw new InvalidDataException("La semilla debe ser un número entero positivo o estar vacía.");
            var request = new VisualRequest(kind, prompt.Text.Trim(), width, height, chosenSeed, frames, visual.AnimationSteps);
            var job = await runtime.GenerateAsync(request, kind == VisualTask.Image ? null : reference, new Progress<PreparationProgress>(p => progress(p)), ct);
            LoadGallery();
            var file = job.Files.FirstOrDefault(f => Path.GetExtension(f) == ".png") ?? job.Files.First();
            ShowResult(file);
            if (kind != VisualTask.Animation) { SetReference(file); task.SelectedIndex = 1; }
            status(kind == VisualTask.Animation ? "Animación creada. Podés abrir el MP4 o guardar la versión GIF." : "Imagen creada. Pedí otro cambio o elegí Crear imagen para empezar otra.");
        }); }
        finally { generating = false; LoadGallery(); Refresh(); }
    }
    private void SetReference(string file)
    {
        try
        {
            if (new FileInfo(file).Length > 32_000_000) throw new IOException("La referencia debe ocupar menos de 32 MB.");
            using var stream = File.OpenRead(file); var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames[0].PixelWidth > 8192 || decoder.Frames[0].PixelHeight > 8192) throw new IOException("La referencia supera 8192 píxeles por lado.");
            var frame = decoder.Frames[0];
            size.SelectedIndex = frame.PixelWidth > frame.PixelHeight ? 1 : frame.PixelHeight > frame.PixelWidth ? 2 : 0;
            reference = file; referenceLabel.Text = "Imagen seleccionada: " + Path.GetFileName(file) + ". Escribí qué querés cambiar.";
            resultFile = IsOwnedExistingResult(file) ? file : null; LoadPreview(file); resultTitle.Text = "Imagen seleccionada"; Refresh();
        }
        catch (Exception ex) { status("No se pudo elegir la referencia: " + ex.Message); }
    }
    private bool IsOwnedExistingResult(string file) => StorageCleanup.Within(Path.GetFullPath(file), paths.VisualOutputs) && File.Exists(file);
    private void ShowResult(string file)
    {
        if (!IsOwnedExistingResult(file)) return; resultFile = file; resultTitle.Text = "Tu resultado"; LoadPreview(file); Refresh();
    }
    private void LoadPreview(string file)
    {
        preview.Source = null;
        if (Path.GetExtension(file).ToLowerInvariant() is ".png" or ".gif")
        {
            try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 600; bitmap.UriSource = new(file); bitmap.EndInit(); bitmap.Freeze(); preview.Source = bitmap; }
            catch (Exception ex) { status("El archivo se guardó, pero no se pudo mostrar: " + ex.Message); }
        }
        emptyPreview.Visibility = preview.Source is null ? Visibility.Visible : Visibility.Collapsed;
        emptyPreview.Text = Path.GetExtension(file) == ".mp4" ? "Tu animación está lista.\nPulsá Abrir para reproducirla o Guardar para elegir MP4 o GIF." : "La imagen aparecerá acá";
    }
    private void LoadGallery()
    { var jobs = new VisualJobStore(paths).Read(); gallery.ItemsSource = jobs.Select(j => new GalleryItem(j)).ToArray(); gallery.Visibility = jobs.Length == 0 ? Visibility.Collapsed : Visibility.Visible; emptyGallery.Visibility = jobs.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }
    internal void VerifyInterfaceForTests()
    {
        Directory.CreateDirectory(paths.VisualOutputs);
        var file = Path.Combine(paths.VisualOutputs, "referencia-fixture.png");
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var output = File.Create(file)) encoder.Save(output);
        SetReference(file); task.SelectedIndex = 1;
        if (reference != file || !referenceLabel.Text.Contains("Imagen seleccionada:") || generate.IsEnabled || IsActive || preview.Source is null) throw new Exception("La edición no conserva la referencia/vista previa o inicia procesos.");
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (reference is not null || task.SelectedIndex != 0 || !File.Exists(file)) throw new Exception("Imagen nueva no separa la referencia o borró la creación.");
        preparing = true; Refresh();
        if (cancel.Visibility != Visibility.Visible || !Equals(cancel.Content, "Pausar descarga")) throw new Exception("Falta la pausa accesible en el modo visual.");
        preparing = false; loading = true; Refresh();
        if (cancel.Visibility != Visibility.Visible || !Equals(cancel.Content, "Cancelar")) throw new Exception("Falta la cancelación del arranque visual.");
        loading = false; Refresh();
        if (cancel.Visibility != Visibility.Collapsed || gallery.Visibility != Visibility.Collapsed || emptyGallery.Visibility != Visibility.Visible) throw new Exception("El estado inicial muestra controles o galería vacíos.");
    }
    private void Export()
    {
        if (resultFile is null || !IsOwnedExistingResult(resultFile)) return;
        var selected = (gallery.SelectedItem as GalleryItem)?.Job ?? new VisualJobStore(paths).Read().FirstOrDefault(j => j.Files.Contains(resultFile));
        var files = selected?.Files.Where(IsOwnedExistingResult).ToArray() ?? [resultFile];
        var dialog = new SaveFileDialog { FileName = Path.GetFileName(resultFile), Filter = string.Join('|', files.Select(f => $"{Path.GetExtension(f).TrimStart('.').ToUpperInvariant()}|*{Path.GetExtension(f)}")), AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var source = files[Math.Clamp(dialog.FilterIndex - 1, 0, files.Length - 1)];
            if (!Path.GetExtension(source).Equals(Path.GetExtension(dialog.FileName), StringComparison.OrdinalIgnoreCase)) throw new IOException("La extensión debe coincidir con el formato seleccionado: " + Path.GetExtension(source));
            if (!Path.GetFullPath(source).Equals(Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase)) File.Copy(source, dialog.FileName, true);
            status("Resultado guardado.");
        }
        catch (Exception ex) { status("No se pudo exportar: " + ex.Message); }
    }
    private void ShowLicenses()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "licenses", "visual");
        if (Directory.Exists(folder)) OpenPath(folder);
        else status("Las licencias completas deben estar en licenses/visual junto a Forja. Revisá el paquete antes de aceptar.");
    }
    private static void OpenPath(string file) => Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
    private sealed record GalleryItem(VisualJob Job)
    { public override string ToString() => $"{Job.Created:dd/MM HH:mm} · {Job.Status} · {Job.Request.Prompt[..Math.Min(Job.Request.Prompt.Length, 70)]}"; }
}
