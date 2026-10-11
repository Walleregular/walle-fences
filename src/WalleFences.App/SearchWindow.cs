using System.Threading.Tasks;
using System.Windows.Automation;

namespace WalleFences.App;

internal sealed class SearchWindow : Window
{
    private enum SearchScope { All, Favorites, Recent }
    private sealed record Result(string Title, string Hint, Action Run, string Icon = "file", string? FilePath = null, ReferenceLaunch? Launch = null, ProgramLibraryEntry? Saved = null);
    private sealed record IndexedFile(FileEntry Entry, ReferenceLaunch? Launch = null);
    private readonly AppController _app;
    private readonly TextBox _query = new();
    private readonly ListBox _results = new();
    private readonly TextBlock _status = Ui.Text("", 11, Ui.Muted);
    private List<Result> _index = [];
    private readonly Dictionary<string, ImageSource?> _fileIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SearchScope, Button> _scopeButtons = [];
    private SearchScope _scope;
    private int _generation;
    private bool _closed;
    private static (string Path, string Arguments, string WorkingDirectory, bool Administrator) InvocationKey(string path, ReferenceLaunch? launch)
        => (PathExtensions.Key(path).ToUpperInvariant(), launch?.Arguments ?? "", (launch?.WorkingDirectory ?? "").ToUpperInvariant(), launch?.RunAsAdministrator ?? false);
    internal SearchWindow(AppController app)
    {
        _app = app; Title = L.Text("Поиск и команды"); Icon = Brand.WindowIcon;
        Width = 680; Height = 500; MinWidth = 440; MinHeight = 280; MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 60);
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        Ui.WindowFrame(this);
        AutomationProperties.SetAutomationId(this, "command-search");
        var shell = new DockPanel();
        var title = new DockPanel { Margin = new Thickness(14, 0, 6, 0), Height = 40 };
        var close = Ui.IconButton(Brand.Icon("close", Ui.Ink), Close, L.Text("Закрыть"), "command-close"); close.Margin = new Thickness(0); close.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(close, Dock.Right); title.Children.Add(close);
        var name = Ui.Row(Brand.Icon("search", Ui.Muted), Ui.Text(L.Text("Поиск и команды"), 12, Ui.Ink, FontWeights.Medium));
        name.VerticalAlignment = VerticalAlignment.Center; ((FrameworkElement)name.Children[1]).Margin = new Thickness(9, 0, 0, 0); title.Children.Add(name);
        title.MouseLeftButtonDown += (_, e) =>
        {
            for (var source = e.OriginalSource as DependencyObject; source != null; source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
                if (source is Button) return;
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        var titleBorder = new Border { Child = title, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(titleBorder, Dock.Top); shell.Children.Add(titleBorder);
        var body = new DockPanel { Margin = new Thickness(18, 16, 18, 14) }; shell.Children.Add(body);
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        var hint = Ui.Text(L.Text("Группы, файлы, страницы и действия"), 11, Ui.Muted); hint.Margin = new Thickness(0, 0, 0, 9); header.Children.Add(hint);
        _query.ToolTip = hint.Text; AutomationProperties.SetName(_query, hint.Text); AutomationProperties.SetAutomationId(_query, "command-query"); header.Children.Add(_query);
        var scopes = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) }; header.Children.Add(scopes);
        foreach (var scope in Enum.GetValues<SearchScope>())
        {
            var label = scope switch { SearchScope.Favorites => "Избранное", SearchScope.Recent => "Недавние", _ => "Все результаты" };
            var button = Ui.Segment(L.Text(label), () => { _scope = scope; Filter(); _query.Focus(); }, scope == _scope, "command-filter-" + scope.ToString().ToLowerInvariant());
            button.Margin = new Thickness(0, 0, 4, 0); _scopeButtons.Add(scope, button); scopes.Children.Add(button);
        }
        _status.Margin = new Thickness(0, 10, 0, 0); DockPanel.SetDock(_status, Dock.Bottom); body.Children.Add(_status);
        _results.HorizontalContentAlignment = HorizontalAlignment.Stretch; AutomationProperties.SetAutomationId(_results, "command-results"); body.Children.Add(_results);
        Content = new Border { Child = shell, Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true };
        _query.TextChanged += (_, _) => Filter();
        _results.MouseDoubleClick += (_, e) => { if (!FromButton(e.OriginalSource as DependencyObject)) Run(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.Enter && !FromButton(Keyboard.FocusedElement as DependencyObject)) { Run(); e.Handled = true; }
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && (_results.SelectedItem as ListBoxItem)?.Tag is Result selected && selected.FilePath is { } path && AppController.CanSaveProgram(path, selected.Launch))
            { Ui.Guard(() => _app.ToggleProgramFavorite(path, selected.Launch)); e.Handled = true; }
            if (e.Key is Key.Down or Key.Up)
            {
                if (_results.Items.Count > 0) { _results.SelectedIndex = Math.Clamp(_results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, _results.Items.Count - 1); _results.ScrollIntoView(_results.SelectedItem); }
                e.Handled = true;
            }
        };
        Activated += async (_, _) => await LoadIndex();
        _app.ProgramLibraryChanged += Filter;
        Closed += (_, _) => { _closed = true; _generation++; _app.ProgramLibraryChanged -= Filter; };
    }
    internal void FocusSearch() { _query.Focus(); _query.SelectAll(); }
    private async Task LoadIndex()
    {
        var generation = ++_generation; var state = _app.Workspace;
        _index = [new(L.Text("Настройки"), L.Text("Открыть настройки программы"), _app.ShowManager, "settings"),
            new(L.Text("Быстрый доступ"), state.Preferences.PeekHotkey, _app.Peek, "peek"),
            new(L.Text("Показать / скрыть группы"), state.Preferences.HideHotkey, _app.ToggleHidden, _app.Hidden ? "eye" : "eye-off"),
            new(L.Text("Свернуть все группы"), "", () => _app.CollapseAll(true), "collapse"),
            new(L.Text("Развернуть все группы"), "", () => _app.CollapseAll(false), "expand"),
            new(L.Text("Сохранить снимок"), "", () => _app.Snapshot(L.Text("Быстрый снимок")), "snapshots")];
        foreach (var (section, title) in new[] { ("pages", L.Text("Наборы")), ("widgets", L.Text("Виджеты")), ("appearance", L.Text("Оформление")), ("monitors", L.Text("Мониторы")), ("shortcuts", L.Text("Сочетания клавиш")) })
            _index.Add(new(title, L.Text("Настройки"), () => { _app.ShowManager(); _app.Manager!.Navigate(section); }, section));
        _index.Add(new(L.Text("Добавить виджет"), L.Text("Каждый виджет появится отдельной панелью в текущем наборе."), () => _app.AddWidgetPanel(_app.Manager), "widgets"));
        _index.Add(new(L.Text("Предпросмотр автосортировки"), L.Text("Поиск по файлу, группе или правилу"), () => SortingPreviewWindow.Open(_app, _app.Manager), "rules"));
        foreach (var page in state.Pages) _index.Add(new(page.Name, L.Text("Набор"), () => _app.SwitchPage(page.Id), "pages"));
        foreach (var fence in state.Fences)
        {
            _index.Add(new(fence.Name, L.Text("Группа") + " · " + fence.Hotkey, () => _app.OpenGroup(fence), GroupIcons.Symbol(fence)));
            foreach (var tab in fence.Tabs) _index.Add(new(tab.Name, fence.Name, () => _app.OpenGroup(fence, tab), tab.PortalPath == null ? "tabs" : "folder"));
        }
        Filter(); _status.Text = L.Text("Загрузка файлов…");
        var tabs = state.Fences.SelectMany(f => f.Tabs).ToArray();
        var references = tabs.SelectMany(t => FileCatalog.References(t)).DistinctBy(r => InvocationKey(r.Path, r.Launch)).Take(30000).ToArray();
        var portals = tabs.Select(t => t.PortalPath).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToArray();
        var hidden = state.Preferences.ShowHiddenFiles;
        try
        {
            var files = await Task.Run(() => references.Select(reference => new IndexedFile(FileCatalog.Entry(reference), reference.Launch))
                .Concat(portals.SelectMany(p => FileCatalog.ReadFolder(p!, hidden)).Select(entry => new IndexedFile(entry)))
                .DistinctBy(file => InvocationKey(file.Entry.Path, file.Launch)).Take(50000).ToArray());
            if (_closed || generation != _generation) return;
            foreach (var file in files)
            {
                var entry = file.Entry;
                var invocation = file.Launch is { Arguments.Length: > 0 } launch ? " · " + launch.Arguments : "";
                _index.Add(new(entry.Name, entry.Path + invocation, () => Native.Open(entry.Path, file.Launch), entry.IsDirectory ? "folder" : "file", entry.Path, file.Launch));
            }
            Filter();
        }
        catch (Exception ex) { if (!_closed && generation == _generation) { _status.Text = L.Text("Часть файлов недоступна. Группы и команды доступны."); _app.Log(ex); } }
    }
    private void Filter()
    {
        if (_closed) return;
        foreach (var (scope, button) in _scopeButtons) button.Tag = scope == _scope ? "selected" : null;
        var query = _query.Text.Trim(); var selected = (_results.SelectedItem as ListBoxItem)?.Tag as Result;
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var library = _app.Programs.Entries.Where(entry => _scope switch { SearchScope.Favorites => entry.IsFavorite, SearchScope.Recent => entry.LastLaunchUtc != null, _ => entry.IsFavorite || entry.LastLaunchUtc != null })
            .Select(entry => new Result(entry.Name, ProgramPickerWindow.ProgramHint(entry), () => Native.Open(entry.Path, entry.Launch), "desktop", entry.Path, entry.Launch, entry)).ToArray();
        var libraryIds = library.Select(result => ProgramLibrary.InvocationId(result.FilePath!, result.Launch)).ToHashSet(StringComparer.Ordinal);
        IEnumerable<Result> source = _scope == SearchScope.All ? library.Concat(_index.Where(result => result.FilePath == null || !libraryIds.Contains(ProgramLibrary.InvocationId(result.FilePath, result.Launch)))) : library;
        var matching = source.Where(r => tokens.All(t => (r.Title + " " + r.Hint).Contains(t, StringComparison.CurrentCultureIgnoreCase)));
        var filtered = _scope == SearchScope.Recent
            ? matching.OrderByDescending(result => result.Saved?.LastLaunchUtc).Take(80).ToArray()
            : matching.OrderByDescending(r => query.Length > 0 && r.Title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)).ThenByDescending(result => result.Saved?.IsFavorite == true).ThenByDescending(result => result.Saved?.LastLaunchUtc).Take(80).ToArray();
        _results.Items.Clear();
        foreach (var result in filtered)
        {
            var row = new Grid { Margin = new Thickness(8, 6, 8, 6) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(30) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.Children.Add(ResultIcon(result));
            var details = new StackPanel(); var title = Ui.Text(result.Title, 13, Ui.Ink, FontWeights.Medium); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; details.Children.Add(title);
            var hint = Ui.Text(result.Hint, 11, Ui.Muted); hint.TextWrapping = TextWrapping.NoWrap; hint.TextTrimming = TextTrimming.CharacterEllipsis; details.Children.Add(hint); Grid.SetColumn(details, 1); row.Children.Add(details);
            if (result.FilePath is { } programPath && AppController.CanSaveProgram(programPath, result.Launch))
            {
                var favorite = _app.IsFavoriteProgram(programPath, result.Launch);
                var star = Ui.IconButton(Brand.Icon(favorite ? "star-filled" : "star", favorite ? Ui.Accent : Ui.Muted), () => _app.ToggleProgramFavorite(programPath, result.Launch), L.Text(favorite ? "Убрать из избранного" : "Добавить в избранное") + " · Ctrl+F", "command-star-" + ProgramLibrary.InvocationId(programPath, result.Launch));
                star.Margin = new Thickness(8, 0, 0, 0); star.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(star, 2); row.Children.Add(star);
            }
            var item = new ListBoxItem { Content = row, Tag = result, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = result.Title + (result.Hint.Length > 0 ? " · " + result.Hint : "") }; AutomationProperties.SetName(item, result.Title); _results.Items.Add(item);
        }
        var selectedIndex = selected == null ? -1 : Array.FindIndex(filtered, result => result.FilePath != null && selected.FilePath != null ? InvocationKey(result.FilePath, result.Launch) == InvocationKey(selected.FilePath, selected.Launch) : result.Title == selected.Title && result.Hint == selected.Hint);
        _results.SelectedIndex = filtered.Length == 0 ? -1 : Math.Max(0, selectedIndex);
        _status.Text = filtered.Length == 0 && query.Length == 0 && _scope == SearchScope.Favorites ? L.Text("Пока нет избранных программ") + ". " + L.Text("Нажмите на звезду рядом с программой.")
            : filtered.Length == 0 && query.Length == 0 && _scope == SearchScope.Recent ? L.Text("Пока нет недавних запусков") + ". " + L.Text("Здесь появятся программы, успешно запущенные через Walle Fences.")
            : filtered.Length == 0 ? L.Text("Ничего не найдено") : L.Text("Enter — открыть · Esc — закрыть · ↑↓ — выбрать");
    }
    private Image ResultIcon(Result result)
    {
        if (result.FilePath is { } path)
        {
            if (!_fileIcons.TryGetValue(path, out var source)) { source = Native.Icon(path); if (_fileIcons.Count >= 1024) _fileIcons.Clear(); _fileIcons[path] = source; }
            if (source != null) return new Image { Source = source, Width = 20, Height = 20, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Focusable = false, Tag = "shell-file" };
        }
        var icon = Brand.Icon(result.Icon, Ui.Muted); icon.HorizontalAlignment = HorizontalAlignment.Left; return icon;
    }
    private void Run()
    {
        if ((_results.SelectedItem as ListBoxItem)?.Tag is not Result result) return;
        Close(); Ui.Guard(result.Run);
    }
    private static bool FromButton(DependencyObject? source)
    {
        for (; source != null; source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (source is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }
}
