using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiIALocal.Core;

namespace MiIALocal.Desktop;

public sealed class StorageWindow : Window
{
    private readonly StorageCleanup cleanup;
    private readonly StackPanel entries = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 10) };
    private readonly Button delete = new() { Content = "Eliminar seleccionados", IsEnabled = false };
    private readonly Dictionary<StorageEntry, CheckBox> choices = [];
    private bool working;
    private readonly Func<Task>? releaseBrowser;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready => ready.Task;
    public int SelectedCount => choices.Count(pair => pair.Value.IsChecked == true);
    public bool SettingsDeleted { get; private set; }
    public bool RequiresRefresh { get; private set; }
    public bool DataDeleted { get; private set; }

    public StorageWindow(AppPaths paths, AppSettings settings, bool uninstall = false, Func<Task>? releaseBrowser = null)
    {
        cleanup = new(paths, settings);
        this.releaseBrowser = releaseBrowser;
        NativeWindowTheme.Attach(this);
        Title = "Forja 50 · almacenamiento"; Width = 760; Height = 730; MinHeight = 500;
        Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)); Foreground = Brushes.White;
        FontFamily = new("Segoe UI"); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new(24), Background = Background }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Administrar almacenamiento", FontSize = 24, Margin = new(0, 0, 0, 12) });
        header.Children.Add(new TextBlock {
            Text = (uninstall ? "Elegí qué eliminar antes de desinstalar. Cerrar esta ventana conserva los datos restantes.\n\n" : "La IA debe estar detenida. Elegí qué eliminar; podrás descargar los componentes de nuevo.\n\n")
                + "Los archivos importados, los repositorios Git y la carpeta de proyecto están protegidos. Solo se borran los archivos de Forja que confirmes. Si eliminás conversaciones o ajustes, se reiniciará la vista y Forja seguirá abierta.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 18) });
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(status);
        var buttons = new WrapPanel(); footer.Children.Add(buttons); buttons.Children.Add(delete);
        var selectAll = new Button { Content = "Seleccionar todo" };
        selectAll.Click += (_, _) => { if (!working) foreach (var choice in choices.Values.Where(c => c.IsEnabled)) choice.IsChecked = true; };
        buttons.Children.Add(selectAll);
        var close = new Button { Content = uninstall ? "Continuar desinstalación" : "Cerrar" }; close.Click += (_, _) => Close(); buttons.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = entries, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        delete.Click += async (_, _) => await DeleteAsync();
        Closing += (_, e) => { if (working) e.Cancel = true; };
        Loaded += async (_, _) => await ReloadAsync();
    }
    private async Task ReloadAsync()
    {
        working = true; delete.IsEnabled = false; status.Text = "Calculando espacio...";
        try
        {
            var plan = await Task.Run(cleanup.Inspect);
            entries.Children.Clear(); choices.Clear();
            foreach (var item in plan)
            {
                var panel = new StackPanel { Margin = new(0, 0, 0, 18) };
                var choice = new CheckBox { Content = new TextBlock { Text = $"{item.Title} · {item.Bytes / 1e9:0.00} GB", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap }, IsEnabled = item.Paths.Length > 0 && item.BlockedReason is null };
                panel.Children.Add(choice); choices.Add(item, choice);
                panel.Children.Add(new TextBlock { Text = item.BlockedReason ?? (item.Paths.Length == 0 ? "Sin archivos para eliminar." : string.Join("\n", item.Paths)), TextWrapping = TextWrapping.Wrap, Foreground = item.BlockedReason is null ? Brushes.LightGray : Brushes.Salmon, FontSize = 11, Margin = new(24, 6, 0, 0) });
                entries.Children.Add(panel);
            }
            delete.IsEnabled = choices.Any(pair => pair.Value.IsEnabled);
            status.Text = "No se borró nada. Marcá las categorías que quieras eliminar.";
        }
        catch (Exception ex) { status.Text = "No se pudo revisar el almacenamiento: " + ex.Message; }
        finally { working = false; ready.TrySetResult(); }
    }
    private async Task DeleteAsync()
    {
        if (working) return;
        var selected = choices.Where(pair => pair.Value.IsChecked == true && pair.Value.IsEnabled).Select(pair => pair.Key).ToArray();
        if (selected.Length == 0) { status.Text = "Elegí al menos una categoría."; return; }
        var summary = string.Join("\n", selected.Select(e => $"• {e.Title} ({e.Bytes / 1e9:0.00} GB)"));
        if (MessageBox.Show(this, "Se eliminarán permanentemente estas categorías:\n\n" + summary + "\n\nLas rutas aparecen en la ventana. Esta acción no se puede deshacer. ¿Continuar?", "Confirmar limpieza", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        working = true; delete.IsEnabled = false; status.Text = "Eliminando los archivos seleccionados...";
        try
        {
            if (selected.Any(e => e.Category is StorageCategory.Conversations or StorageCategory.Settings))
            {
                if (releaseBrowser is not null) await releaseBrowser();
            }
            await Task.Run(() => cleanup.Delete(selected.Select(e => e.Category)));
            SettingsDeleted |= selected.Any(e => e.Category == StorageCategory.Settings);
            RequiresRefresh |= selected.Any(e => e.Category is StorageCategory.Conversations or StorageCategory.Settings);
            DataDeleted = true;
            working = false;
            await ReloadAsync(); status.Text = "Limpieza completada. Los archivos protegidos se conservaron.";
        }
        catch (Exception ex) { status.Text = "La limpieza no se pudo completar: " + ex.Message + " Los archivos restantes se conservan."; }
        finally { working = false; delete.IsEnabled = choices.Any(pair => pair.Value.IsEnabled); }
    }
}
