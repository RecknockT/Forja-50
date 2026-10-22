using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MiIALocal.Desktop;

/// <summary>Review and edit the assistant caption before any diffusion component loads.</summary>
internal static class PreparedPromptWindow
{
    internal const string DialogTitle = "Descripción preparada";
    internal const string AcceptText = "Generar con esta descripción";
    internal const string RegenerateText = "Preparar otra vez";
    internal const string OriginalText = "Usar mi descripción original";

    internal static string? Show(Window owner, string prepared, string original, bool usedReference, Func<CancellationToken, Task<string>> regenerate, CancellationToken ct)
    {
        var body = new StackPanel { Margin = new(22) };
        body.Children.Add(new TextBlock { Text = "Revisá la descripción antes de generar", FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock
        {
            Text = usedReference
                ? "El asistente amplió tu pedido analizando también la imagen de referencia. Corregí lo que no corresponda: el texto que quede acá es el que recibe HiDream."
                : "El asistente amplió tu pedido. Corregí lo que no corresponda: el texto que quede acá es el que recibe HiDream.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 12), Foreground = Brushes.LightGray
        });
        var box = new TextBox { Text = prepared, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 220, MaxHeight = 360, MaxLength = 12000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(box);
        var state = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0), Foreground = Brushes.LightGray, FontSize = 12 };
        body.Children.Add(state);
        var actions = new WrapPanel { Margin = new(0, 14, 0, 0) };
        var accept = new Button { Content = AcceptText, IsDefault = true };
        var again = new Button { Content = RegenerateText };
        var mine = new Button { Content = OriginalText };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        foreach (var button in new[] { accept, again, mine, cancel }) { button.Margin = new(0, 0, 8, 8); button.Padding = new(12, 9, 12, 9); actions.Children.Add(button); }
        body.Children.Add(actions);
        var dialog = new Window
        {
            Owner = owner, Title = DialogTitle, Width = 700, SizeToContent = SizeToContent.Height, MaxHeight = SystemParameters.WorkArea.Height - 40,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(24, 35, 44)), Foreground = Brushes.White,
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        NativeWindowTheme.Attach(dialog);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var closed = false; var working = false; string? result = null;
        void Sync()
        {
            box.IsReadOnly = working;
            accept.IsEnabled = !working && !string.IsNullOrWhiteSpace(box.Text);
            again.IsEnabled = !working;
            mine.IsEnabled = !working;
        }
        accept.Click += (_, _) =>
        {
            if (working || string.IsNullOrWhiteSpace(box.Text)) { state.Text = "La descripción no puede quedar vacía."; return; }
            result = box.Text.Trim(); dialog.DialogResult = true;
        };
        mine.Click += (_, _) => { if (!working) { result = original; dialog.DialogResult = true; } };
        cancel.Click += (_, _) => dialog.Close();
        again.Click += async (_, _) =>
        {
            if (working) return;
            working = true; state.Text = "Preparando otra descripción…"; Sync();
            try
            {
                var text = await regenerate(lifetime.Token);
                if (closed) return;
                box.Text = text; state.Text = "Descripción actualizada. Podés corregirla antes de generar.";
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { if (!closed) state.Text = "No se pudo preparar otra descripción: " + ex.Message; }
            finally { working = false; if (!closed) Sync(); }
        };
        box.TextChanged += (_, _) => { if (!working) { state.Text = ""; Sync(); } };
        dialog.Closed += (_, _) => { closed = true; lifetime.Cancel(); };
        using var registration = ct.Register(() => dialog.Dispatcher.BeginInvoke(new Action(() => { if (!closed && dialog.IsVisible) dialog.Close(); })));
        dialog.Loaded += (_, _) => { if (ct.IsCancellationRequested) dialog.Close(); else { Sync(); box.Focus(); box.CaretIndex = box.Text.Length; } };
        return dialog.ShowDialog() == true ? result : null;
    }

    /// <summary>Drive the dialog without a person: regenerate, edit and accept; reset to the original; cancel.</summary>
    internal static void VerifyForTests(Window owner)
    {
        var phase = 0;
        bool Step(Window dialog)
        {
            switch (phase)
            {
                case 0:
                    if (!Click(dialog, RegenerateText)) return false;
                    phase = 1; return false;
                case 1:
                    var box = Descendants(dialog).OfType<TextBox>().First();
                    if (box.Text != "Texto regenerado") return false;
                    box.Text = "Texto editado"; phase = 2; return false;
                default:
                    return Click(dialog, AcceptText);
            }
        }
        var result = Drive(owner, "Texto preparado", "Texto original", _ => Task.FromResult("Texto regenerado"), Step, "No se pudo regenerar, editar y aceptar la descripción.");
        if (result != "Texto editado") throw new Exception("La revisión no conservó la descripción editada.");
        result = Drive(owner, "Texto preparado", "Texto original", _ => Task.FromResult("Texto regenerado"), dialog => Click(dialog, OriginalText), "No se pudo volver a la descripción original.");
        if (result != "Texto original") throw new Exception("Volver al pedido original perdió el texto de la persona.");
        result = Drive(owner, "Texto preparado", "Texto original", _ => Task.FromResult("Texto regenerado"), dialog => Click(dialog, "Cancelar"), "No se pudo cancelar la revisión.");
        if (result is not null) throw new Exception("Cancelar la revisión inició una generación.");
    }

    private static bool Click(Window dialog, string content)
    {
        var button = Descendants(dialog).OfType<Button>().FirstOrDefault(b => Equals(b.Content, content));
        if (button?.IsEnabled != true) return false;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private static string? Drive(Window owner, string prepared, string original, Func<CancellationToken, Task<string>> regenerate, Func<Window, bool> step, string failure)
    {
        var ticks = 0; Exception? error = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var dialog = owner.OwnedWindows.Cast<Window>().FirstOrDefault(w => w.Title == DialogTitle);
            if (dialog is null) { if (++ticks > 200) { error = new Exception(failure); timer.Stop(); } return; }
            if (++ticks > 240) { error = new Exception(failure); timer.Stop(); dialog.Close(); return; }
            try { if (step(dialog)) timer.Stop(); }
            catch (Exception ex) { error = ex; timer.Stop(); dialog.Close(); }
        };
        timer.Start();
        string? result;
        try { result = Show(owner, prepared, original, false, regenerate, default); } finally { timer.Stop(); }
        if (error is not null) throw error;
        return result;
    }
}
