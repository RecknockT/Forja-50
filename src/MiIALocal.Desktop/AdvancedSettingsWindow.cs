using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiIALocal.Core;

namespace MiIALocal.Desktop;

public sealed class AdvancedSettingsWindow : Window
{
    private readonly Dictionary<PropertyInfo, TextBox> fields = [];
    private readonly ComboBox context = new() { ItemsSource = new[] { "64K · predeterminado", "32K · alternativa" }, Margin = new(0, 5, 0, 12), Padding = new(8) };
    private readonly ComboBox kv = new() { ItemsSource = EngineTuning.KvTypes, Padding = new(8) };
    private readonly TextBlock error = new() { Foreground = Brush("#FFB4A9"), Margin = new(0, 12, 0, 8) };
    public EngineTuning Result { get; private set; }
    public int ContextTokens { get; private set; }
    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    public AdvancedSettingsWindow(AppSettings current)
    {
        NativeWindowTheme.Attach(this);
        Title = "Configuración avanzada"; Width = 760; Height = 790; MinWidth = 620; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brush("#101820"); Foreground = Brush("#E2ECF1"); FontFamily = new("Segoe UI");
        Result = JsonSerializer.Deserialize<EngineTuning>(JsonSerializer.Serialize(current.Tuning))!;
        ContextTokens = current.ContextTokens;
        var root = new DockPanel { Margin = new(24), Background = Background }; Content = root;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "Ajustá cómo trabaja tu IA", FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Estos cambios pueden aumentar el uso de VRAM o RAM. Los límites verifican que los valores sean válidos; no garantizan que todas las combinaciones entren en tu GPU. Se aplican al próximo inicio.", Foreground = Brush("#9FB1BD"), Margin = new(0, 10, 0, 16) });
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(error);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var restore = new Button { Content = "Restaurar recomendada" }; restore.Click += (_, _) => Fill(new(), 65536); buttons.Children.Add(restore);
        var save = new Button { Content = "Guardar", Background = Brush("#087F70"), Foreground = Brushes.White }; save.Click += (_, _) => Save(); buttons.Children.Add(save);
        var cancel = new Button { Content = "Cancelar", IsCancel = true }; buttons.Children.Add(cancel);
        var content = new StackPanel(); root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(new TextBlock { Text = "Contexto de conversación", FontWeight = FontWeights.SemiBold }); content.Children.Add(context);
        foreach (var (property, label, hint) in new (string, string, string)[]
        {
            (nameof(EngineTuning.HistoryTokens), "Historial seleccionado en GPU", "tokens · múltiplo de 64"),
            (nameof(EngineTuning.GenerationReserve), "Reserva de generación GPU", "tokens · múltiplo de 64"),
            (nameof(EngineTuning.HostCacheMiB), "Caché KV en RAM", "MiB · auto = 4096 con 64K / 2048 con 32K"),
            (nameof(EngineTuning.SinkTokens), "Retener inicio del historial", "tokens · múltiplo de 64"),
            (nameof(EngineTuning.RecentTokens), "Retener tokens recientes", "tokens · mínimo 64"),
            (nameof(EngineTuning.PrefillChunk), "Bloque de procesamiento inicial", "tokens · múltiplo de 128"),
            (nameof(EngineTuning.DraftTokens), "Borradores MTP por ronda", "1 a 4"),
            (nameof(EngineTuning.NgramDraftTokens), "Borradores por copia Ngram", "0 = desactivado · hasta 63"),
            (nameof(EngineTuning.NgramMinMatch), "Coincidencia mínima Ngram", "4 a 64 tokens"),
            (nameof(EngineTuning.ThinkingTokens), "Presupuesto de razonamiento", "tokens · incluido en la salida"),
            (nameof(EngineTuning.MaxOutput), "Salida máxima por respuesta", "tokens · hasta 16384"),
            (nameof(EngineTuning.Temperature), "Temperatura del motor", "0 a 2"),
            (nameof(EngineTuning.AgentTemperature), "Temperatura del agente", "0 a 2 · el agente la envía al motor"),
            (nameof(EngineTuning.TopP), "Top P", "mayor que 0 y hasta 1"),
            (nameof(EngineTuning.TopK), "Top K", "0 a 20"),
            (nameof(EngineTuning.MinP), "Min P", "0 a 1")
        })
        {
            var row = new Grid { Margin = new(0, 0, 12, 10) }; row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new(175) });
            var labels = new StackPanel(); labels.Children.Add(new TextBlock { Text = label }); labels.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = Brush("#9FB1BD") }); row.Children.Add(labels);
            var input = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, ToolTip = hint }; Grid.SetColumn(input, 1); row.Children.Add(input);
            fields.Add(typeof(EngineTuning).GetProperty(property)!, input); content.Children.Add(row);
        }
        content.Children.Add(new TextBlock { Text = "Formato de caché KV", FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 8) }); content.Children.Add(kv);
        content.Children.Add(new TextBlock { Text = "Fijos en esta versión: GDN FP16, MTP, una sesión y una petición a la vez, visión desactivada, perfil automático del motor desactivado y conexiones solo a este equipo. Cambiar el formato KV también cambia consumo y precisión.", Foreground = Brush("#9FB1BD"), FontSize = 12, Margin = new(0, 12, 0, 12) });
        Fill(Result, ContextTokens);
    }
    private void Fill(EngineTuning tuning, int tokens)
    {
        context.SelectedIndex = tokens == 65536 ? 0 : 1; kv.SelectedItem = tuning.KvType;
        foreach (var (property, input) in fields) input.Text = Convert.ToString(property.GetValue(tuning), CultureInfo.InvariantCulture) ?? "auto";
        if (tuning.HostCacheMiB is null) fields[typeof(EngineTuning).GetProperty(nameof(EngineTuning.HostCacheMiB))!].Text = "auto";
        error.Text = "";
    }
    private void Save()
    {
        try
        {
            var next = new EngineTuning { KvType = kv.SelectedItem as string ?? "" };
            foreach (var (property, input) in fields)
            {
                var text = input.Text.Trim(); object? value;
                if (property.PropertyType == typeof(int?) && (text.Length == 0 || text.Equals("auto", StringComparison.OrdinalIgnoreCase))) value = null;
                else if (property.PropertyType == typeof(double))
                {
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) throw new InvalidDataException("Revisá el número de " + property.Name + ".");
                    value = number;
                }
                else { if (!int.TryParse(text, out var number)) throw new InvalidDataException("Revisá el entero de " + property.Name + "."); value = number; }
                property.SetValue(next, value);
            }
            var tokens = context.SelectedIndex == 0 ? 65536 : 32768; next.Validate(tokens);
            Result = next; ContextTokens = tokens; DialogResult = true;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { error.Text = ex.Message; }
    }
}
