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
    private readonly VisualProvisioner provisioner;
    private readonly VisualRuntime runtime;
    private readonly TextBox prompt = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxLength = 6000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ComboBox task = new() { ItemsSource = new[] { "Crear una imagen nueva", "Editar la imagen seleccionada", "Crear una animación" }, SelectedIndex = 0 };
    private readonly ComboBox size = new() { ItemsSource = new[] { "Cuadrada · 1024 × 1024", "Horizontal · 1024 × 768", "Vertical · 768 × 1024" }, SelectedIndex = 0 };
    private readonly ComboBox duration = new() { ItemsSource = new[] { "3 segundos", "5 segundos" }, SelectedIndex = 0 };
    private readonly TextBox seed = new() { MinWidth = 180, MaxLength = 16 };
    private readonly ComboBox animationMode = new() { ItemsSource = new[] { "Rápido · NVFP4 + Sol (experimental)", "Normal · NVFP4" }, SelectedIndex = 0 };
    private readonly CheckBox imageModel = new() { Content = "HiDream-O1 Full · crear y editar imágenes (principal · 8,1 GB)", IsChecked = true, IsEnabled = false, ToolTip = "Es el generador principal del modo visual y siempre está seleccionado." };
    private readonly CheckBox animation = new() { Content = "Wan 14B · animaciones y GIF (perfil automático por GPU)" };
    private readonly CheckBox license = new() { Content = "Revisé y acepto las licencias del modo visual" };
    private readonly CheckBox enhancePrompt = new() { Content = "Gemma 4 12B · asistente visual (12,1 GB)" };
    private readonly CheckBox reviewPreparedPrompt = new() { Content = "Revisar y editar la descripción preparada antes de generar", FontSize = 12 };
    private readonly Button importModels = new() { Content = "Usar modelos ya descargados", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox imageQuality = new() { ItemsSource = new[] { "Prueba rápida · 1024 píxeles (menos fiel)", "Equilibrada · 1536 píxeles", "Recomendada · 2048 píxeles" }, SelectedIndex = 2 };
    private readonly CheckBox webReferences = new() { Content = "Buscar personajes reconocibles en Wikipedia" };
    private readonly TextBlock animationHint = new() { Foreground = Brushes.LightGray, Margin = new(0, 8, 0, 10), Visibility = Visibility.Collapsed };
    private readonly TextBox webSubject = new() { MaxLength = 100, ToolTip = "Opcional: escribí solo el personaje u objeto, por ejemplo Frieren. Vacío: se intenta detectar en tu descripción." };
    private readonly TextBlock webStatus = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly Button webSource = new() { Content = "Ver origen de la referencia", HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, FontSize = 12 };
    private readonly CheckBox useIdentity = new() { Content = "Usar esta imagen como referencia del personaje" };
    private VisualReference? onlineReference;
    private VisualReference? usedSource;
    private readonly Button prepare = new() { Content = "Preparar imágenes" };
    private readonly Button generate = new() { Content = "Generar", Background = new SolidColorBrush(Color.FromRgb(8, 127, 112)), Foreground = Brushes.White };
    private readonly Button example = new() { Content = "Probar un ejemplo", HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12, Padding = new(0, 8, 0, 8), Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(91, 211, 189)) };
    private readonly Button attach = new() { Content = "Elegir referencia" };
    private readonly Button clear = new() { Content = "Imagen nueva" };
    private readonly Button open = new() { Content = "Abrir resultado" };
    private readonly Button export = new() { Content = "Guardar como…" };
    private readonly TextBlock taskHelp = new() { Foreground = Brushes.LightGray, Margin = new(0, 10, 0, 14) };
    private readonly WrapPanel referenceActions = new();
    private readonly TextBlock creationHint = new() { Foreground = new SolidColorBrush(Color.FromRgb(91, 211, 189)), Margin = new(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Expander onlineOptions = new() { Header = "Opciones de la referencia", Foreground = Brushes.LightGray, Margin = new(0, 8, 0, 10) };
    private readonly TextBlock setupStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    private readonly TextBlock resultTitle = new() { Text = "Tu próxima creación", FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly TextBox preparedDescription = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Expander preparedDetails = new() { Header = "Ver el texto final que recibió HiDream", Foreground = Brushes.LightGray, Visibility = Visibility.Collapsed, Margin = new(0, 12, 0, 0) };
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
    private (int Width, int Height)? referencePixels;
    private string? referenceSubject;
    private string? resultFile;
    private bool available, compatible, preparing, generating, loading, wasSelectedPrepared, animationFormats;
    public bool IsReady => runtime.IsReady;
    public bool IsActive => runtime.HasProcess;
    public bool IsPreparing => preparing;
    public bool IsPrepared => provisioner.IsPrepared(false, gpu(), settings().Visual.EnhancePrompt, settings().Visual.FullEdit, settings().Visual.LargeAssistant);
    private bool AllSelectedPrepared => provisioner.IsPrepared(settings().Visual.IncludeAnimation, gpu(), settings().Visual.EnhancePrompt);
    public string Summary => $"Imágenes y animación · independiente\nComfyUI {VisualCatalog.ComfyVersion}\nCrear y editar: HiDream-O1 Full · FP8\n40 pasos · CFG 5 · DPM++ 2M SDE\nSuavizado de costuras activado\nGemma 4 12B INT8: {(settings().Visual.EnhancePrompt ? (settings().Visual.ReviewPreparedPrompt ? "activado, con revisión previa" : "activado, por etapas") : "desactivado")}\n{(settings().Visual.IncludeAnimation ? gpu()?.IsRtx3090 == true ? "Wan 2.2 A14B · GGUF Q4_K_S · LightX2V · 4 pasos\n" : $"Wan 2.2 A14B · NVFP4 · 4 pasos · {(settings().Visual.AnimationSol ? "Sol experimental" : "Normal")}\n" : "Animaciones: descarga opcional\n")}1 trabajo a la vez\nAsistente y generador se cargan por turnos\nApoyo en RAM según memoria libre\nSolo conexión local protegida\nPerfil unificado: prueba física pendiente";


    public VisualPane(AppPaths paths, Func<AppSettings> settings, HttpClient http, Func<GpuInfo?> gpu,
        Func<Func<CancellationToken, Task>, Task> run, Action<string> status, Action changed, Action<PreparationProgress> progress)
    {
        Foreground = Brushes.White;
        this.paths = paths; this.settings = settings; this.gpu = gpu; this.run = run; this.status = status; this.changed = changed; this.progress = progress;
        provisioner = new(paths, new VerifiedDownloader(http)); runtime = new(paths, http);
        new VisualJobStore(paths).RecoverInterrupted();
        var panel = new StackPanel { Margin = new(24), MaxWidth = 1200, HorizontalAlignment = HorizontalAlignment.Stretch };
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(24, 35, 44)), CornerRadius = new(14), Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        ToolTipService.SetShowOnDisabled(prepare, true);
        var preparation = new StackPanel();
        var details = new StackPanel(); details.Children.Add(package);
        preparation.Children.Add(new TextBlock { Text = "La preparación se hace una sola vez. Después podés crear sin conexión.", Foreground = Brushes.LightGray, Margin = new(0, 0, 0, 10) });
        preparation.Children.Add(imageModel); imageModel.Margin = new(0, 0, 0, 10);
        preparation.Children.Add(enhancePrompt); enhancePrompt.Margin = new(0, 0, 0, 10);
        preparation.Children.Add(animation); animation.Margin = new(0, 0, 0, 12);
        preparation.Children.Add(new TextBlock { Text = "Descarga directa sin cuenta ni token. HiDream incluye su procesamiento de texto e imagen; el asistente es opcional. Los modelos anteriores se conservan.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new(0, 0, 0, 12) });
        details.Children.Add(importModels);
        var licenses = new Button { Content = "Leer licencias", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 10) };
        licenses.Click += (_, _) => ShowLicenses(); preparation.Children.Add(licenses);
        var check = new Button { Content = "Comprobar todo y guardar informe", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 10), FontSize = 12 };
        check.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(paths.Logs);
                var file = Path.Combine(paths.Logs, "comprobacion-visual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(file, provisioner.Report(settings().Visual.IncludeAnimation, gpu(), settings().Visual.EnhancePrompt, settings().Visual.FullEdit, settings().Visual.LargeAssistant));
                status(StartProblem is { Length: > 0 } reason ? reason : "Todo listo para iniciar.");
                OpenPath(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status("No se pudo guardar el informe: " + ex.Message); }
        };
        details.Children.Add(check);
        preparation.Children.Add(license); license.Margin = new(0, 0, 0, 12); license.Content = "Revisé y acepto las licencias de los modelos seleccionados y del motor";
        var downloadOptions = new Expander { Header = "Opciones de descarga y diagnóstico", Foreground = Brushes.LightGray, Content = details, Margin = new(0, 12, 0, 0) };
        downloads.Header = "Modelos y descargas"; downloads.Content = preparation; downloads.IsExpanded = !AllSelectedPrepared || !settings().Visual.HiDreamLicenseAccepted;
        var setup = new StackPanel(); setup.Children.Add(downloads);
        setupStatus.Margin = new(0, 10, 0, 0); setupStatus.FontSize = 12; setup.Children.Add(setupStatus);
        prepare.HorizontalAlignment = HorizontalAlignment.Left; prepare.Margin = new(0, 12, 0, 0); preparation.Children.Add(prepare); preparation.Children.Add(downloadOptions);
        panel.Children.Add(Card(setup));
        panel.Children.Add(workspace);
        workspace.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new() { Width = new(1.35, GridUnitType.Star) });
        workspace.RowDefinitions.Add(new() { Height = GridLength.Auto }); workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var editCard = Card(editor); var resultCard = Card(results); workspace.Children.Add(editCard); workspace.Children.Add(resultCard);
        editor.Children.Add(new TextBlock { Text = "¿Qué querés hacer?", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) });
        task.Visibility = Visibility.Collapsed; editor.Children.Add(task);
        var tabs = new WrapPanel { Margin = new(0, 0, 0, 14) };
        taskTabs = new[] { new Button { Content = "Crear imagen" }, new Button { Content = "Editar imagen" }, new Button { Content = "Animar" } };
        for (var index = 0; index < taskTabs.Length; index++) { var selected = index; taskTabs[index].Padding = new(10, 8, 10, 8); taskTabs[index].FontSize = 12; taskTabs[index].Click += (_, _) => task.SelectedIndex = selected; tabs.Children.Add(taskTabs[index]); }
        editor.Children.Add(tabs);
        editor.Children.Add(taskHelp);
        attach.Content = "Elegir una imagen"; clear.Content = "Quitar referencia"; foreach (var button in new[] { attach, clear }) { button.FontSize = 12; button.Padding = new(10, 7, 10, 7); } referenceActions.Children.Add(attach); referenceActions.Children.Add(clear); editor.Children.Add(referenceActions);
        referenceLabel.Margin = new(0, 6, 0, 10); referenceLabel.FontSize = 12; editor.Children.Add(referenceLabel);
        useIdentity.Margin = new(0, 0, 0, 10); editor.Children.Add(useIdentity);
        editor.Children.Add(new TextBlock { Text = "Tu descripción", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) }); prompt.MinHeight = 100; editor.Children.Add(prompt);
        reviewPreparedPrompt.Margin = new(0, 8, 0, 0); editor.Children.Add(reviewPreparedPrompt);
        generate.HorizontalAlignment = HorizontalAlignment.Left; generate.MinWidth = 180; generate.Margin = new(0, 12, 0, 0); editor.Children.Add(generate); editor.Children.Add(creationHint);
        example.Click += (_, _) => prompt.Text = task.SelectedIndex switch { 1 => "Cambiá solo el brazo izquierdo a rojo. Conservá los demás detalles.", 2 => "El personaje saluda suavemente con la mano. Cámara fija.", _ => "Un pequeño robot blanco en un taller acogedor, iluminación cálida y estilo de ilustración 3D." }; editor.Children.Add(example);
        editor.Children.Add(new TextBlock { Text = "Formato", FontWeight = FontWeights.SemiBold, Margin = new(0, 6, 0, 8) });
        var options = new WrapPanel(); options.Children.Add(size); options.Children.Add(duration); editor.Children.Add(options); editor.Children.Add(imageQuality); imageQuality.Margin = new(0, 8, 0, 0);
        editor.Children.Add(animationHint);
        var advanced = new StackPanel(); advanced.Children.Add(new TextBlock { Text = "Semilla: dejala vacía para variar cada resultado." }); advanced.Children.Add(seed);
        advanced.Children.Add(animationMode);
        animationMode.SelectionChanged += (_, _) => { settings().Visual.AnimationSol = animationMode.SelectedIndex == 0; Save(); };
        editor.Children.Add(new Expander { Header = "Más opciones", Foreground = Brushes.LightGray, Content = advanced, Margin = new(0, 12, 0, 14) });
        editor.Children.Add(webReferences);
        var webOptions = new StackPanel();
        webOptions.Children.Add(new TextBlock { Text = "Personaje u objeto (opcional; vacío = detectar en tu descripción)", FontSize = 12, Margin = new(0, 10, 0, 6) }); webOptions.Children.Add(webSubject);
        webOptions.Children.Add(new TextBlock { Text = "Antes de generar podés revisar la imagen encontrada y reemplazarla por una de tu PC. Se envía solo el nombre a Wikipedia. La búsqueda no analiza caras ni garantiza la identidad. Los estilos generales, como anime de los 90, se describen directamente al modelo. Editar y animar usan la imagen elegida.", FontSize = 11, Foreground = Brushes.LightGray, Margin = new(0, 8, 0, 10) });
        onlineOptions.Content = webOptions; editor.Children.Add(onlineOptions);
        editor.Children.Add(webStatus); editor.Children.Add(webSource);
        webReferences.Click += (_, _) => { settings().Visual.WebReferencesEnabled = webReferences.IsChecked == true; Save(); Refresh(); };
        webSource.Click += (_, _) => { if ((usedSource ?? onlineReference) is { } item && VisualReferenceSearch.AllowedPage(item.PageUrl)) OpenPath(item.PageUrl); };
        results.Children.Add(resultTitle); results.Children.Add(new TextBlock { Text = "Acá podés revisar, abrir y guardar tu creación.", Foreground = Brushes.LightGray, Margin = new(0, 6, 0, 14) });
        var canvas = new Grid { MinHeight = 280, Background = new SolidColorBrush(Color.FromRgb(13, 21, 28)), Margin = new(0, 0, 0, 14) }; preview.Height = 300; canvas.Children.Add(preview); canvas.Children.Add(emptyPreview); results.Children.Add(canvas);
        var actions = new WrapPanel(); actions.Children.Add(open); actions.Children.Add(export);
        open.Content = "Abrir"; export.Content = "Guardar…";
        foreach (var button in new[] { open, export }) { button.FontSize = 12; button.Padding = new(12, 9, 12, 9); }
        var folder = new Button { Content = "Ver carpeta", FontSize = 12, Padding = new(12, 9, 12, 9) }; folder.Click += (_, _) => { Directory.CreateDirectory(paths.VisualOutputs); OpenPath(paths.VisualOutputs); }; actions.Children.Add(folder); results.Children.Add(actions);
        preparedDetails.Content = preparedDescription; results.Children.Add(preparedDetails);
        var history = new StackPanel(); history.Children.Add(new TextBlock { Text = "Tus creaciones", FontSize = 18, FontWeight = FontWeights.SemiBold });
        history.Children.Add(new TextBlock { Text = "Seleccioná una imagen anterior para seguir editándola.", Foreground = Brushes.LightGray, Margin = new(0, 6, 0, 10) }); history.Children.Add(emptyGallery); history.Children.Add(gallery); panel.Children.Add(Card(history));
        gallery.Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)); gallery.Foreground = Brushes.White; gallery.BorderBrush = Brushes.DarkSlateGray;
        gallery.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBox'><Border Background='{TemplateBinding Background}' Padding='8' CornerRadius='8'><ScrollViewer Background='{TemplateBinding Background}' Focusable='False'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate>");
        SizeChanged += (_, _) => { var narrow = ActualWidth < 800; workspace.ColumnDefinitions[1].Width = narrow ? new(0) : new(1.35, GridUnitType.Star); Grid.SetRow(resultCard, narrow ? 1 : 0); Grid.SetColumn(resultCard, narrow ? 0 : 1); editCard.Margin = new(0, 0, narrow ? 0 : 16, narrow ? 16 : 0); };
        panel.Children.Add(new TextBlock { Text = "Animaciones experimentales: GIF y MP4. La primera carga puede tardar; la velocidad depende de tu placa y memoria libre. No se garantiza un bucle continuo.", Margin = new(0, 14, 0, 0), Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        enhancePrompt.Click += (_, _) =>
        {
            settings().Visual.EnhancePrompt = enhancePrompt.IsChecked == true; Save(); UpdatePackage(); Refresh();
            if (enhancePrompt.IsChecked == true && !provisioner.IsPrepared(false, gpu(), true)) status("El asistente Gemma 4 12B necesita 12,1 GB. Pulsá Descargar y preparar antes de iniciar la IA; lo descargado se reutiliza.");
            else if (enhancePrompt.IsChecked != true) status("Asistente desactivado: HiDream recibe tu descripción tal cual y conviene escribirla en inglés. Los archivos descargados se conservan.");
        };
        reviewPreparedPrompt.Click += (_, _) => { settings().Visual.ReviewPreparedPrompt = reviewPreparedPrompt.IsChecked == true; Save(); };
        animation.Click += (_, _) => { settings().Visual.IncludeAnimation = animation.IsChecked == true; Save(); UpdatePackage(); Refresh(); };
        license.Click += (_, _) => { settings().Visual.LicenseAccepted = settings().Visual.HiDreamLicenseAccepted = license.IsChecked == true; Save(); Refresh(); };
        importModels.Click += async (_, _) =>
        {
            if (!available || IsActive) return;
            var picker = new OpenFileDialog { Multiselect = true, Filter = "HiDream Full / Gemma 12B|*.safetensors", Title = "Elegir HiDream Full o Gemma 4 12B" };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            await run(async ct => { await provisioner.ImportModelsAsync(picker.FileNames, new Progress<PreparationProgress>(progress), ct); status("Archivos importados y verificados. Pulsá Descargar y preparar para completar el motor y los componentes faltantes."); }); Refresh();
        };
        task.SelectionChanged += (_, _) => { Refresh(); if (task.SelectedIndex == 2 && !provisioner.IsPrepared(true, gpu(), settings().Visual.EnhancePrompt, false, settings().Visual.LargeAssistant)) status("Para animar, detené la IA, abrí Modelos y descargas y marcá la descarga de Wan. Después pulsá Descargar y preparar."); };
        prepare.Click += async (_, _) =>
        {
            if (!available || !compatible) return;
            if (license.IsChecked != true) { status("Revisá y aceptá las licencias visuales antes de descargar."); return; }
            preparing = true; Refresh();
            try { await run(async ct => { await provisioner.PrepareAsync(settings().Visual, new Progress<PreparationProgress>(progress), ct, gpu()); status("Modelos listos. Pulsá Iniciar IA arriba a la derecha."); }); }
            finally { preparing = false; Refresh(); }
        };
        attach.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg", Title = "Elegir imagen de referencia" };
            if (picker.ShowDialog(Window.GetWindow(this)) == true) { SetReference(picker.FileName); if (task.SelectedIndex == 0 && reference == picker.FileName) useIdentity.IsChecked = true; Refresh(); }
        };
        clear.Click += (_, _) => { reference = null; referencePixels = null; referenceSubject = null; useIdentity.IsChecked = false; ShowOnlineSource(null); referenceLabel.Text = "Sin referencia: se creará una imagen nueva."; task.SelectedIndex = 0; Refresh(); };
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
            ShowUsedSource(item.Job.OnlineReference);
            referenceSubject = item.Job.OnlineReference?.Title;            ShowPreparedDescription(item.Job);
        };
        ReloadSettings(); LoadGallery();
    }
    private static Border Card(UIElement content) => new() { Child = content, Background = new SolidColorBrush(Color.FromRgb(28, 41, 51)), BorderBrush = new SolidColorBrush(Color.FromRgb(42, 59, 71)), BorderThickness = new(1), CornerRadius = new(12), Padding = new(20), Margin = new(0, 0, 0, 16) };
    public void ReloadSettings()
    {
        var visual = settings().Visual;
        enhancePrompt.IsChecked = visual.EnhancePrompt; reviewPreparedPrompt.IsChecked = visual.ReviewPreparedPrompt;
        imageQuality.SelectedIndex = visual.Width > 1536 || visual.Height > 1536 ? 2 : visual.Width > 1024 || visual.Height > 1024 ? 1 : 0;
        animation.IsChecked = visual.IncludeAnimation; license.IsChecked = visual.HiDreamLicenseAccepted;
        webReferences.IsChecked = visual.WebReferencesEnabled;
        size.SelectedIndex = visual.Width > visual.Height ? 1 : visual.Height > visual.Width ? 2 : 0;
        duration.SelectedIndex = visual.AnimationFrames == 121 ? 1 : 0;
        animationMode.SelectedIndex = visual.AnimationSol ? 0 : 1;
        UpdatePackage(); LoadGallery(); Refresh();
    }
    public void SetAvailable(bool available, bool compatible)
    { this.available = available; this.compatible = compatible; Refresh(); }
    private void Save() { paths.SaveSettings(settings()); changed(); }
    /// <summary>Empty when the visual mode can start; otherwise the exact reason it cannot.</summary>
    public string StartProblem => gpu() is null
        ? "No se detectó una placa compatible (RTX 5070 Ti, RTX 5080 o RTX 3090)."
        : !settings().Visual.HiDreamLicenseAccepted ? "Revisá y aceptá las licencias en Modelos y descargas."
        : !AllSelectedPrepared ? MissingDescription(true)
        : gpu()!.FreeMiB < 11000 ? "Necesitás al menos 11 GB de VRAM libres. Cerrá otras aplicaciones que usen la GPU."
        : "";
    /// <summary>Say which file is missing instead of a generic "download the models".</summary>
    private string MissingDescription(bool selected = false)
    {
        if (provisioner.RuntimeMissing) return "Falta el motor visual ComfyUI (2,0 GB comprimidos). Pulsá Descargar y preparar.";
        var missing = provisioner.Missing(selected && settings().Visual.IncludeAnimation, gpu(), settings().Visual.EnhancePrompt, settings().Visual.FullEdit, settings().Visual.LargeAssistant);
        if (missing.Length == 0) return selected && !AllSelectedPrepared ? "Falta preparar los componentes de animación. Pulsá Descargar y preparar." : "Modelos listos. Pulsá Iniciar IA arriba a la derecha.";
        var detail = string.Join(" · ", missing.Select(m => m.Asset.Name + " (" + (m.Asset.Bytes!.Value / 1e9).ToString("0.0") + " GB)"));
        var hint = " Pulsá Descargar y preparar; lo descargado se reutiliza. Podés desactivar el asistente si preferís usar tu descripción directamente.";
        return "Falta descargar: " + detail + "." + hint;
    }
    private void UpdatePackage() => package.Text = $"Descarga completa: {VisualCatalog.DownloadBytes(settings().Visual.IncludeAnimation, gpu(), settings().Visual.EnhancePrompt, settings().Visual.FullEdit, settings().Visual.LargeAssistant) / 1e9:0.0} GB (motor y modelos; lo descargado se reutiliza).\nUbicación: {paths.VisualModels}\nCreaciones: {paths.VisualOutputs}";
    private void Refresh()
    {
        var prepared = IsPrepared;
        ApplyPreparationState(AllSelectedPrepared);
        generate.IsEnabled = available && compatible && IsReady && (task.SelectedIndex != 1 || reference is not null)
            && (task.SelectedIndex != 2 || reference is not null && VisualCatalog.SupportsAnimation(gpu()) && provisioner.IsPrepared(true, gpu(), settings().Visual.EnhancePrompt, false, settings().Visual.LargeAssistant));
        prompt.IsEnabled = example.IsEnabled = available; task.IsEnabled = available;
        for (var index = 0; index < taskTabs.Length; index++) { taskTabs[index].IsEnabled = available; taskTabs[index].Background = new SolidColorBrush(index == task.SelectedIndex ? Color.FromRgb(8, 127, 112) : Color.FromRgb(38, 55, 67)); }
        license.IsEnabled = available && !IsActive; animation.IsEnabled = available && !IsActive && VisualCatalog.SupportsAnimation(gpu());
        enhancePrompt.IsEnabled = importModels.IsEnabled = available && !IsActive;
        reviewPreparedPrompt.IsEnabled = available && settings().Visual.EnhancePrompt;
        reviewPreparedPrompt.Visibility = settings().Visual.EnhancePrompt ? Visibility.Visible : Visibility.Collapsed;
        attach.IsEnabled = clear.IsEnabled = gallery.IsEnabled = useIdentity.IsEnabled = available;
        imageQuality.IsEnabled = available && task.SelectedIndex != 2; imageQuality.Visibility = task.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
        size.IsEnabled = available; duration.IsEnabled = available && task.SelectedIndex == 2;
        seed.IsEnabled = available; animationMode.IsEnabled = available && gpu()?.IsRtx3090 != true; animationMode.Visibility = task.SelectedIndex == 2 && gpu()?.IsRtx3090 != true ? Visibility.Visible : Visibility.Collapsed;
        webReferences.IsEnabled = available && task.SelectedIndex == 0;
        webReferences.Visibility = onlineOptions.Visibility = task.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        webSubject.IsEnabled = available && task.SelectedIndex == 0 && webReferences.IsChecked == true;
        if (animationFormats != (task.SelectedIndex == 2))
        {
            var format = Math.Max(0, size.SelectedIndex); animationFormats = task.SelectedIndex == 2;
            size.ItemsSource = animationFormats ? new[] { "Cuadrada · 576 × 576", "Horizontal · 768 × 448", "Vertical · 448 × 768" } : new[] { "Cuadrada · 1024 × 1024", "Horizontal · 1024 × 768", "Vertical · 768 × 1024" };
            size.SelectedIndex = format;
        }
        animationHint.Visibility = task.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        animationHint.Text = reference is null ? "Wan 14B necesita una imagen inicial. Elegí una de tu PC o creala primero en Crear imagen. Forja elige NVFP4 para RTX 50 y GGUF para RTX 3090." : "La imagen será el primer fotograma y se ajustará sin recortarla. Describí qué se desplaza, hacia dónde y cómo se mueve la cámara. El final no tiene por qué coincidir con el inicio del GIF. La opción de 5 segundos es experimental y requiere más memoria.";
        duration.Visibility = task.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        open.IsEnabled = export.IsEnabled = resultFile is not null && File.Exists(resultFile);
        generate.Content = task.SelectedIndex switch { 1 => "Aplicar cambio", 2 => "Crear animación", _ => "Crear imagen" };
        taskHelp.Text = task.SelectedIndex switch { 1 => "Elegí una imagen y contá qué detalle querés cambiar. El resultado conserva su encuadre, en una resolución entrenada.", 2 => "Elegí una imagen y describí su movimiento. Wan 14B · 4 pasos. En RTX 50 podés cambiar Normal / Sol en Más opciones; RTX 3090 usa GGUF.", _ => "Describí la escena que imaginás." };
        referenceActions.Visibility = Visibility.Visible;
        useIdentity.Visibility = task.SelectedIndex == 0 && reference is not null ? Visibility.Visible : Visibility.Collapsed;
        clear.Visibility = reference is null ? Visibility.Collapsed : Visibility.Visible;
        referenceLabel.Visibility = reference is not null || task.SelectedIndex != 0 ? Visibility.Visible : Visibility.Collapsed;
        referenceLabel.Text = reference is null ? "Elegí una imagen para usarla como punto de partida." : task.SelectedIndex == 0 ? "Imagen disponible. Marcá la opción de referencia para usar su personaje en una escena nueva." : IsOwnedExistingResult(reference) ? "Imagen seleccionada de tus creaciones. Escribí qué querés cambiar." : "Imagen seleccionada: " + Path.GetFileName(reference);
        creationHint.Text = generating ? "Creando… Podés cancelar desde el botón de arriba." : !prepared ? "Descargá los modelos en Modelos y descargas para empezar." : !IsReady ? "Pulsá Iniciar IA arriba a la derecha para crear." : task.SelectedIndex == 1 && reference is null ? "Primero elegí la imagen que querés editar." : task.SelectedIndex == 2 && !VisualCatalog.SupportsAnimation(gpu()) ? "No se detectó una placa compatible con animación." : task.SelectedIndex == 2 && reference is null ? "Elegí una imagen inicial para animar." : task.SelectedIndex == 2 && !provisioner.IsPrepared(true, gpu(), settings().Visual.EnhancePrompt, false, settings().Visual.LargeAssistant) ? "Agregá el modelo de animación en Modelos y descargas." : "";
        creationHint.Visibility = string.IsNullOrEmpty(creationHint.Text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ApplyPreparationState(bool selectedPrepared)
    {
        prepare.IsEnabled = available && compatible && !IsActive && !preparing && settings().Visual.HiDreamLicenseAccepted;
        prepare.Background = new SolidColorBrush(prepare.IsEnabled && !selectedPrepared ? Color.FromRgb(8, 127, 112) : Color.FromRgb(38, 55, 67));
        prepare.Foreground = Brushes.White;
        prepare.Content = preparing ? "Descargando y preparando…" : selectedPrepared ? "Comprobar / reparar" : "Descargar y preparar";
        prepare.ToolTip = !settings().Visual.HiDreamLicenseAccepted ? "Marcá Revisé y acepto para habilitar la preparación." : null;
        var complete = selectedPrepared && settings().Visual.HiDreamLicenseAccepted;
        downloads.Header = complete ? "Modelos y descargas · todo listo" : selectedPrepared ? "Modelos y descargas · aceptar licencias" : "Modelos y descargas";
        setupStatus.Text = preparing ? "Descargando. Podés pausar desde el botón de arriba y continuar después."
            : loading ? "Iniciando la IA…"
            : !settings().Visual.HiDreamLicenseAccepted ? "Revisá las licencias y marcá Revisé y acepto para continuar."
            : selectedPrepared ? "Todo listo. Pulsá Iniciar IA arriba a la derecha."
            : "Listo para descargar. Pulsá Descargar y preparar.";
        setupStatus.Visibility = complete && !preparing && !loading ? Visibility.Collapsed : Visibility.Visible;
        if (!preparing)
        {
            if (complete && !wasSelectedPrepared) downloads.IsExpanded = false;
            if (!complete && wasSelectedPrepared) downloads.IsExpanded = true;
            wasSelectedPrepared = complete;
        }
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
            var side = imageQuality.SelectedIndex switch { 1 => 1536, 2 => 2048, _ => 1024 };
            var (width, height) = size.SelectedIndex switch { 1 => (side, side * 3 / 4), 2 => (side * 3 / 4, side), _ => (side, side) };
            var kind = (VisualTask)task.SelectedIndex;
            var frames = duration.SelectedIndex == 1 ? 121 : 73;
            if (kind == VisualTask.Animation) (width, height) = VisualAnimation.Dimensions(size.SelectedIndex);
            if (kind != VisualTask.Animation) { visual.Width = width; visual.Height = height; }
            visual.AnimationFrames = frames; visual.AnimationSteps = 4; Save();
            var chosenSeed = (long)RandomNumberGenerator.GetInt32(int.MaxValue);
            if (!string.IsNullOrWhiteSpace(seed.Text) && !long.TryParse(seed.Text, out chosenSeed)) throw new InvalidDataException("La semilla debe ser un número entero positivo o estar vacía.");
            var request = new VisualRequest(kind, prompt.Text.Trim(), width, height, chosenSeed, frames, visual.AnimationSteps, false, visual.EnhancePrompt, kind != VisualTask.Animation, true, gpu()?.IsRtx3090 != true && visual.AnimationSol, gpu()?.IsRtx3090 == true);
            request.Validate();
            var chosenReference = kind == VisualTask.Image && useIdentity.IsChecked != true ? null : reference;
            VisualReference? discovered = chosenReference is null ? null : onlineReference;
            if (kind == VisualTask.Image && chosenReference is null && visual.WebReferencesEnabled)
            {
                var explicitSearch = !string.IsNullOrWhiteSpace(webSubject.Text);
                var subject = explicitSearch ? webSubject.Text.Trim() : VisualReferenceSearch.AutomaticSubject(request.Prompt);
                if (subject is null) { ShowOnlineSource(null); webStatus.Text = "Concepto o estilo general: se usa tu descripción, sin elegir una obra concreta."; }
                else
                {
                status("Buscando referencia: " + subject + "…"); webStatus.Text = "Buscando en Wikipedia: " + subject; onlineReference = null; webSource.Visibility = Visibility.Collapsed;
                try
                {
                    using var search = new VisualReferenceSearch();
                    var choices = await search.SearchAsync(subject, ct);
                    var matched = VisualReferenceSearch.AutomaticMatch(choices, subject);
                    if (matched is null && !explicitSearch) webStatus.Text = "Sin una coincidencia clara: se generará tu idea sin imponer una referencia. Podés elegir una imagen de tu PC.";
                    else
                    {
                    var ordered = matched is null ? choices : choices.OrderByDescending(item => item == matched).ToArray();
                    var selection = VisualReferencePicker.Show(Window.GetWindow(this), ordered, search, paths.VisualInputs, ct);
                    ct.ThrowIfCancellationRequested();
                    if (selection is null) { status("Generación cancelada antes de usar la GPU."); return; }
                    chosenReference = selection.File; discovered = selection.Source;
                    if (chosenReference is not null) { referencePixels = ReadPixels(chosenReference) ?? referencePixels; LoadPreview(chosenReference); resultTitle.Text = "Referencia elegida"; }
                    ShowOnlineSource(discovered);
                    if (discovered is null) webStatus.Text = chosenReference is null ? "Elegiste crear sin referencia." : "Se utiliza la referencia de tu PC, sin subirla a internet.";
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or NotSupportedException or System.IO.FileFormatException)
                {
                    chosenReference = null; discovered = null; ShowOnlineSource(null);
                    if (explicitSearch) { webStatus.Text = "No se pudo obtener la referencia solicitada. Corregí el nombre o borrá el término para crear sin ella."; status(webStatus.Text + " " + ex.Message); return; }
                    webStatus.Text = "La búsqueda no estuvo disponible. Se usa tu descripción sin referencia en línea.";
                }
                }
            }
            else if (kind == VisualTask.Image) { if (chosenReference is null) ShowOnlineSource(null); webStatus.Text = chosenReference is null ? "Creación sin búsqueda en internet." : "Se usa la imagen seleccionada como referencia del personaje."; }
            var lookupNote = webStatus.Text;
            // The assistant needs the subject's name to resolve requests like "add her anime companions".
            var contextTitle = discovered?.Title ?? (chosenReference is not null ? referenceSubject : null);
            // A single reference defines the framing, as HiDream's official workflow does: the model
            // copies a wide reference (a series cover, a landscape) when the target is padded to another shape.
            if (kind != VisualTask.Animation && chosenReference is not null && referencePixels is { } frame)
            { (width, height) = VisualSizes.ForReference(frame.Width, frame.Height); request = request with { Width = width, Height = height }; request.Validate(); }            PreparedPrompt? prepared = null;
            if (visual.EnhancePrompt)
            {
                status(kind == VisualTask.Animation ? "Preparando la descripción del movimiento con el asistente visual…" : "Preparando la descripción con el asistente visual…");
                var current = await runtime.PreparePromptAsync(request, chosenReference, new Progress<PreparationProgress>(p => progress(p)), ct, discovered, contextTitle);
                if (visual.ReviewPreparedPrompt)
                {
                    var reviewed = PreparedPromptWindow.Show(Window.GetWindow(this), current.Text, request.Prompt, chosenReference is not null,
                        async token => (current = await runtime.PreparePromptAsync(request, chosenReference, new Progress<PreparationProgress>(p => progress(p)), token, discovered, contextTitle)).Text, ct);
                    if (reviewed is null) { status("Generación cancelada. Tu pedido sigue en el cuadro de texto."); return; }
                    current = current with { Text = reviewed };
                }
                prepared = current;
            }
            status(kind == VisualTask.Animation ? "Preparando la animación; la primera carga puede tardar…" : "Preparando tu imagen…");
            var job = await runtime.GenerateAsync(request, chosenReference, new Progress<PreparationProgress>(p => progress(p)), ct, discovered, prepared, contextTitle);
            LoadGallery();
            var file = job.Files.FirstOrDefault(f => Path.GetExtension(f) == ".png") ?? job.Files.First();
            ShowResult(file);
            ShowPreparedDescription(job);
            if (kind != VisualTask.Animation) { SetReference(file); task.SelectedIndex = 1; }
            // Display only: the chosen image is now a local file, so the online source must not
            // become the metadata of the next reference.
            ShowUsedSource(job.OnlineReference);
            if (kind == VisualTask.Image && job.OnlineReference is null && visual.WebReferencesEnabled) webStatus.Text = lookupNote;
            var referenceSummary = kind == VisualTask.Image && visual.WebReferencesEnabled ? job.OnlineReference is { } source ? " Referencia utilizada: " + source.Title + "." : " " + lookupNote : "";
            status((kind == VisualTask.Animation ? "Animación creada. Podés abrir el MP4 o guardar la versión GIF." : "Imagen creada. Pedí otro cambio o elegí Crear imagen para empezar otra.") + (job.TotalSeconds is { } seconds ? $" Tiempo del motor: {seconds:0.0} s." : "") + referenceSummary);
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
            referencePixels = (frame.PixelWidth, frame.PixelHeight);
            ShowOnlineSource(null);
            reference = file; useIdentity.IsChecked = false; referenceLabel.Text = "Imagen seleccionada: " + Path.GetFileName(file) + ". Escribí qué querés cambiar.";
            // The result may come from a generation whose reference identified the subject; the assistant
            // needs that name to resolve requests like "add her anime companions".
            referenceSubject = IsOwnedExistingResult(file) ? new VisualJobStore(paths).Read().FirstOrDefault(j => j.Files.Contains(file, StringComparer.OrdinalIgnoreCase))?.OnlineReference?.Title : null;
            if ((long)frame.PixelWidth * frame.PixelHeight < 1_700_000) status("Imagen pequeña para editar (" + Math.Round(frame.PixelWidth * (double)frame.PixelHeight / 1e6, 1) + " MP): la edición rinde mejor desde 1,7 MP. Conviene generarla antes y editarla a mayor tamaño.");
            resultFile = IsOwnedExistingResult(file) ? file : null; LoadPreview(file); resultTitle.Text = "Imagen seleccionada"; Refresh();
        }
        catch (Exception ex) { status("No se pudo elegir la referencia: " + ex.Message); }
    }
    private bool IsOwnedExistingResult(string file) => StorageCleanup.Within(Path.GetFullPath(file), paths.VisualOutputs) && File.Exists(file);
    private static (int Width, int Height)? ReadPixels(string file)
    {
        try
        {
            using var stream = File.OpenRead(file); var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or System.IO.FileFormatException) { return null; }
    }
    private void ShowOnlineSource(VisualReference? source)
    { usedSource = source; onlineReference = source; webStatus.Text = source is null ? "" : "Referencia original: " + source.Title + " · Wikipedia"; webSource.Visibility = source is not null ? Visibility.Visible : Visibility.Collapsed; }
    // Keeps the source visible in the result without turning it into the current reference metadata.
    private void ShowUsedSource(VisualReference? source)
    { usedSource = source; webStatus.Text = source is null ? "" : "Referencia original usada: " + source.Title + " · Wikipedia"; webSource.Visibility = source is not null ? Visibility.Visible : Visibility.Collapsed; }
    private void ShowResult(string file)
    {
        if (!IsOwnedExistingResult(file)) return; resultFile = file; resultTitle.Text = "Tu resultado"; LoadPreview(file); Refresh();
    }
    private void ShowPreparedDescription(VisualJob job)
    { preparedDescription.Text = job.PreparedPrompt ?? ""; preparedDetails.Visibility = string.IsNullOrWhiteSpace(job.PreparedPrompt) ? Visibility.Collapsed : Visibility.Visible; }
    private void LoadPreview(string file)
    {
        preview.Source = null;
        if (Path.GetExtension(file).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif")
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
        var oldConsent = settings().Visual.HiDreamLicenseAccepted;
        try
        {
            if (imageModel.IsChecked != true || enhancePrompt.IsChecked != settings().Visual.EnhancePrompt || animation.IsChecked != settings().Visual.IncludeAnimation)
                throw new Exception("La lista de modelos no muestra HiDream, Gemma y Wan seleccionados según el perfil.");
            settings().Visual.HiDreamLicenseAccepted = false; license.IsChecked = false; Refresh();
            if (prepare.IsEnabled || ((SolidColorBrush)prepare.Background).Color != Color.FromRgb(38, 55, 67))
                throw new Exception("La descarga se habilita o destaca sin aceptar licencias.");
            ApplyPreparationState(true);
            if (!downloads.IsExpanded || setupStatus.Visibility != Visibility.Visible) throw new Exception("Se ocultan las licencias pendientes aunque los archivos estén descargados.");
            license.IsChecked = true; license.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            if (!prepare.IsEnabled || ((SolidColorBrush)prepare.Background).Color != Color.FromRgb(8, 127, 112))
                throw new Exception("Aceptar las licencias no habilita y destaca la descarga.");
            preparing = true; ApplyPreparationState(true); preparing = false; ApplyPreparationState(true);
            if (downloads.IsExpanded || !downloads.Header.ToString()!.Contains("todo listo"))
                throw new Exception("La sección no se repliega al completar los modelos elegidos.");
            downloads.IsExpanded = true; ApplyPreparationState(true);
            if (!downloads.IsExpanded) throw new Exception("No se pueden volver a abrir las opciones una vez preparado.");
            ApplyPreparationState(false);
            if (!downloads.IsExpanded || !prepare.IsEnabled || !Equals(prepare.Content, "Descargar y preparar"))
                throw new Exception("Faltar un modelo seleccionado no vuelve a mostrar la preparación.");
            available = false; ApplyPreparationState(false);
            if (prepare.IsEnabled) throw new Exception("La preparación queda habilitada durante otra operación.");
        }
        finally
        {
            settings().Visual.HiDreamLicenseAccepted = oldConsent; license.IsChecked = oldConsent;
            available = true; wasSelectedPrepared = false; downloads.IsExpanded = !AllSelectedPrepared || !settings().Visual.HiDreamLicenseAccepted; Save(); Refresh();
        }
        Directory.CreateDirectory(paths.VisualOutputs);
        var file = Path.Combine(paths.VisualOutputs, "referencia-fixture.png");
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var output = File.Create(file)) encoder.Save(output);
        SetReference(file); task.SelectedIndex = 1;
        if (reference != file || !referenceLabel.Text.Contains("Imagen seleccionada") || generate.IsEnabled || IsActive || preview.Source is null) throw new Exception("La edición no conserva la referencia/vista previa o inicia procesos.");
        task.SelectedIndex = 0; Refresh();
        if (useIdentity.IsChecked == true || useIdentity.Visibility != Visibility.Visible || referenceActions.Visibility != Visibility.Visible) throw new Exception("La escena nueva hereda la identidad sin elegirla o no permite elegir una referencia.");
        VisualReferencePicker.VerifyForTests(Window.GetWindow(this), file, paths.VisualInputs);
        PreparedPromptWindow.VerifyForTests(Window.GetWindow(this));
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (reference is not null || task.SelectedIndex != 0 || !File.Exists(file)) throw new Exception("Imagen nueva no separa la referencia o borró la creación.");
        if (gallery.Visibility != Visibility.Collapsed || emptyGallery.Visibility != Visibility.Visible || creationHint.Visibility != Visibility.Visible) throw new Exception("El estado inicial no explica cómo empezar.");
        task.SelectedIndex = 2; size.SelectedIndex = 2; Refresh();
        if (!animationFormats || !size.IsEnabled || !size.Items[2].ToString()!.Contains("448 × 768") || animationHint.Visibility != Visibility.Visible) throw new Exception("La orientación de la animación no se puede elegir.");
        if (reviewPreparedPrompt.Visibility != (settings().Visual.EnhancePrompt ? Visibility.Visible : Visibility.Collapsed)) throw new Exception("La revisión de la descripción no sigue al asistente al animar.");
        task.SelectedIndex = 0; Refresh();
        if (animationFormats || !size.Items[2].ToString()!.Contains("768 × 1024")) throw new Exception("La animación alteró los formatos de imagen.");
        if (reviewPreparedPrompt.Visibility != (settings().Visual.EnhancePrompt ? Visibility.Visible : Visibility.Collapsed)) throw new Exception("La revisión de la descripción no sigue al asistente.");
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
