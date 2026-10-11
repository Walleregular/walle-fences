using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace WalleFences.App;

internal sealed class ProgramPickerWindow : Window
{
    private enum ProgramScope { All, Favorites, Recent }
    private sealed record ProgramRow(string Id, string Name, ImageSource? Icon, InstalledProgram? Installed = null, ProgramLibraryEntry? Saved = null);
    private readonly AppController _app;
    private readonly TextBox _query = new();
    private readonly ListBox _programs = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = Ui.Text("", 11, Ui.Muted);
    private readonly TextBlock _selection = Ui.Text("", 11, Ui.Muted);
    private readonly Button _add;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImportedReference> _prepared = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _busyPrograms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ProgramScope, Button> _scopeButtons = [];
    private readonly Button _clearRecent;
    private ProgramScope _scope;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private IReadOnlyList<InstalledProgram> _catalog = [];
    private bool _closed, _loading = true, _preparing, _partialCatalog;
    internal ImportedReference[] References { get; private set; } = [];

    internal ProgramPickerWindow(AppController app)
    {
        _app = app; Title = L.Text("Добавить программы"); Icon = Brand.WindowIcon;
        Width = 620; Height = 580; MinWidth = 460; MinHeight = 350;
        MaxHeight = Math.Max(350, SystemParameters.WorkArea.Height - 60);
        FontFamily = Ui.AppFont; Language = XmlLanguage.GetLanguage(L.Culture.Name);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Ui.WindowFrame(this); AutomationProperties.SetAutomationId(this, "program-picker");
        var shell = new DockPanel();
        var title = new DockPanel { Height = 40, Margin = new Thickness(15, 0, 7, 0) };
        var close = Ui.IconButton("close", Close, L.Text("Закрыть"), "program-picker-close");
        close.Margin = new Thickness(0); close.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(close, Dock.Right); title.Children.Add(close);
        title.Children.Add(Ui.Text(Title, 13, Ui.Ink, FontWeights.SemiBold));
        title.MouseLeftButtonDown += (_, e) =>
        {
            for (var source = e.OriginalSource as DependencyObject; source != null; source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
                if (source is ButtonBase) return;
            if (e.ButtonState == MouseButtonState.Pressed) { DragMove(); e.Handled = true; }
        };
        var titleBorder = new Border { Child = title, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(titleBorder, Dock.Top); shell.Children.Add(titleBorder);
        var body = new DockPanel { Margin = new Thickness(20, 16, 20, 16) }; shell.Children.Add(body);
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        var hint = Ui.Text(L.Text("Отметьте программы, чтобы добавить их в текущую вкладку."), 12, Ui.Muted); hint.Margin = new Thickness(0, 0, 0, 10); header.Children.Add(hint);
        _query.ToolTip = L.Text("Поиск по названию программы"); AutomationProperties.SetName(_query, (string)_query.ToolTip); AutomationProperties.SetAutomationId(_query, "program-picker-query"); header.Children.Add(_query);
        var filters = new DockPanel { Margin = new Thickness(0, 10, 0, 9) };
        _clearRecent = Ui.IconButton("trash", _app.ClearRecentPrograms, L.Text("Очистить недавние"), "program-picker-clear-recent"); _clearRecent.Margin = new Thickness(5, 0, 0, 0); DockPanel.SetDock(_clearRecent, Dock.Right); filters.Children.Add(_clearRecent);
        var scopes = new WrapPanel(); filters.Children.Add(scopes);
        foreach (var scope in Enum.GetValues<ProgramScope>())
        {
            var label = scope switch { ProgramScope.Favorites => "Избранное", ProgramScope.Recent => "Недавние", _ => "Все программы" };
            var button = Ui.Segment(L.Text(label), () => { _scope = scope; Filter(); }, _scope == scope, "program-picker-filter-" + scope.ToString().ToLowerInvariant());
            button.Margin = new Thickness(0, 0, 4, 0); _scopeButtons.Add(scope, button); scopes.Children.Add(button);
        }
        header.Children.Add(filters);
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false }; DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
        var cancel = Ui.Button(L.Text("Отмена"), Close, id: "program-picker-cancel"); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 7, 0); buttons.Children.Add(cancel);
        _add = Ui.Button(L.Text("Добавить выбранные"), () => _ = AddSelected(), true, "program-picker-add"); _add.IsEnabled = false; _add.IsDefault = true; _add.Margin = new Thickness(0); buttons.Children.Add(_add);
        _selection.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(_selection);
        _status.Margin = new Thickness(0, 0, 0, 7); _status.TextWrapping = TextWrapping.Wrap; DockPanel.SetDock(_status, Dock.Bottom); body.Children.Add(_status);
        AutomationProperties.SetAutomationId(_status, "program-picker-status");
        AutomationProperties.SetAutomationId(_programs, "program-picker-list"); body.Children.Add(_programs);
        Content = new Border { Child = shell, Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true };
        _query.TextChanged += (_, _) => { _filterTimer.Stop(); _filterTimer.Start(); };
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); Filter(); };
        Loaded += async (_, _) => { _query.Focus(); await LoadCatalog(); };
        _app.ProgramLibraryChanged += Filter;
        Closed += (_, _) => { _closed = true; _app.ProgramLibraryChanged -= Filter; _filterTimer.Stop(); _lifetime.Cancel(); _lifetime.Dispose(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Filter(); UpdateSelection();
    }

    private async Task LoadCatalog()
    {
        _status.Text = L.Text("Загрузка программ…");
        try
        {
            var result = await InstalledPrograms.LoadAsync(_lifetime.Token);
            if (_closed) return;
            _catalog = result.Programs; _partialCatalog = result.Errors.Count > 0;
            foreach (var error in result.Errors) _app.Log(error);
            _loading = false; Filter(); UpdateSelection();
        }
        catch (OperationCanceledException) when (_closed || _lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _app.Log(ex);
            if (_closed) return;
            _loading = false; _status.Text = L.Text("Не удалось загрузить программы. Можно добавить их через выбор файлов."); UpdateSelection();
        }
    }

    private void Filter()
    {
        if (_closed) return;
        foreach (var (scope, button) in _scopeButtons) button.Tag = scope == _scope ? "selected" : null;
        _clearRecent.Visibility = _scope == ProgramScope.Recent ? Visibility.Visible : Visibility.Collapsed;
        _clearRecent.IsEnabled = !_preparing && _app.Programs.Entries.Any(entry => entry.LastLaunchUtc != null);
        var words = _query.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var saved = _scope == ProgramScope.Recent
            ? _app.Programs.Entries.Where(entry => entry.LastLaunchUtc != null).OrderByDescending(entry => entry.LastLaunchUtc)
            : _app.Programs.Entries.Where(entry => _scope == ProgramScope.All || entry.IsFavorite).OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);
        var catalogIds = _catalog.Select(program => program.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<ProgramRow> rows = _scope == ProgramScope.All ? _catalog.Select(program => new ProgramRow("catalog:" + program.Id, program.Name, program.Icon, Installed: program)) : [];
        rows = rows.Concat(saved.Where(entry => _scope != ProgramScope.All || !entry.CatalogIds.Any(catalogIds.Contains)).Select(entry => new ProgramRow("saved:" + entry.Id, entry.Name, SavedIcon(entry.Path), Saved: entry)));
        var visible = rows.Where(program => words.All(word => (program.Name + " " + program.Saved?.Path + " " + program.Saved?.Launch?.Arguments).Contains(word, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        _programs.Items.Clear();
        foreach (var program in visible)
        {
            if (program.Saved is { } savedProgram) _prepared[program.Id] = new(savedProgram.Path, savedProgram.Path, savedProgram.Launch);
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var selection = new Grid(); selection.ColumnDefinitions.Add(new() { Width = new GridLength(34) }); selection.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var icon = program.Icon != null ? new Image { Source = program.Icon, Width = 24, Height = 24, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center } : Brand.Icon("desktop", Ui.Muted, 22);
            selection.Children.Add(icon);
            var details = new StackPanel { Margin = new Thickness(0, 1, 0, 1) }; Grid.SetColumn(details, 1); selection.Children.Add(details);
            var name = Ui.Text(program.Name, 13, Ui.Ink, FontWeights.Medium); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; details.Children.Add(name);
            var description = program.Saved is { } savedEntry
                ? ProgramHint(savedEntry)
                : L.Text(program.Installed!.IsWindowsApplication ? "Приложение Windows" : "Меню «Пуск»");
            var source = Ui.Text(description, 10.5, Ui.Muted); source.TextWrapping = TextWrapping.NoWrap; source.TextTrimming = TextTrimming.CharacterEllipsis; details.Children.Add(source);
            var check = new CheckBox { Content = selection, IsChecked = _selected.Contains(program.Id), HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(7, 5, 7, 5), ToolTip = program.Saved?.Path ?? program.Installed?.ShortcutPath ?? program.Name };
            AutomationProperties.SetName(check, program.Name);
            check.Checked += (_, _) => { _selected.Add(program.Id); UpdateSelection(); };
            check.Unchecked += (_, _) => { _selected.Remove(program.Id); UpdateSelection(); };
            row.Children.Add(check);
            var favorite = IsFavorite(program);
            var star = Ui.IconButton(Brand.Icon(favorite ? "star-filled" : "star", favorite ? Ui.Accent : Ui.Muted), () => _ = ToggleFavorite(program), L.Text(favorite ? "Убрать из избранного" : "Добавить в избранное"), "program-star-" + program.Id);
            star.IsEnabled = !_preparing && !_busyPrograms.Contains(program.Id); star.Margin = new Thickness(2, 0, 2, 0); star.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(star, 1); row.Children.Add(star);
            var open = Ui.IconButton("external-link", () => _ = Launch(program), L.Text("Запустить программу"), "program-launch-" + program.Id);
            open.IsEnabled = star.IsEnabled; open.Margin = new Thickness(2, 0, 5, 0); open.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(open, 2); row.Children.Add(open);
            _programs.Items.Add(new ListBoxItem { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), Margin = new Thickness(0, 1, 0, 1) });
        }
        _status.Text = visible.Length == 0 && words.Length == 0 && _scope == ProgramScope.Favorites ? L.Text("Пока нет избранных программ") + ". " + L.Text("Нажмите на звезду рядом с программой.")
            : visible.Length == 0 && words.Length == 0 && _scope == ProgramScope.Recent ? L.Text("Пока нет недавних запусков") + ". " + L.Text("Здесь появятся программы, успешно запущенные через Walle Fences.")
            : _loading && _scope == ProgramScope.All ? L.Text("Загрузка программ…")
            : visible.Length == 0 ? L.Text("Программы не найдены") : _partialCatalog && _scope == ProgramScope.All ? L.Text("Часть программ недоступна. Доступные программы можно добавить.") : L.Format("Программы: {0}", visible.Length);
    }

    private void UpdateSelection()
    {
        _selection.Text = L.Format("Выбрано: {0}", _selected.Count);
        _add.IsEnabled = !_preparing && _selected.Count > 0 && (!_loading || _selected.All(id => id.StartsWith("saved:", StringComparison.Ordinal)));
    }

    private async Task AddSelected()
    {
        if (_closed || _preparing || !_add.IsEnabled) return;
        _preparing = true; _filterTimer.Stop(); _query.IsEnabled = _programs.IsEnabled = false; UpdateSelection();
        _status.Text = L.Text("Подготовка программ…");
        try
        {
            var selected = _catalog.Where(program => _selected.Contains("catalog:" + program.Id)).ToArray();
            var references = await InstalledPrograms.PrepareReferencesAsync(selected, _app.Store.Root, _lifetime.Token);
            if (_closed) return;
            References = references.Concat(_selected.Where(id => id.StartsWith("saved:", StringComparison.Ordinal)).Select(id => _prepared[id]))
                .DistinctBy(reference => ProgramLibrary.InvocationId(reference.Path, reference.Launch)).ToArray(); DialogResult = true;
        }
        catch (OperationCanceledException) when (_closed || _lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _app.Log(ex);
            if (!_closed) _status.Text = L.Text("Не удалось подготовить выбранную программу. Уберите недоступную программу из выбора и повторите.") + " " + ex.Message;
        }
        finally
        {
            _preparing = false;
            if (!_closed) { _query.IsEnabled = _programs.IsEnabled = true; UpdateSelection(); }
        }
    }

    private ImageSource? SavedIcon(string path)
    {
        if (!_icons.TryGetValue(path, out var icon)) { icon = Native.Icon(path); _icons[path] = icon; }
        return icon;
    }

    private bool IsFavorite(ProgramRow row)
    {
        if (row.Saved != null) return _app.IsFavoriteProgram(row.Saved.Path, row.Saved.Launch);
        if (_prepared.TryGetValue(row.Id, out var reference)) return _app.IsFavoriteProgram(reference.Path, reference.Launch);
        return _app.Programs.Entries.Any(entry => entry.IsFavorite && entry.CatalogIds.Contains(row.Installed!.Id, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<ImportedReference> Prepare(ProgramRow row)
    {
        if (_prepared.TryGetValue(row.Id, out var reference)) return reference;
        var references = await InstalledPrograms.PrepareReferencesAsync([row.Installed!], _app.Store.Root, _lifetime.Token);
        reference = references[0];
        reference = reference with { Launch = reference.Launch is { } launch ? launch with { Name = row.Name } : new(row.Name) };
        _prepared[row.Id] = reference; return reference;
    }

    private async Task ToggleFavorite(ProgramRow row)
    {
        if (_preparing || !_busyPrograms.Add(row.Id)) return;
        var favorite = !IsFavorite(row);
        Filter();
        try
        {
            var reference = await Prepare(row); if (_closed) return;
            _app.SetProgramFavorite(reference.Path, reference.Launch, favorite, row.Installed?.Id);
        }
        catch (OperationCanceledException) when (_closed || _lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _app.Log(ex); if (!_closed) Ui.Error(ex.Message); }
        finally { _busyPrograms.Remove(row.Id); if (!_closed) Filter(); }
    }

    private async Task Launch(ProgramRow row)
    {
        if (_preparing || !_busyPrograms.Add(row.Id)) return;
        Filter();
        try { var reference = await Prepare(row); if (!_closed) Native.Open(reference.Path, reference.Launch); }
        catch (OperationCanceledException) when (_closed || _lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _app.Log(ex); if (!_closed) Ui.Error(ex.Message); }
        finally { _busyPrograms.Remove(row.Id); if (!_closed) Filter(); }
    }

    internal static string ProgramHint(ProgramLibraryEntry entry)
    {
        var launch = entry.Launch;
        return (entry.LastLaunchUtc is { } last ? L.Format("Последний запуск: {0}", last.ToLocalTime().ToString("g", L.Culture)) + " · " : "")
            + entry.Path + (launch?.Arguments.Length > 0 ? " · " + launch.Arguments : "")
            + (launch?.WorkingDirectory.Length > 0 ? " · " + L.Format("Рабочая папка: {0}", launch.WorkingDirectory) : "")
            + (launch?.RunAsAdministrator == true ? " · " + L.Text("Запуск от администратора") : "");
    }
}
