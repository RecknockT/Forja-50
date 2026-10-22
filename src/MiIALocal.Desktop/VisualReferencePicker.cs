using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MiIALocal.Core;

namespace MiIALocal.Desktop;

internal sealed record ReferenceSelection(string? File, VisualReference? Source);

/// <summary>Review the actual bounded download before it can condition a generation.</summary>
internal static class VisualReferencePicker
{
    internal static void VerifyForTests(Window owner, string fixture, string folder)
    {
        using var search = new VisualReferenceSearch(new FixtureHandler(fixture));
        var source = new VisualReference("Fixture", "https://en.wikipedia.org/wiki/Fixture", "https://upload.wikimedia.org/fixture.png", "Fixture");
        Exception? failure = null; var ticks = 0;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var dialog = owner.OwnedWindows.Cast<Window>().FirstOrDefault(w => w.Title == "Referencia del personaje");
            if (dialog is null) return;
            var button = Descendants(dialog).OfType<Button>().FirstOrDefault(b => Equals(b.Content, "Usar esta referencia"));
            if (button?.IsEnabled == true) { timer.Stop(); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            else if (++ticks > 60) { failure = new Exception("No se pudo revisar la imagen antes de generar."); timer.Stop(); dialog.Close(); }
        };
        timer.Start(); ReferenceSelection? selected;
        try { selected = Show(owner, [source], search, folder, default); } finally { timer.Stop(); }
        if (failure is not null) throw failure;
        if (selected?.Source != source || selected.File is null || !File.Exists(selected.File)) throw new Exception("La revisión perdió la imagen o su origen.");
        using var cancel = new CancellationTokenSource();
        var cancelTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        cancelTimer.Tick += (_, _) => { cancelTimer.Stop(); cancel.Cancel(); }; cancelTimer.Start();
        try { if (Show(owner, [], search, folder, cancel.Token) is not null) throw new Exception("Cancelar la revisión eligió una referencia."); }
        finally { cancelTimer.Stop(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private sealed class FixtureHandler(string file) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(file)) };
            response.Content.Headers.ContentType = new("image/png"); return Task.FromResult(response);
        }
    }
    internal static BitmapImage ReadImage(string file)
    {
        if (new FileInfo(file).Length > 32_000_000) throw new IOException("Elegí una imagen de hasta 32 MB.");
        using (var input = File.OpenRead(file))
        {
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames[0].PixelWidth > 8192 || decoder.Frames[0].PixelHeight > 8192)
                throw new InvalidDataException("La referencia supera 8192 píxeles por lado.");
        }
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 600; image.UriSource = new(Path.GetFullPath(file)); image.EndInit(); image.Freeze(); return image;
    }

    internal static ReferenceSelection? Show(Window owner, VisualReference[] choices, VisualReferenceSearch search, string folder, CancellationToken ct)
    {
        var body = new StackPanel { Margin = new(22) };
        body.Children.Add(new TextBlock { Text = "Revisá la referencia antes de crear", FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Buscá una cara clara, vestimenta reconocible y pocos personajes. Una portada o un logo puede dar un mal resultado.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 12), Foreground = Brushes.LightGray });
        var choice = new ComboBox { ItemsSource = choices, Margin = new(0, 0, 0, 12) }; body.Children.Add(choice);
        var preview = new Image { Height = 290, Stretch = Stretch.Uniform }; body.Children.Add(preview);
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 12), Foreground = Brushes.LightGray }; body.Children.Add(info);
        var source = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brushes.LightGray }; body.Children.Add(source);
        var actions = new WrapPanel { Margin = new(0, 12, 0, 0) };
        var use = new Button { Content = "Usar esta referencia", IsEnabled = false, IsDefault = true };
        var local = new Button { Content = "Elegir de mi PC" };
        var without = new Button { Content = "Crear sin referencia" };
        var cancel = new Button { Content = "Volver", IsCancel = true };
        foreach (var button in new[] { use, local, without, cancel }) { button.Margin = new(0, 0, 8, 8); button.Padding = new(12, 9, 12, 9); actions.Children.Add(button); }
        body.Children.Add(actions);
        var dialog = new Window { Owner = owner, Title = "Referencia del personaje", Content = body, Width = 620, SizeToContent = SizeToContent.Height, MaxHeight = SystemParameters.WorkArea.Height - 40, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(24, 35, 44)), Foreground = Brushes.White };
        dialog.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        NativeWindowTheme.Attach(dialog);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = lifetime.Token;
        var closed = false; var version = 0;
        ReferenceSelection? selected = null, result = null;
        var cache = new Dictionary<string, string>();
        void ShowFile(string file, VisualReference? reference)
        {
            preview.Source = ReadImage(file);
            using var input = File.OpenRead(file); var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            info.Text = $"{frame.PixelWidth} × {frame.PixelHeight} píxeles" + (Math.Min(frame.PixelWidth, frame.PixelHeight) < 256 ? " · Resolución baja: puede perder detalles. Conviene elegir otra imagen." : " · Comprobá que sea el personaje y la apariencia que buscás.");
            source.Text = reference?.PageUrl ?? "Imagen de tu equipo. No se sube a internet.";
            selected = new(file, reference); use.IsEnabled = true;
        }
        choice.SelectionChanged += async (_, _) =>
        {
            var revision = ++version; use.IsEnabled = false; selected = null; preview.Source = null;
            if (choice.SelectedItem is not VisualReference reference) return;
            info.Text = "Descargando la referencia para revisarla…"; source.Text = reference.PageUrl;
            try
            {
                if (!cache.TryGetValue(reference.ImageUrl, out var file))
                { file = await search.DownloadAsync(reference, folder, token); cache[reference.ImageUrl] = file; }
                if (!closed && revision == version) ShowFile(file, reference);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or NotSupportedException or System.IO.FileFormatException)
            { if (!closed && revision == version) info.Text = "No se pudo mostrar esta referencia. Elegí otra o una imagen de tu PC. " + ex.Message; }
        };
        local.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg", Title = "Elegir referencia del personaje" };
            if (picker.ShowDialog(dialog) != true) return;
            ++version; use.IsEnabled = false; selected = null;
            try { ShowFile(picker.FileName, null); }
            catch (Exception ex) when (ex is IOException or NotSupportedException or System.IO.FileFormatException) { preview.Source = null; info.Text = ex.Message; }
        };
        use.Click += (_, _) => { if (selected is null) return; result = selected; dialog.DialogResult = true; };
        without.Click += (_, _) => { result = new(null, null); dialog.DialogResult = true; };
        dialog.Closed += (_, _) => { closed = true; lifetime.Cancel(); };
        using var registration = ct.Register(() => dialog.Dispatcher.BeginInvoke(new Action(() => { if (!closed && dialog.IsVisible) dialog.Close(); })));
        dialog.Loaded += (_, _) => { if (ct.IsCancellationRequested) dialog.Close(); else choice.SelectedIndex = choices.Length > 0 ? 0 : -1; };
        return dialog.ShowDialog() == true ? result : null;
    }
}
