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
    private readonly ComboBox steps = new() { ItemsSource = new[] { "10 pasos · más rápido", "20 pasos · inicial", "30 pasos · más lento" }, SelectedIndex = 1 };
    private readonly CheckBox animation = new() { Content = "Descargar también Wan para animaciones y GIF" };
    private readonly CheckBox license = new() { Content = "Revisé y acepto las licencias del modo visual" };
    private readonly Button prepare = new() { Content = "Preparar imágenes" };
    private readonly Button generate = new() { Content = "Generar", Background = new SolidColorBrush(Color.FromRgb(8, 127, 112)), Foreground = Brushes.White };
    private readonly Button attach = new() { Content = "Elegir referencia" };
    private readonly Button clear = new() { Content = "Imagen nueva" };
    private readonly Button open = new() { Content = "Abrir resultado" };
    private readonly Button export = new() { Content = "Guardar como…" };
    private readonly TextBlock referenceLabel = new() { Text = "Sin referencia: se creará una imagen nueva.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    private readonly TextBlock package = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    private readonly Image preview = new() { Height = 220, Stretch = Stretch.Uniform };
    private readonly ListBox gallery = new() { Height = 145 };
    private string? reference;
    private string? resultFile;
    private bool available, compatible, preparing;
    public bool IsReady => runtime.IsReady;
    public bool IsActive => runtime.HasProcess;
    public bool IsPreparing => preparing;
    public bool IsPrepared => provisioner.IsPrepared(false);
    public string Summary => $"Imágenes y animación · independiente\nComfyUI {VisualCatalog.ComfyVersion}\nFLUX.2 Klein 4B · BF16 · 4 pasos\nCodificador interno Qwen 4B\n{(settings().Visual.IncludeAnimation ? "Wan 2.2 5B · FP16 · codificador FP8\n" : "Animaciones: descarga opcional\n")}1 imagen / trabajo a la vez\nTransferencia de componentes a RAM\nSin Qwen de programación ni OpenCode\nSolo conexión local protegida\nPruebas de inferencia: pendientes";

    public VisualPane(AppPaths paths, Func<AppSettings> settings, HttpClient http, Func<GpuInfo?> gpu,
        Func<Func<CancellationToken, Task>, Task> run, Action<string> status, Action changed, Action<PreparationProgress> progress)
    {
        Foreground = Brushes.White;
        this.paths = paths; this.settings = settings; this.gpu = gpu; this.run = run; this.status = status; this.changed = changed; this.progress = progress;
        provisioner = new(paths, new VerifiedDownloader(http)); runtime = new(paths, http);
        new VisualJobStore(paths).RecoverInterrupted();
        var panel = new StackPanel { Margin = new(24) };
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(24, 35, 44)), CornerRadius = new(14), Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        panel.Children.Add(new TextBlock { Text = "Creá, cambiá y animá tus imágenes", FontSize = 25, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Describí lo que querés. Para cambiar un detalle, seleccioná una imagen y escribí el cambio. Todo se ejecuta en tu equipo.", Margin = new(0, 8, 0, 14), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        var preparation = new StackPanel(); preparation.Children.Add(package); preparation.Children.Add(animation); preparation.Children.Add(license);
        var licenses = new Button { Content = "Ver licencias y componentes visuales" };
        licenses.Click += (_, _) => ShowLicenses(); preparation.Children.Add(licenses); preparation.Children.Add(prepare);
        panel.Children.Add(new Expander { Header = "Descargas y preparación", Foreground = Brushes.White, IsExpanded = !IsPrepared, Content = preparation, Margin = new(0, 0, 0, 12) });
        panel.Children.Add(task); panel.Children.Add(new TextBlock { Text = "Describí la imagen o el cambio que querés hacer", Margin = new(0, 10, 0, 6), Foreground = Brushes.LightGray }); panel.Children.Add(prompt);
        var options = new WrapPanel(); options.Children.Add(size); options.Children.Add(duration); panel.Children.Add(options);
        var advanced = new StackPanel(); advanced.Children.Add(new TextBlock { Text = "Semilla: dejala vacía para variar cada resultado." }); advanced.Children.Add(seed);
        advanced.Children.Add(new TextBlock { Text = "Pasos de animación (no cambia los cuatro pasos de imágenes)." }); advanced.Children.Add(steps);
        panel.Children.Add(new Expander { Header = "Configuración avanzada visual", Foreground = Brushes.White, Content = advanced, Margin = new(0, 8, 0, 8) });
        var references = new WrapPanel(); references.Children.Add(attach); references.Children.Add(clear); panel.Children.Add(references);
        panel.Children.Add(referenceLabel); panel.Children.Add(generate); panel.Children.Add(preview);
        var actions = new WrapPanel(); actions.Children.Add(open); actions.Children.Add(export);
        var folder = new Button { Content = "Carpeta de creaciones" }; folder.Click += (_, _) => { Directory.CreateDirectory(paths.VisualOutputs); OpenPath(paths.VisualOutputs); }; actions.Children.Add(folder); panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = "Tus creaciones · seleccioná un resultado para editarlo", Margin = new(0, 12, 0, 8), FontWeight = FontWeights.SemiBold }); panel.Children.Add(gallery);
        gallery.Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)); gallery.Foreground = Brushes.White; gallery.BorderBrush = Brushes.DarkSlateGray;
        panel.Children.Add(new TextBlock { Text = "Animaciones experimentales: GIF y MP4. La primera carga puede tardar; la velocidad depende de tu placa y memoria libre. No se garantiza un bucle continuo.", Margin = new(0, 14, 0, 0), Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        animation.Click += (_, _) => { settings().Visual.IncludeAnimation = animation.IsChecked == true; Save(); UpdatePackage(); };
        license.Click += (_, _) => { settings().Visual.LicenseAccepted = license.IsChecked == true; Save(); };
        task.SelectionChanged += (_, _) => { Refresh(); if (task.SelectedIndex == 2 && !provisioner.IsPrepared(true)) status("Para animar, detené la IA, marcá la descarga opcional de Wan y pulsá Preparar / reanudar imágenes."); };
        prepare.Click += async (_, _) =>
        {
            if (!available || !compatible) return;
            if (license.IsChecked != true) { status("Revisá y aceptá las licencias visuales antes de descargar."); return; }
            preparing = true;
            try { await run(async ct => { await provisioner.PrepareAsync(settings().Visual, new Progress<PreparationProgress>(progress), ct); status("Modo visual preparado. Pulsá Iniciar IA; no necesita proyecto Git."); }); }
            finally { preparing = false; Refresh(); }
        };
        attach.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg", Title = "Elegir imagen de referencia" };
            if (picker.ShowDialog(Window.GetWindow(this)) == true) { SetReference(picker.FileName); task.SelectedIndex = 1; }
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
        prepare.IsEnabled = available && compatible && !IsActive;
        prepare.Content = preparing ? "Preparando…" : "Preparar / reanudar imágenes";
        generate.IsEnabled = available && compatible && IsReady && (task.SelectedIndex != 1 || reference is not null)
            && (task.SelectedIndex != 2 || provisioner.IsPrepared(true));
        prompt.IsEnabled = available; task.IsEnabled = available;
        animation.IsEnabled = license.IsEnabled = available && !IsActive;
        attach.IsEnabled = clear.IsEnabled = gallery.IsEnabled = available;
        size.IsEnabled = available && task.SelectedIndex != 2; duration.IsEnabled = available && task.SelectedIndex == 2;
        seed.IsEnabled = available; steps.IsEnabled = available && task.SelectedIndex == 2;
        size.Visibility = task.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
        duration.Visibility = task.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        open.IsEnabled = export.IsEnabled = resultFile is not null && File.Exists(resultFile);
    }
    public async Task StartAsync(CancellationToken ct)
    {
        var detected = gpu() ?? throw new InvalidOperationException("No se detectó una placa compatible.");
        if (detected.FreeMiB < 11000) throw new InvalidOperationException("El modo visual necesita al menos 11 GB libres para intentar iniciar. Cerrá otras aplicaciones que usen la GPU.");
        status("Comprobando modelos e iniciando el modo visual…");
        await runtime.StartAsync(settings().Visual, detected, ct); status("Modo visual listo. Escribí una descripción o elegí una referencia."); Refresh();
    }
    public async Task StopAsync() { await runtime.StopAsync(); Refresh(); }
    public async ValueTask DisposeAsync() => await runtime.DisposeAsync();
    private async Task GenerateAsync()
    {
        await run(async ct =>
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
            status(kind == VisualTask.Animation ? "Animación creada. Podés abrir el MP4 o guardar la versión GIF." : "Imagen creada. Pedí otro cambio o elegí Imagen nueva.");
        });
        LoadGallery(); Refresh();
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
            reference = file; referenceLabel.Text = "Referencia: " + Path.GetFileName(file) + ". El próximo cambio se aplicará sobre esta imagen, ajustada a la resolución seleccionada."; Refresh();
        }
        catch (Exception ex) { status("No se pudo elegir la referencia: " + ex.Message); }
    }
    private bool IsOwnedExistingResult(string file) => StorageCleanup.Within(Path.GetFullPath(file), paths.VisualOutputs) && File.Exists(file);
    private void ShowResult(string file)
    {
        if (!IsOwnedExistingResult(file)) return; resultFile = file; preview.Source = null;
        if (Path.GetExtension(file).ToLowerInvariant() is ".png" or ".gif")
        {
            try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 600; bitmap.UriSource = new(file); bitmap.EndInit(); bitmap.Freeze(); preview.Source = bitmap; }
            catch (Exception ex) { status("El archivo se guardó, pero no se pudo mostrar: " + ex.Message); }
        }
        Refresh();
    }
    private void LoadGallery() => gallery.ItemsSource = new VisualJobStore(paths).Read().Select(j => new GalleryItem(j)).ToArray();
    internal void VerifyInterfaceForTests()
    {
        Directory.CreateDirectory(paths.VisualOutputs);
        var file = Path.Combine(paths.VisualOutputs, "referencia-fixture.png");
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var output = File.Create(file)) encoder.Save(output);
        SetReference(file); task.SelectedIndex = 1;
        if (reference != file || !referenceLabel.Text.Contains("Referencia:") || generate.IsEnabled || IsActive) throw new Exception("La edición no conserva la referencia o inicia procesos.");
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (reference is not null || task.SelectedIndex != 0 || !File.Exists(file)) throw new Exception("Imagen nueva no separa la referencia o borró la creación.");
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
