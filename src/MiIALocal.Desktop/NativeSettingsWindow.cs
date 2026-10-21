using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiIALocal.Core;

namespace MiIALocal.Desktop;

public sealed class NativeSettingsWindow : Window
{
    public NativeEngineTuning Result { get; private set; }
    public int ContextTokens { get; private set; }
    private readonly Dictionary<PropertyInfo, TextBox> fields = [];
    private readonly ComboBox context = new() { ItemsSource = new[] { "64K · inicial", "128K · experimental en Windows" }, Padding = new(8), Margin = new(0, 6, 0, 16) };
    private readonly CheckBox graph = new() { Content = new TextBlock { Text = "CUDA Graph activado", Foreground = Brushes.White }, Margin = new(0, 12, 0, 10) };
    private readonly TextBlock error = new() { Foreground = Brushes.Salmon, TextWrapping = TextWrapping.Wrap };
    public NativeSettingsWindow(AppSettings settings)
    {
        NativeWindowTheme.Attach(this);
        Title = "Forja 50 · configuración nativa"; Width = 700; Height = 710; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)); Foreground = Brushes.White; FontFamily = new("Segoe UI");
        Result = JsonSerializer.Deserialize<NativeEngineTuning>(JsonSerializer.Serialize(settings.NativeTuning))!;
        ContextTokens = settings.ContextTokens;
        var root = new DockPanel { Margin = new(24), Background = Background }; Content = root;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(error);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var restore = new Button { Content = "Restaurar perfil por placa" }; restore.Click += (_, _) => Fill(new(), 65536); buttons.Children.Add(restore);
        var save = new Button { Content = "Guardar" }; save.Click += (_, _) => Save(); buttons.Children.Add(save);
        buttons.Children.Add(new Button { Content = "Cancelar", IsCancel = true });
        var form = new StackPanel(); root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        form.Children.Add(new TextBlock { Text = "NInfer nativo · ajustes avanzados", FontSize = 24, Margin = new(0, 0, 0, 12) });
        form.Children.Add(new TextBlock { Text = "Se aplican al próximo inicio. 128K y visión aumentan el uso de memoria; las pruebas en GPU de estos perfiles de Forja siguen pendientes.", TextWrapping = TextWrapping.Wrap });
        form.Children.Add(context);
        foreach (var (property, label) in new (string, string)[] {
            (nameof(NativeEngineTuning.PrefillChunk), "Prefill · 0 = automático (5080: 896 / 5070 Ti: 512)"),
            (nameof(NativeEngineTuning.ThinkingTokens), "Razonamiento · tokens (0 = desactivado)"),
            (nameof(NativeEngineTuning.MaxOutput), "Salida máxima · tokens"),
            (nameof(NativeEngineTuning.DraftTokens), "Borradores MTP por ronda · 1 a 4"),
            (nameof(NativeEngineTuning.VisionMaxTokens), "Presupuesto de visión · 256 a 2048 tokens"),
            (nameof(NativeEngineTuning.AgentTemperature), "Temperatura del agente · 0 a 2") })
        {
            form.Children.Add(new TextBlock { Text = label, Margin = new(0, 10, 0, 4) });
            var input = new TextBox { Padding = new(8) }; fields.Add(typeof(NativeEngineTuning).GetProperty(property)!, input); form.Children.Add(input);
        }
        form.Children.Add(graph);
        form.Children.Add(new TextBlock { Text = "Fijos: Q4 KV, embeddings en RAM, un modelo y una petición a la vez. El prefill automático diferencia las placas; un valor manual lo reemplaza. El contexto y la visión también se pueden elegir en la pantalla principal.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        Fill(Result, ContextTokens);
    }
    private void Fill(NativeEngineTuning tuning, int tokens)
    {
        context.SelectedIndex = tokens == 131072 ? 1 : 0; graph.IsChecked = tuning.CudaGraph;
        foreach (var (property, input) in fields) input.Text = Convert.ToString(property.GetValue(tuning), CultureInfo.InvariantCulture)!;
        error.Text = "";
    }
    private void Save()
    {
        try
        {
            var next = new NativeEngineTuning { CudaGraph = graph.IsChecked == true };
            foreach (var (property, input) in fields)
            {
                if (property.PropertyType == typeof(double))
                {
                    if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) && !double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) throw new InvalidDataException("Revisá la temperatura.");
                    property.SetValue(next, number);
                }
                else
                {
                    if (!int.TryParse(input.Text, out var number)) throw new InvalidDataException("Usá un número entero en " + property.Name);
                    property.SetValue(next, number);
                }
            }
            var tokens = context.SelectedIndex == 1 ? 131072 : 65536; next.Validate(tokens);
            Result = next; ContextTokens = tokens; DialogResult = true;
        }
        catch (InvalidDataException ex) { error.Text = ex.Message; }
    }
}
