using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace WalleFences.App;

internal sealed class SortingPreviewWindow : Window
{
    private enum Filter { All, Ready, Problems, AlreadyAdded, NoRule }
    private readonly AppController _app;
    private readonly SortingRule? _draftRule;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _filterDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly ListBox _list = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _query = new() { MaxLength = 120 };
    private readonly TextBlock _summary = Ui.Text("", 12, Ui.Muted), _message = Ui.Text("", 11, Ui.Muted);
    private readonly TextBlock _empty = Ui.Text("", 14, Ui.Muted);
    private readonly TextBlock _scope = Ui.Text("", 12, Ui.Ink, FontWeights.SemiBold);
    private readonly StackPanel _details = new();
    private readonly Dictionary<Filter, Button> _filters = [];
    private readonly Button _refresh, _apply;
    private IReadOnlyList<SortingPreviewRow> _rows = [];
    private IReadOnlyList<SortingPreviewRow> _visibleRows = [];
    private string _applicationMessage = "", _loadWarning = "";
    private Filter _filter;
    private bool _busy, _closed;

    internal static void Open(AppController app, Window? owner = null, SortingRule? draftRule = null)
    {
        using var pause = app.PauseAutomaticSortingForPreview();
        var dialog = new SortingPreviewWindow(app, draftRule);
        var manager = app.Manager is { IsVisible: true, WindowState: not WindowState.Minimized } visible ? visible : null;
        var effectiveOwner = owner is { IsVisible: true } ? owner : (Window?)manager;
        if (effectiveOwner != null) { dialog.Owner = effectiveOwner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        dialog.ShowDialog();
    }

    private SortingPreviewWindow(AppController app, SortingRule? draftRule)
    {
        _app = app; _draftRule = draftRule;
        Title = L.Text("Предпросмотр автосортировки"); Icon = Brand.WindowIcon; ShowInTaskbar = false;
        FontFamily = Ui.AppFont; Language = XmlLanguage.GetLanguage(L.Culture.Name);
        Width = Math.Min(1080, Math.Max(400, SystemParameters.WorkArea.Width - 40));
        Height = Math.Min(760, Math.Max(350, SystemParameters.WorkArea.Height - 40));
        MinWidth = Math.Min(720, Width); MinHeight = Math.Min(500, Height); ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Ui.WindowFrame(this);
        SetResourceReference(BackgroundProperty, "Paper"); SetResourceReference(ForegroundProperty, "Ink");
        AutomationProperties.SetAutomationId(this, "sorting-preview");

        var shell = new DockPanel();
        var title = new DockPanel { Height = 40, Margin = new Thickness(15, 0, 7, 0) };
        var close = Ui.IconButton("close", Close, L.Text("Закрыть")); close.Margin = new Thickness(0); close.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(close, Dock.Right); title.Children.Add(close); title.Children.Add(Ui.Text(Title, 13, Ui.Ink, FontWeights.SemiBold));
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
        header.Children.Add(_scope);
        var hint = Ui.Text(L.Text("Посмотрите назначения до применения. На рабочем столе ничего не перемещается."), 12, Ui.Muted);
        hint.Margin = new Thickness(0, 5, 0, 10); header.Children.Add(hint);
        if (_draftRule != null)
        {
            var draftHint = Ui.Text(L.Text("Предпросмотр черновика. Для применения сначала сохраните правило."), 12, Ui.Accent);
            draftHint.Margin = new Thickness(0, 0, 0, 10); header.Children.Add(draftHint);
        }
        _query.ToolTip = L.Text("Поиск по файлу, группе или правилу");
        AutomationProperties.SetName(_query, (string)_query.ToolTip); AutomationProperties.SetAutomationId(_query, "sorting-preview-query");
        header.Children.Add(_query);
        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
        foreach (var filter in Enum.GetValues<Filter>())
        {
            var item = filter; var button = Ui.Segment(FilterLabel(filter), () => { _filter = item; FilterRows(); }, filter == Filter.All);
            _filters.Add(filter, button); chips.Children.Add(button);
        }
        header.Children.Add(chips); _summary.Margin = new Thickness(0, 0, 0, 8); header.Children.Add(_summary);

        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        _refresh = Ui.Button(L.Text("Обновить"), () => _ = RefreshAsync(), id: "sorting-preview-refresh"); actions.Children.Add(_refresh);
        _apply = Ui.Button(L.Text("Применить"), Apply, true, "sorting-preview-apply"); _apply.Margin = new Thickness(8, 0, 0, 0);
        _apply.IsEnabled = false; actions.Children.Add(_apply);
        _message.TextWrapping = TextWrapping.Wrap; _message.Margin = new Thickness(0, 0, 0, 4); _message.MaxHeight = 65;
        DockPanel.SetDock(_message, Dock.Bottom); body.Children.Add(_message);
        var details = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Background = Ui.Surface,
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 10), MinHeight = 90, MaxHeight = 165,
            Child = new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        DockPanel.SetDock(details, Dock.Bottom); body.Children.Add(details);

        var table = new DockPanel(); var columns = RowGrid();
        AddColumn(columns, L.Text("Файл"), 0, Ui.Muted, 11); AddColumn(columns, L.Text("Группа / вкладка"), 1, Ui.Muted, 11);
        AddColumn(columns, L.Text("Правило"), 2, Ui.Muted, 11); AddColumn(columns, L.Text("Результат"), 3, Ui.Muted, 11);
        columns.Margin = new Thickness(12, 2, 12, 6); DockPanel.SetDock(columns, Dock.Top); table.Children.Add(columns);
        AutomationProperties.SetAutomationId(_list, "sorting-preview-list");
        var listHost = new Grid(); listHost.Children.Add(_list);
        _empty.HorizontalAlignment = HorizontalAlignment.Center; _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.TextWrapping = TextWrapping.Wrap; _empty.TextAlignment = TextAlignment.Center; _empty.MaxWidth = 400;
        _empty.Margin = new Thickness(20); _empty.Visibility = Visibility.Collapsed; _empty.IsHitTestVisible = false;
        listHost.Children.Add(_empty); table.Children.Add(listHost); body.Children.Add(table);
        Content = new Border { Child = shell, Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
        _query.TextChanged += (_, _) => { _filterDelay.Stop(); _filterDelay.Start(); };
        _filterDelay.Tick += (_, _) => { _filterDelay.Stop(); FilterRows(); };
        _list.SelectionChanged += (_, _) => ShowDetails((_list.SelectedItem as ListBoxItem)?.Tag as SortingPreviewRow);
        Loaded += async (_, _) => { _query.Focus(); await RefreshAsync(); };
        Closed += (_, _) => { _closed = true; _filterDelay.Stop(); _lifetime.Cancel(); _lifetime.Dispose(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        ShowDetails(null);
    }

    private async Task RefreshAsync()
    {
        if (_busy || _closed) return; SetBusy(true); _summary.Text = L.Text("Чтение рабочего стола…");
        var cancellation = _lifetime.Token;
        try
        {
            var workspace = WorkspaceStore.Clone(_app.Workspace);
            if (_draftRule != null)
            {
                var index = workspace.Rules.FindIndex(rule => rule.Id == _draftRule.Id);
                if (index < 0) workspace.Rules.Add(_draftRule); else workspace.Rules[index] = _draftRule;
            }
            var desktop = _app.DesktopPath; var shared = _app.PublicDesktopPath;
            _scope.Text = L.Format("Набор: {0}", workspace.Pages.FirstOrDefault(page => page.Id == workspace.ActivePageId)?.Name ?? "");
            var result = await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                var personal = FileCatalog.ReadFolderDetailed(desktop, workspace.Preferences.ShowHiddenFiles);
                var common = FileCatalog.ReadFolderDetailed(shared, workspace.Preferences.ShowHiddenFiles);
                cancellation.ThrowIfCancellationRequested();
                var rows = RuleEngine.Preview(workspace, personal.Entries.Concat(common.Entries), resolve: entry =>
                { cancellation.ThrowIfCancellationRequested(); return ShortcutReferences.Read(entry.Path); });
                return (Rows: rows, Errors: new[] { personal.Error, common.Error }.Where(error => error != null).Cast<Exception>().ToArray());
            }, cancellation);
            if (_closed) return;
            _rows = result.Rows;
            foreach (var error in result.Errors) _app.Log(error);
            _loadWarning = result.Errors.Length > 0 ? L.Text("Часть рабочего стола недоступна. Показаны только прочитанные элементы.") : "";
            UpdateMessage();
            FilterRows();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _app.Log(ex); if (_closed) return; _rows = []; FilterRows();
            _loadWarning = L.Text("Не удалось прочитать рабочий стол. Нажмите «Обновить», чтобы повторить."); UpdateMessage();
        }
        finally { if (!_closed) SetBusy(false); }
    }

    private void Apply()
    {
        if (_busy || _draftRule != null) return;
        // Flush the debounced search before committing so the button always acts
        // on the rows the current search and filter actually show.
        _filterDelay.Stop(); FilterRows();
        var reviewed = _visibleRows.Where(row => row.Status == SortingPreviewStatus.Ready).ToArray();
        if (reviewed.Length == 0) return;
        SetBusy(true);
        try
        {
            SortingPreviewApplyResult? result = null;
            _app.Mutate(() => result = RuleEngine.ApplyPreview(_app.Workspace, reviewed, entry => ShortcutReferences.Read(entry.Path)));
            _applicationMessage = L.Format("Добавлено ссылок: {0}. Пропущено после повторной проверки: {1}.", result!.Added, result.Skipped); UpdateMessage();
        }
        catch (Exception ex) { _app.Log(ex); _applicationMessage = L.Text("Ошибка сортировки: ") + ex.Message; UpdateMessage(); }
        finally { SetBusy(false); }
        _ = RefreshAsync();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy; _refresh.IsEnabled = !busy;
        UpdateApplyState();
    }

    private void UpdateApplyState()
    {
        var count = _visibleRows.Count(row => row.Status == SortingPreviewStatus.Ready);
        _apply.IsEnabled = !_busy && _draftRule == null && count > 0;
        _apply.Content = L.Format("Применить ({0})", count);
    }

    private void UpdateMessage() => _message.Text = string.Join(Environment.NewLine, new[] { _applicationMessage, _loadWarning }.Where(text => text.Length > 0));

    private void FilterRows()
    {
        if (_closed) return;
        var selected = ((_list.SelectedItem as ListBoxItem)?.Tag as SortingPreviewRow)?.Entry.Path;
        var words = _query.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var visible = _rows.Where(row => Accept(row) && words.All(word =>
            (row.Entry.Name + " " + row.Entry.Path + " " + row.Destination?.FenceName + " " + row.Destination?.TabName + " " + row.Rule?.Name)
                .Contains(word, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        _visibleRows = visible;
        _list.Items.Clear(); ListBoxItem? first = null;
        foreach (var row in visible)
        {
            var grid = RowGrid(); var name = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
            var icon = Brand.Icon(row.Entry.IsDirectory ? "folder" : "file", Ui.Muted, 17); icon.Margin = new Thickness(0, 0, 8, 0); name.Children.Add(icon);
            var label = Ui.Text(row.Entry.Name, 12, Ui.Ink, FontWeights.Medium); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; name.Children.Add(label); grid.Children.Add(name);
            AddColumn(grid, row.Destination == null ? "—" : row.Destination.FenceName + " / " + row.Destination.TabName, 1, Ui.Ink, 11);
            AddColumn(grid, row.Rule?.Name ?? "—", 2, Ui.Muted, 11); AddColumn(grid, StatusLabel(row.Status), 3, StatusColor(row.Status), 11);
            var item = new ListBoxItem { Content = grid, Tag = row, Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0, 1, 0, 1), ToolTip = row.Entry.Path, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(item, row.Entry.Name + ", " + StatusLabel(row.Status)); _list.Items.Add(item); first ??= item;
            if (row.Entry.Path == selected) _list.SelectedItem = item;
        }
        if (_list.SelectedItem == null && first != null) _list.SelectedItem = first;
        if (first == null) ShowDetails(null);
        var ready = visible.Count(row => row.Status == SortingPreviewStatus.Ready);
        var problems = visible.Count(IsProblem);
        _summary.Text = _rows.Count == 0 ? L.Text("На рабочем столе нет доступных элементов.")
            : L.Format("Показано: {0} из {1} · Будет добавлено: {2} · Проблемы: {3}", visible.Length, _rows.Count, ready, problems);
        _empty.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Text = _rows.Count == 0 ? L.Text("На рабочем столе нет доступных элементов.") : L.Text("Ничего не найдено. Измените поиск или фильтр.");
        foreach (var pair in _filters) pair.Value.Tag = pair.Key == _filter ? "selected" : null;
        UpdateApplyState();
    }

    private bool Accept(SortingPreviewRow row) => _filter switch
    {
        Filter.Ready => row.Status == SortingPreviewStatus.Ready,
        Filter.Problems => IsProblem(row), Filter.AlreadyAdded => row.Status == SortingPreviewStatus.AlreadyAdded,
        Filter.NoRule => row.Status == SortingPreviewStatus.NoRule, _ => true
    };
    private static bool IsProblem(SortingPreviewRow row) => row.Status is SortingPreviewStatus.InvalidTarget or SortingPreviewStatus.MissingSource or SortingPreviewStatus.MissingReference or SortingPreviewStatus.ReferenceError;

    private void ShowDetails(SortingPreviewRow? row)
    {
        _details.Children.Clear();
        if (row == null) { Detail(L.Text("Выберите файл, чтобы увидеть правило и точное назначение."), Ui.Muted); return; }
        Detail(row.Entry.Name + " · " + StatusLabel(row.Status), StatusColor(row.Status), true);
        Detail(L.Format("Источник: {0}", row.Entry.Path), Ui.Muted);
        if (row.Reference != null && !PathExtensions.Same(row.Entry.Path, row.Reference.Path)) Detail(L.Format("Цель ярлыка: {0}", row.Reference.Path), Ui.Muted);
        if (row.Reference?.Launch is { Arguments.Length: > 0 } launch) Detail(L.Format("Аргументы: {0}", launch.Arguments), Ui.Muted);
        if (row.Destination != null) Detail(L.Format("Назначение: {0} / {1}", row.Destination.FenceName, row.Destination.TabName), Ui.Ink);
        if (row.DuplicateInPreview) Detail(L.Text("Тот же объект уже назначен другой строкой предпросмотра."), Ui.Muted);
        else if (row.Status == SortingPreviewStatus.AlreadyAdded) Detail(L.Text("Ссылка уже есть в текущем наборе. Ручное назначение сохраняется."), Ui.Muted);
        if (row.Rule != null) Detail(L.Format("Правило: {0} · {1}", row.Rule.Name, RuleDescription(row.Rule)), Ui.Ink);
        if (row.Status == SortingPreviewStatus.Ready) Detail(L.Text("Первое подходящее доступное правило. Будет добавлена ссылка, исходный файл останется на месте."), Ui.Muted);
        if (row.Status == SortingPreviewStatus.NoRule) Detail(L.Text("Ни одно включённое правило не подходит. Этот элемент останется без изменений."), Ui.Muted);
        foreach (var issue in row.SkippedRules ?? []) Detail(L.Format("Пропущено правило «{0}»: {1}", issue.Rule.Name, ProblemLabel(issue.Problem)), Ui.Muted);
        if (row.Error != null) Detail(row.Error, Ui.Muted);
    }

    private void Detail(string text, Brush color, bool strong = false)
    { var label = Ui.Text(text, strong ? 12 : 11, color, strong ? FontWeights.SemiBold : FontWeights.Normal); label.Margin = new Thickness(0, 0, 0, 4); label.TextWrapping = TextWrapping.Wrap; _details.Children.Add(label); }
    private static Grid RowGrid()
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(.8, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(142) }); return row;
    }
    private static void AddColumn(Grid row, string text, int column, Brush color, double size)
    {
        var label = Ui.Text(text, size, color); label.Margin = new Thickness(0, 0, 8, 0); label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis; label.ToolTip = text; Grid.SetColumn(label, column); row.Children.Add(label);
    }
    private static string RuleDescription(SortingRule rule)
        => rule.DesktopCategory != null ? L.Format("Категория: {0}", L.Text(DesktopOrganizer.Categories.FirstOrDefault(category => category.Id == rule.DesktopCategory)?.NameKey ?? rule.DesktopCategory))
            : Editors.RuleLabels[(int)rule.Kind] + (string.IsNullOrWhiteSpace(rule.Pattern) ? "" : ": " + rule.Pattern);
    private static Brush StatusColor(SortingPreviewStatus status) => status == SortingPreviewStatus.Ready ? Ui.Accent : status == SortingPreviewStatus.AlreadyAdded ? Ui.Muted : status == SortingPreviewStatus.NoRule ? Ui.Ink : Ui.Brush("#E7BD82");
    private static string FilterLabel(Filter filter) => L.Text(filter switch { Filter.Ready => "Будут добавлены", Filter.Problems => "Проблемы", Filter.AlreadyAdded => "Уже добавлены", Filter.NoRule => "Без правила", _ => "Все" });
    private static string StatusLabel(SortingPreviewStatus status) => L.Text(status switch
    {
        SortingPreviewStatus.Ready => "Будет добавлено", SortingPreviewStatus.AlreadyAdded => "Уже добавлено", SortingPreviewStatus.NoRule => "Нет правила",
        SortingPreviewStatus.InvalidTarget => "Нет назначения", SortingPreviewStatus.MissingSource => "Файл недоступен", SortingPreviewStatus.MissingReference => "Цель недоступна", _ => "Ошибка чтения"
    });
    private static string ProblemLabel(SortingTargetProblem problem) => L.Text(problem switch
    {
        SortingTargetProblem.GroupMissing => "Группа удалена", SortingTargetProblem.OtherSet => "Группа в другом наборе",
        SortingTargetProblem.ContentLocked => "Содержимое группы защищено", SortingTargetProblem.TabMissing => "Вкладка удалена", _ => "В портал нельзя добавлять ссылки"
    });
}
