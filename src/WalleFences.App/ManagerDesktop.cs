using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private string? _selectedFenceId, _selectedMonitorId;
    private Border? _desktopMonitors;
    private StackPanel? _desktopRows;
    private Button? _toolbarOverflow;
    private bool _toolbarCompact;
    private Action? _refreshCaption;
    private Action? _refreshToolbar;
    private Grid? _setTabsView;
    private string? _setTabsSignature;
    private readonly Dictionary<string, Button> _setTabButtons = [];
    private ComboBox? _setSelector;
    private StackPanel? _setTabStrip;
    private TabSelectionMotion? _setSelectionMotion;
    private bool _animateSetSelection, _updatingSetSelector;
    private Fence? SelectedFence => _app.Workspace.Fences.FirstOrDefault(f => f.Id == _selectedFenceId && f.PageId == _app.Workspace.ActivePageId);

    private UIElement Titlebar()
    {
        var title = new Grid { Background = Ui.Paper };
        title.ColumnDefinitions.Add(new() { Width = new GridLength(180) }); title.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); title.ColumnDefinitions.Add(new() { Width = new GridLength(114) });
        var wordmark = Ui.Text("Walle Fences", 11, Ui.Ink, FontWeights.SemiBold); wordmark.Margin = new Thickness(8, 0, 0, 0); var brand = Ui.Row(Brand.Logo(28), wordmark); brand.Margin = new Thickness(16, 0, 0, 0); title.Children.Add(brand);
        title.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && AncestorButton(source) != null) return;
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        };
        var search = Ui.Button("", _app.ShowSearch, id: "manager-search"); search.Height = 28; search.Width = 360; search.MaxWidth = 390; search.HorizontalAlignment = HorizontalAlignment.Center; search.HorizontalContentAlignment = HorizontalAlignment.Stretch; search.Padding = new Thickness(10, 0, 8, 0); search.Margin = new Thickness(16, 0, 16, 0); search.ToolTip = L.Text("Поиск и команды") + " · Ctrl + K";
        var searchContent = new Grid(); searchContent.ColumnDefinitions.Add(new() { Width = new GridLength(22) }); searchContent.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); searchContent.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        searchContent.Children.Add(Brand.Icon("search", Ui.Muted)); var prompt = Ui.Text(L.Text("Поиск и команды"), 10, Ui.Muted); prompt.TextWrapping = TextWrapping.NoWrap; prompt.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(prompt, 1); searchContent.Children.Add(prompt);
        var shortcut = Ui.Text("Ctrl  K", 9, Ui.Muted); shortcut.Margin = new Thickness(36, 0, 0, 0); Grid.SetColumn(shortcut, 2); searchContent.Children.Add(shortcut); search.Content = searchContent; AutomationProperties.SetName(search, L.Text("Поиск и команды")); Grid.SetColumn(search, 1); title.Children.Add(search);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button Caption(string icon, string label, Action action, string id)
        {
            var button = Ui.Button("", action, id: id); button.Content = Brand.Icon(icon, Ui.Ink, 16); button.Width = 38; button.Height = 40; button.MinWidth = 0; button.MinHeight = 0; button.Margin = new Thickness(0); button.Padding = new Thickness(0); button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.ToolTip = L.Text(label); AutomationProperties.SetName(button, L.Text(label)); return button;
        }
        buttons.Children.Add(Caption("minimize", "Свернуть", () => WindowState = WindowState.Minimized, "manager-minimize"));
        var maximize = Caption("maximize", "Развернуть окно", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, "manager-maximize"); buttons.Children.Add(maximize);
        _refreshCaption = () => { maximize.Content = Brand.Icon(WindowState == WindowState.Maximized ? "restore" : "maximize", Ui.Ink, 16); var label = L.Text(WindowState == WindowState.Maximized ? "Восстановить размер окна" : "Развернуть окно"); maximize.ToolTip = label; AutomationProperties.SetName(maximize, label); }; _refreshCaption();
        buttons.Children.Add(Caption("close", "Закрыть", Close, "manager-close")); Grid.SetColumn(buttons, 2); title.Children.Add(buttons);
        return new Border { Child = title, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }
    private static Button? AncestorButton(DependencyObject source)
    {
        for (var current = source; current != null; current = current is Visual || current is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is Button button) return button;
        return null;
    }

    private void DesktopPage()
    {
        Intro(L.Text("Всё нужное — у края экрана"));
        var active = _app.Workspace.Fences.Where(f => f.PageId == _app.Workspace.ActivePageId).ToList();
        if (!active.Any(f => f.Id == _selectedFenceId)) _selectedFenceId = active.FirstOrDefault()?.Id;
        if (_selectedMonitorId == null && SelectedFence is { } selected) _selectedMonitorId = DisplayLayout.Select(selected, AppController.Displays()).Id;
        _desktopMonitors = new Border { Margin = new Thickness(0, 1, 0, 16), Child = MonitorPreviews() }; AutomationProperties.SetAutomationId(_desktopMonitors, "desktop-monitor-previews"); _page.Children.Add(_desktopMonitors);
        _page.Children.Add(GroupToolbar());
        _desktopRows = new StackPanel(); AutomationProperties.SetAutomationId(_desktopRows, "group-table");
        _page.Children.Add(new Border { Child = _desktopRows, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), ClipToBounds = true });
        RenderDesktopRows(); RenderInspector();
    }

    private UIElement SetTabs()
    {
        var signature = System.Text.Json.JsonSerializer.Serialize(new { Language = L.Language, Pages = _app.Workspace.Pages.Select(page => new { page.Id, page.Name }) });
        if (_setTabsView != null && _setTabsSignature == signature)
        {
            UpdateSetSelection(_animateSetSelection); _animateSetSelection = false; return _setTabsView;
        }
        _setTabsSignature = signature; _setTabButtons.Clear();
        var row = new Grid { Margin = new Thickness(0, 0, 0, 16) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        AutomationProperties.SetAutomationId(row, "set-controls");
        var tabs = new StackPanel { Orientation = Orientation.Horizontal }; _setTabStrip = tabs;
        var strip = new Grid(); var canvas = new Canvas { IsHitTestVisible = false }; strip.Children.Add(canvas);
        var selection = new Border { Background = Ui.AccentWash, CornerRadius = new CornerRadius(5), BorderBrush = Ui.FocusAccent, BorderThickness = new Thickness(0, 0, 0, 1.5) }; AutomationProperties.SetAutomationId(selection, "set-active-indicator"); canvas.Children.Add(selection); _setSelectionMotion = new TabSelectionMotion(selection); strip.Children.Add(tabs);
        foreach (var page in _app.Workspace.Pages)
        {
            var tab = Ui.Segment(page.Name, () => { try { _app.SwitchPage(page.Id); } finally { _animateSetSelection = false; } }, false, "set-tab-" + page.Id); tab.MinWidth = 76; tab.MaxWidth = 150; tab.Height = 32; tab.Padding = new Thickness(12, 5, 12, 5); tab.Margin = new Thickness(0); tab.Content = Ellipsis(page.Name, 11, page.Id == _app.Workspace.ActivePageId ? Ui.Ink : Ui.Muted); tab.ToolTip = page.Name; AutomationProperties.SetName(tab, page.Name); tab.PreviewMouseLeftButtonDown += (_, _) => _animateSetSelection = true; tab.PreviewKeyDown += (_, _) => _animateSetSelection = false; tabs.Children.Add(tab); _setTabButtons.Add(page.Id, tab);
        }
        var scroller = new ScrollViewer { Content = new Border { Child = strip, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true }, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; AutomationProperties.SetAutomationId(scroller, "set-tab-scroll");
        scroller.PreviewMouseWheel += (_, e) => { if (scroller.ScrollableWidth <= 0) return; scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta); e.Handled = true; }; row.Children.Add(scroller);
        var controls = Ui.Row(); controls.Margin = new Thickness(8, 0, 0, 0);
        // The compact dropdown remains keyboard accessible when a set is outside the strip.
        var select = new ComboBox { Width = 32, Height = 32, MinWidth = 0, ItemsSource = _app.Workspace.Pages.Select(p => p.Name).ToList(), SelectedIndex = _app.Workspace.Pages.FindIndex(p => p.Id == _app.Workspace.ActivePageId), Margin = new Thickness(0, 0, 6, 0), ToolTip = L.Text("Все наборы") };
        select.ItemContainerStyle = new Style(typeof(ComboBoxItem), (Style)Application.Current.FindResource(typeof(ComboBoxItem))) { Setters = { new Setter(MinWidthProperty, 150.0) } }; AutomationProperties.SetAutomationId(select, "set-selector"); AutomationProperties.SetName(select, L.Text("Все наборы"));
        _setSelector = select; select.SelectionChanged += (_, _) => { if (_updatingSetSelector || select.SelectedIndex < 0 || select.SelectedIndex >= _app.Workspace.Pages.Count) return; _animateSetSelection = false; _app.SwitchPage(_app.Workspace.Pages[select.SelectedIndex].Id); }; controls.Children.Add(select);
        var add = CompactAction("Новый набор", "plus", () => SetEditor.Create(_app), "new-set-inline"); controls.Children.Add(add); Grid.SetColumn(controls, 1); row.Children.Add(controls); _setTabsView = row;
        row.Loaded += (_, _) => { if (ReferenceEquals(_setTabsView, row) && _setSelectionMotion?.IsAnimating != true) UpdateSetSelection(false); };
        tabs.SizeChanged += (_, _) => { if (ReferenceEquals(_setTabStrip, tabs)) UpdateSetSelection(_setSelectionMotion?.IsAnimating == true); };
        return row;
    }
    private void UpdateSetSelection(bool animate)
    {
        if (_setTabStrip == null || _setSelectionMotion == null) return;
        foreach (var (id, button) in _setTabButtons)
        {
            var selected = id == _app.Workspace.ActivePageId;
            if (button.Content is TextBlock label) { label.Foreground = selected ? Ui.Ink : Ui.Muted; label.FontFamily = selected ? Ui.SemiboldFont : Ui.AppFont; label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal; }
        }
        if (_setTabButtons.TryGetValue(_app.Workspace.ActivePageId, out var active) && active.ActualWidth > 0)
        {
            var bounds = active.TransformToAncestor(_setTabStrip).TransformBounds(new Rect(0, 0, active.ActualWidth, active.ActualHeight));
            _setSelectionMotion.MoveTo(bounds, animate); active.BringIntoView();
        }
        else _setSelectionMotion.Hide();
        if (_setSelector != null)
        {
            _updatingSetSelector = true;
            try { _setSelector.SelectedIndex = _app.Workspace.Pages.FindIndex(page => page.Id == _app.Workspace.ActivePageId); }
            finally { _updatingSetSelector = false; }
        }
    }
    private UIElement GroupToolbar()
    {
        var toolbar = new Grid { Height = 32, Margin = new Thickness(0, 0, 0, 8) }; AutomationProperties.SetAutomationId(toolbar, "group-toolbar");
        toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var heading = Ellipsis(L.Text("Группы в наборе"), 16); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 10, 0); heading.ToolTip = heading.Text; toolbar.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(actions, 1); toolbar.Children.Add(actions);
        var add = Ui.Button("", () => Editors.Fence(_app), id: "new-fence"); add.Height = 32; add.MinWidth = 32; add.Padding = new Thickness(8, 0, 8, 0); add.Margin = new Thickness(0, 0, 2, 0); add.ToolTip = L.Text("Новая группа"); AutomationProperties.SetName(add, L.Text("Новая группа")); actions.Children.Add(add);
        var widget = Ui.Button("", () => _app.AddWidgetPanel(this, display: AppController.Displays().FirstOrDefault(display => display.Id == _selectedMonitorId)), id: "new-widget");
        widget.Height = 32; widget.MinWidth = 32; widget.Margin = new Thickness(0, 0, 2, 0); widget.ToolTip = L.Text("Добавить виджет"); AutomationProperties.SetName(widget, L.Text("Добавить виджет")); actions.Children.Add(widget);
        actions.Children.Add(CompactAction("Портал папки", "folder-add", () => Editors.NewPortal(_app), "new-portal"));
        var secondary = new[] {
            CompactAction("Нарисовать новую группу на рабочем столе", "draw", () => DrawFenceWindow.Draw(_app), "draw-fence"),
            CompactAction("Отменить изменение", "undo", _app.Undo, "undo"),
            CompactAction("Свернуть все", "collapse", () => _app.CollapseAll(true), "collapse-all"),
            CompactAction("Развернуть все", "expand", () => _app.CollapseAll(false), "expand-all") };
        foreach (var button in secondary) actions.Children.Add(button);
        var visibility = CompactAction(_app.Hidden ? "Показать группы" : "Скрыть группы", _app.Hidden ? "eye" : "eye-off", _app.ToggleHidden, "toggle-hidden"); actions.Children.Add(visibility);
        var more = CompactAction("Дополнительные действия", "more", ShowLayoutMenu, "layout-actions"); more.Margin = new Thickness(0); _toolbarOverflow = more; actions.Children.Add(more);
        _refreshToolbar = () =>
        {
            secondary[1].IsEnabled = _app.CanUndo;
            visibility.Content = Brand.Icon(_app.Hidden ? "eye" : "eye-off", Ui.Muted); var name = L.Text(_app.Hidden ? "Показать группы" : "Скрыть группы"); visibility.ToolTip = name; AutomationProperties.SetName(visibility, name);
        }; _refreshToolbar();
        void Fit(double available)
        {
            _toolbarCompact = available < 520;
            foreach (var button in secondary) button.Visibility = _toolbarCompact ? Visibility.Collapsed : Visibility.Visible;
            add.Content = _toolbarCompact ? Brand.Icon("plus", Ui.Ink) : IconText("plus", L.Text("Группа"));
            add.Padding = new Thickness(_toolbarCompact ? 0 : 8, 0, _toolbarCompact ? 0 : 8, 0); add.Width = _toolbarCompact ? 32 : double.NaN;
            widget.Content = _toolbarCompact ? Brand.Icon("widgets", Ui.Ink) : IconText("widgets", L.Text("Виджет"));
            widget.Padding = new Thickness(_toolbarCompact ? 0 : 8, 0, _toolbarCompact ? 0 : 8, 0); widget.Width = _toolbarCompact ? 32 : double.NaN;
            more.ContextMenu = CreateLayoutMenu(_toolbarCompact); more.ContextMenu.PlacementTarget = more;
        }
        Fit(Math.Max(0, (ActualWidth > 0 ? ActualWidth : Width) - (_app.Workspace.Preferences.CompactNavigation ? 64 : 180) - (ActualWidth > 0 && ActualWidth < 1010 ? 260 : 280) - 40));
        toolbar.SizeChanged += (_, e) => Fit(e.NewSize.Width); return toolbar;
    }
    private Button CompactAction(string label, string icon, Action action, string? id = null)
    {
        var button = Ui.IconButton(Brand.Icon(icon, Ui.Muted), action, L.Text(label), id); button.Width = 32; button.Height = 32; button.Margin = new Thickness(0, 0, 2, 0); return button;
    }
    private static Grid IconText(string icon, string label, Brush? color = null, double size = 16)
    {
        var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new GridLength(size + 8) }); content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var picture = Brand.Icon(icon, color ?? Ui.Ink, size); picture.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(picture);
        var text = Ellipsis(label, 11, color); Grid.SetColumn(text, 1); content.Children.Add(text); return content;
    }
    private static Border DragGrip(string id)
    {
        var grip = new Border { Width = 24, Height = 32, Background = Brushes.Transparent, Child = Brand.Icon("grip", Ui.Muted, 14), Cursor = Cursors.SizeNS, ToolTip = L.Text("Перетащите для изменения порядка") };
        AutomationProperties.SetAutomationId(grip, id); AutomationProperties.SetName(grip, L.Text("Перетащите для изменения порядка")); return grip;
    }
    private static TextBlock Ellipsis(string text, double size = 11, Brush? color = null)
    {
        var label = Ui.Text(text, size, color); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; return label;
    }
    private static Grid TableColumns()
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 76 }); row.ColumnDefinitions.Add(new() { Width = new GridLength(60) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(68) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(92) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(34) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(34) }); return row;
    }
    private static string EdgeLabel(DockEdge edge) => L.Text(edge switch { DockEdge.Top => "Сверху", DockEdge.Right => "Справа", DockEdge.Bottom => "Снизу", DockEdge.Left => "Слева", _ => "Свободно" });
    private static string RevealLabel(Fence fence) => fence.Edge == DockEdge.None && !fence.HoverExpand && Editors.AutomaticReveal(fence.RevealTrigger) ? L.Text("Вручную") : Editors.TriggerShortLabels[(int)fence.RevealTrigger];
    private void RenderDesktopRows()
    {
        if (_desktopRows == null) return; _desktopRows.Children.Clear();
        var header = TableColumns(); header.Height = 31; header.Margin = new Thickness(10, 0, 6, 0);
        foreach (var (label, column) in new[] { ("Название", 0), ("Вкладки", 1), ("Положение", 2), ("Раскрытие", 3), ("", 4) }) { var text = Ellipsis(L.Text(label), 10.5, Ui.Muted); text.ToolTip = L.Text(label); Grid.SetColumn(text, column); header.Children.Add(text); }
        _desktopRows.Children.Add(header);
        var active = _app.Workspace.Fences.Where(f => f.PageId == _app.Workspace.ActivePageId).ToList();
        foreach (var fence in active)
        {
            var row = TableColumns(); row.Height = 40; row.Margin = new Thickness(8, 0, 5, 0);
            var name = new Grid(); name.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); name.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); name.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var grip = DragGrip("group-grip-" + fence.Id); WireDragGrip(grip, "WalleFences.ManagerGroup", fence.Id, () => SelectGroup(fence.Id)); name.Children.Add(grip); var swatch = GroupIcons.Create(fence, Ui.Brush(fence.Accent), 17); swatch.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(swatch, 1); name.Children.Add(swatch); var label = Ellipsis(fence.Name, 12); label.ToolTip = fence.Name; label.Margin = new Thickness(2, 0, 8, 0); Grid.SetColumn(label, 2); name.Children.Add(label); row.Children.Add(name);
            var tabs = Ellipsis(fence.Tabs.Count.ToString(L.Culture), 12, Ui.Muted); Grid.SetColumn(tabs, 1); row.Children.Add(tabs);
            var edge = Ellipsis(EdgeLabel(fence.Edge), 12, Ui.Muted); edge.ToolTip = EdgeLabel(fence.Edge); Grid.SetColumn(edge, 2); row.Children.Add(edge);
            var reveal = Ellipsis(RevealLabel(fence), 12, Ui.Muted); reveal.ToolTip = RevealLabel(fence); Grid.SetColumn(reveal, 3); row.Children.Add(reveal);
            var visible = Ui.IconButton(Brand.Icon("eye", Ui.Muted, 16), () => _app.OpenGroup(fence), L.Text("Показать группу"), "show-" + fence.Id); visible.Width = 32; visible.Height = 32; visible.Margin = new Thickness(0); Grid.SetColumn(visible, 4); row.Children.Add(visible);
            Button more = null!; more = Ui.IconButton(Brand.Icon("more", Ui.Muted, 16), () => ShowGroupMenu(fence, more), L.Text("Действия группы"), "more-" + fence.Id); more.Width = 32; more.Height = 32; more.Margin = new Thickness(0); Grid.SetColumn(more, 5); row.Children.Add(more);
            var selected = fence.Id == _selectedFenceId;
            var container = new Button { Content = row, Background = selected ? Ui.AccentWash : Brushes.Transparent, BorderBrush = selected ? Ui.Accent : Ui.Line, BorderThickness = new Thickness(selected ? 1 : 0, 1, selected ? 1 : 0, selected ? 1 : 0), Padding = new Thickness(0), Margin = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Cursor = Cursors.Hand, AllowDrop = true };
            container.Click += (_, e) => { if (ReferenceEquals(e.OriginalSource, container)) Ui.Guard(() => SelectGroup(fence.Id)); }; AutomationProperties.SetAutomationId(container, "group-row-" + fence.Id); AutomationProperties.SetName(container, fence.Name);
            container.PreviewMouseDoubleClick += (_, e) => { if (e.OriginalSource is DependencyObject source && AncestorButton(source) is { } button && button != container) return; SelectGroup(fence.Id); Editors.Fence(_app, SelectedFence); e.Handled = true; };
            container.DragOver += (_, e) => { if (!e.Data.GetDataPresent("WalleFences.ManagerGroup")) return; e.Effects = e.Data.GetData("WalleFences.ManagerGroup") is string id && _app.Workspace.Fences.Any(f => f.Id == id && f.PageId == fence.PageId) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            container.Drop += (_, e) => { if (e.Data.GetData("WalleFences.ManagerGroup") is string id) { Ui.Guard(() => ReorderGroups(id, fence.Id)); e.Handled = true; } };
            var menu = new ContextMenu(); var edit = new MenuItem { Header = L.Text("Настроить"), Icon = Brand.Icon("settings", Ui.Ink, 16) }; edit.Click += (_, _) => Editors.Fence(_app, fence); menu.Items.Add(edit); var options = new MenuItem { Header = L.Text("Вкладки"), Icon = Brand.Icon("tabs", Ui.Ink, 16) }; options.Click += (_, _) => ShowGroupMenu(fence, container); menu.Items.Add(options); container.ContextMenu = menu;
            _desktopRows.Children.Add(container);
        }
        if (active.Count == 0) _desktopRows.Children.Add(new Border { Padding = new Thickness(16, 24, 16, 24), Child = Ui.Text(L.Text("Набор пуст. Добавьте группу, портал папки или виджеты."), 12, Ui.Muted) });
    }
    private void SelectGroup(string id)
    {
        _selectedFenceId = id; if (SelectedFence is { } fence) _selectedMonitorId = DisplayLayout.Select(fence, AppController.Displays()).Id; RefreshDesktopVisuals(); RenderInspector(); QueueGuidedTourTarget();
    }
    private void RefreshDesktopVisuals()
    {
        RenderDesktopRows(); if (_desktopMonitors != null) _desktopMonitors.Child = MonitorPreviews();
    }
    internal void ReorderGroups(string sourceId, string beforeId)
    {
        var groups = _app.Workspace.Fences; var source = groups.FirstOrDefault(f => f.Id == sourceId); var target = groups.FirstOrDefault(f => f.Id == beforeId);
        if (source == null || target == null || source == target || source.PageId != target.PageId || source.PageId != _app.Workspace.ActivePageId) return;
        var from = groups.IndexOf(source); var to = groups.IndexOf(target); if (from + 1 == to) return;
        _app.Mutate(() => { groups.RemoveAt(from); groups.Insert(from < to ? to - 1 : to, source); });
    }
    private static void WireDragGrip(UIElement grip, string format, string id, Action? click = null)
    {
        Point? start = null;
        grip.MouseLeftButtonDown += (_, e) => { start = e.GetPosition(grip); grip.CaptureMouse(); e.Handled = true; };
        grip.MouseLeftButtonUp += (_, e) => { var clicked = start != null; start = null; grip.ReleaseMouseCapture(); if (clicked) click?.Invoke(); e.Handled = true; };
        grip.MouseLeave += (_, _) => { if (Mouse.LeftButton != MouseButtonState.Pressed) { start = null; grip.ReleaseMouseCapture(); } };
        grip.LostMouseCapture += (_, _) => start = null;
        grip.MouseMove += (_, e) =>
        {
            if (start is not { } origin || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(grip); if (Math.Abs(point.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null; grip.ReleaseMouseCapture(); DragDrop.DoDragDrop(grip, new DataObject(format, id), DragDropEffects.Move);
        };
    }
    private void InspectorMutate(Action<Fence> action, bool refreshInspector = false)
    {
        if (SelectedFence is not { } fence) return;
        _suppressRefresh++;
        try { _app.Mutate(() => action(fence)); _app.SaveNow(); _app.SetStatus(L.Text("Изменения сохранены")); }
        catch { Refresh(); throw; }
        finally { _suppressRefresh--; }
        RefreshDesktopVisuals(); if (refreshInspector) RenderInspector();
    }

    private void RenderInspector()
    {
        _inspectorContentHost = null;
        _inspectorTabRows.Clear();
        var body = new StackPanel { Margin = new Thickness(18, 13, 18, 0) }; AutomationProperties.SetAutomationId(body, "group-inspector");
        if (SelectedFence is not { } fence)
        {
            body.Children.Add(Ui.Text(L.Text("Настройки группы"), 18, Ui.Ink, FontWeights.SemiBold)); var empty = Ui.Text(L.Text("Выберите группу в наборе, чтобы изменить её настройки."), 12, Ui.Muted); empty.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(empty); _inspectorHost.Child = body; return;
        }
        var heading = new DockPanel { LastChildFill = true };
        var iconChoice = Ui.IconButton(GroupIcons.Create(fence, Ui.Brush(fence.Accent), 20), () => ChooseGroupIcon(fence), L.Text("Выбрать значок группы"), "inspector-group-icon");
        iconChoice.Width = iconChoice.Height = 30; iconChoice.Margin = new Thickness(-5, 0, 5, 0); iconChoice.Padding = new Thickness(3); DockPanel.SetDock(iconChoice, Dock.Left); heading.Children.Add(iconChoice);
        var name = Ellipsis(fence.Name, 18); name.FontWeight = FontWeights.SemiBold; name.FontFamily = Ui.SemiboldFont; name.ToolTip = fence.Name; heading.Children.Add(name); body.Children.Add(heading);
        var subtitle = Ui.Text(L.Text("Настройки группы"), 10.5, Ui.Muted); subtitle.Margin = new Thickness(0, 1, 0, 6); body.Children.Add(subtitle);
        body.Children.Add(InspectorMonitorSelector(fence));
        var placement = new Grid { Height = 22 }; placement.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); placement.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); placement.Children.Add(Ellipsis(L.Text("Положение"), 11, Ui.Muted));
        var free = Ui.Button(L.Text("Свободно"), () => InspectorMutate(f => f.Edge = DockEdge.None, true), id: "inspector-edge-None"); free.FontSize = 10.5; free.Padding = new Thickness(5, 0, 5, 0); free.MinHeight = 0; free.Height = 22; free.Background = fence.Edge == DockEdge.None ? Ui.AccentWash : Brushes.Transparent; free.BorderThickness = new Thickness(0); free.Foreground = fence.Edge == DockEdge.None ? Ui.FocusAccent : Ui.Muted; free.Margin = new Thickness(5, 0, 0, 0); free.ToolTip = L.Text("Свободное положение"); AutomationProperties.SetName(free, L.Text("Свободное положение")); Grid.SetColumn(free, 1); placement.Children.Add(free); body.Children.Add(placement);
        var edges = new UniformGrid { Columns = 4, Margin = new Thickness(0, 3, -4, 5) };
        foreach (var edge in new[] { DockEdge.Top, DockEdge.Right, DockEdge.Bottom, DockEdge.Left })
        {
            var option = new StackPanel(); var border = Ui.Segment("", () => InspectorMutate(f => { f.Edge = edge; f.HoverExpand = true; f.Layer = FenceLayer.AlwaysOnTop; }, true), fence.Edge == edge, "inspector-edge-" + edge); border.Height = 29; border.MinHeight = 0; border.Margin = new Thickness(0, 0, 4, 0); border.Padding = new Thickness(0); border.BorderBrush = fence.Edge == edge ? Ui.FocusAccent : Ui.Line; border.Background = fence.Edge == edge ? Ui.AccentWash : Ui.Surface; border.Content = EdgeGlyph(edge); AutomationProperties.SetName(border, EdgeLabel(edge)); option.Children.Add(border);
            var caption = Ellipsis(EdgeLabel(edge), 10, Ui.Muted); caption.HorizontalAlignment = HorizontalAlignment.Center; caption.Margin = new Thickness(0, 3, 4, 0); option.Children.Add(caption); edges.Children.Add(option);
        }
        body.Children.Add(edges);
        var span = new EdgeSpanControl(fence, compact: true);
        span.Changed += (_, _) => Ui.Guard(() => InspectorMutate(f => span.Apply(f)));
        body.Children.Add(span);
        var thickness = InspectorSlider(body, "Полоска", 2, 12, fence.EdgeThickness, value => InspectorMutate(f => f.EdgeThickness = value), "inspector-thickness", " px"); thickness.IsEnabled = fence.Edge != DockEdge.None; ((FrameworkElement)thickness.Parent).Visibility = fence.Edge != DockEdge.None ? Visibility.Visible : Visibility.Collapsed;
        BuildRevealSettings(body, fence, thickness);
        body.Children.Add(Ui.Text(L.Text("Размер"), 11, Ui.Muted));
        var sizes = new UniformGrid { Columns = 3 };
        foreach (var (size, label) in new[] { (OpeningSize.Small, "Компактный"), (OpeningSize.Medium, "Средний"), (OpeningSize.Wide, "Большой") })
        {
            var button = Ui.Segment(L.Text(label), () => InspectorMutate(f => OpeningSizes.Apply(f, size, DisplayLayout.Select(f, AppController.Displays())), true), fence.OpeningSize == size, "inspector-size-" + size); button.Padding = new Thickness(3, 3, 3, 3); button.Margin = new Thickness(0); button.Height = 28; button.MinHeight = 0; button.FontSize = 10.5; button.Content = Ellipsis(L.Text(label), 10.5); button.ToolTip = L.Text(label); AutomationProperties.SetName(button, L.Text(label)); sizes.Children.Add(button);
        }
        body.Children.Add(new Border { Child = sizes, Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(1), Margin = new Thickness(0, 3, 0, 5) });
        var topmost = Ui.Toggle(L.Text("Всегда поверх"), fence.Layer == FenceLayer.AlwaysOnTop || fence.Edge != DockEdge.None, value => InspectorMutate(f => f.Layer = value ? FenceLayer.AlwaysOnTop : FenceLayer.Desktop), "inspector-topmost"); topmost.Margin = new Thickness(0); topmost.Height = 28; topmost.FontSize = 11; topmost.IsEnabled = fence.Edge == DockEdge.None; if (fence.Edge != DockEdge.None) { topmost.ToolTip = L.Text("Полоски у края всегда поверх окон"); ToolTipService.SetShowOnDisabled(topmost, true); AutomationProperties.SetHelpText(topmost, L.Text("Полоски у края всегда поверх окон")); } body.Children.Add(topmost);
        var locked = Ui.Toggle(L.Text("Закрепить позицию"), fence.Locked, value => InspectorMutate(f => f.Locked = value), "inspector-locked"); locked.Margin = new Thickness(0); locked.Height = 28; locked.FontSize = 11; body.Children.Add(locked);
        InspectorSlider(body, "Непрозрачность", 15, 100, fence.Opacity * 100, value => InspectorMutate(f => f.Opacity = value / 100), "inspector-opacity", "%", 91);
        body.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 6, 0, 6) });
        var visibilityRow = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 5) }; visibilityRow.ColumnDefinitions.Add(new() { Width = new GridLength(70) }); visibilityRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var visibilityHeader = Ellipsis(L.Text("Вкладки"), 11, Ui.Muted); visibilityHeader.ToolTip = L.Text("Показывать вкладки"); visibilityRow.Children.Add(visibilityHeader);
        var tabVisibility = new ComboBox { ItemsSource = Editors.TabVisibilityLabels, SelectedIndex = (int)Editors.VisibleTabPolicy(fence), FontSize = 11, Height = 28, Margin = new Thickness(0) }; AutomationProperties.SetName(tabVisibility, L.Text("Показывать вкладки")); Grid.SetColumn(tabVisibility, 1); visibilityRow.Children.Add(tabVisibility); body.Children.Add(visibilityRow);
        tabVisibility.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(ComboBox.SelectedItem)) { Source = tabVisibility });
        AutomationProperties.SetAutomationId(tabVisibility, "inspector-tab-visibility");
        var tabHint = Ui.Text(L.Text("Скрытые вкладки доступны в меню группы и через Ctrl+Tab."), 10.5, Ui.Muted); tabHint.Margin = new Thickness(0, 0, 0, 6); tabHint.Visibility = Editors.VisibleTabPolicy(fence) == TabVisibility.Hidden ? Visibility.Visible : Visibility.Collapsed; AutomationProperties.SetAutomationId(tabHint, "inspector-tab-hint"); body.Children.Add(tabHint);
        tabVisibility.SelectionChanged += (_, _) =>
        {
            if (tabVisibility.SelectedIndex < 0) return;
            Ui.Guard(() => InspectorMutate(f => { f.TabVisibility = (TabVisibility)tabVisibility.SelectedIndex; if (f.TabStyle == TabStyle.MenuOnly) f.TabStyle = TabStyle.Flat; }));
            tabHint.Visibility = tabVisibility.SelectedIndex == (int)TabVisibility.Hidden ? Visibility.Visible : Visibility.Collapsed;
        };
        var contentTab = InspectorContentTab(fence);
        foreach (var tab in fence.Tabs)
        {
            var row = new Grid { Height = 32 }; row.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(32) }); var grip = DragGrip("tab-grip-" + tab.Id); grip.Height = 32; grip.Cursor = fence.ContentLocked ? Cursors.Arrow : Cursors.SizeNS; grip.IsEnabled = !fence.ContentLocked; WireDragGrip(grip, "WalleFences.ManagerTab", tab.Id); row.Children.Add(grip);
            var selectTab = Ui.Button("", () => SelectInspectorContentTab(fence.Id, tab.Id), id: "inspector-tab-content-select-" + tab.Id);
            selectTab.Content = Ellipsis(tab.Name, 11); selectTab.ToolTip = L.Text("Показать содержимое вкладки"); selectTab.Padding = new Thickness(3, 0, 4, 0); selectTab.Margin = new Thickness(0); selectTab.Height = 32; selectTab.MinHeight = 0; selectTab.Background = Brushes.Transparent; selectTab.BorderThickness = new Thickness(0); selectTab.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(selectTab, tab.Name); Grid.SetColumn(selectTab, 1); row.Children.Add(selectTab);
            var tabMenu = CreateTabActionsMenu(fence, tab);
            var edit = Ui.IconButton(Brand.Icon("more", Ui.Muted, 15), () => { }, L.Text("Действия с вкладкой"), "inspector-tab-" + tab.Id); edit.Height = 32; edit.Width = 32; edit.Margin = new Thickness(0); Grid.SetColumn(edit, 2); row.Children.Add(edit);
            edit.Click += (_, _) => Ui.Guard(() => OpenTabActionsMenu(tabMenu, edit));
            var tabContainer = new Border { Child = row, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 1, 0), Margin = new Thickness(0, 3, 0, 0), AllowDrop = !fence.ContentLocked }; AutomationProperties.SetAutomationId(tabContainer, "inspector-tab-row-" + tab.Id);
            tabContainer.ContextMenu = tabMenu;
            tabContainer.Background = tab.Id == contentTab.Id ? Ui.AccentWash : Brushes.Transparent;
            tabContainer.BorderBrush = tab.Id == contentTab.Id ? Ui.FocusAccent : Ui.Line;
            _inspectorTabRows[tab.Id] = tabContainer;
            tabContainer.Focusable = true;
            AutomationProperties.SetName(tabContainer, tab.Name);
            AutomationProperties.SetHelpText(tabContainer, L.Text("Откройте действия правой кнопкой мыши или кнопкой «…»."));
            tabContainer.DragOver += (_, e) => { if (!e.Data.GetDataPresent("WalleFences.ManagerTab")) return; e.Effects = e.Data.GetData("WalleFences.ManagerTab") is string id && fence.Tabs.Any(t => t.Id == id) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            tabContainer.Drop += (_, e) => { if (e.Data.GetData("WalleFences.ManagerTab") is string id) { Ui.Guard(() => InspectorMutate(f => WorkspaceActions.MoveTab(_app.Workspace, f.Id, id, f.Id, f.Tabs.FindIndex(t => t.Id == tab.Id)), true)); e.Handled = true; } }; body.Children.Add(tabContainer);
        }
        var add = Ui.Button("", () => Editors.Tab(_app, fence), id: "inspector-add-tab"); add.Content = IconText("plus", L.Text("Добавить вкладку"), Ui.Muted, 14); add.Padding = new Thickness(3, 2, 3, 2); add.Height = 32; add.MinHeight = 0; add.BorderThickness = new Thickness(0); add.Background = Brushes.Transparent; add.Foreground = Ui.Muted; add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 3, 0, 6); add.ToolTip = L.Text("Добавить вкладку"); AutomationProperties.SetName(add, L.Text("Добавить вкладку")); add.IsEnabled = !fence.ContentLocked; body.Children.Add(add);
        _inspectorContentHost = new Border { Margin = new Thickness(0, 3, 0, 8) };
        AutomationProperties.SetAutomationId(_inspectorContentHost, "inspector-tab-content");
        body.Children.Add(_inspectorContentHost);
        RefreshInspectorTabContent();
        var contentMenu = new ContextMenu();
        void AddContent(bool programs)
        {
            var current = _app.Workspace.Fences.FirstOrDefault(candidate => candidate.Id == fence.Id) ?? throw new InvalidOperationException(L.Text("Группа удалена"));
            WorkspaceActions.RequireEditable(current);
            var targetTab = InspectorContentTab(current);
            _app.OpenGroup(current, targetTab);
            var window = _app.Windows.FirstOrDefault(candidate => candidate.Fence.Id == current.Id);
            if (window == null) throw new InvalidOperationException(L.Text("Группа удалена"));
            if (programs) Editors.AddPrograms(window, _app); else Editors.AddFiles(window, _app);
        }
        var files = new MenuItem { Header = L.Text("Добавить файлы…"), Icon = Brand.Icon("file", Ui.Ink, 16) }; files.Click += (_, _) => Ui.Guard(() => AddContent(false)); contentMenu.Items.Add(files);
        var applications = new MenuItem { Header = L.Text("Добавить программы…"), Icon = Brand.Icon("desktop", Ui.Ink, 16), IsEnabled = string.IsNullOrEmpty(contentTab.PortalPath) }; applications.Click += (_, _) => Ui.Guard(() => AddContent(true)); ToolTipService.SetShowOnDisabled(applications, true); contentMenu.Items.Add(applications);
        var widget = new MenuItem { Header = L.Text("Добавить виджет…"), Icon = Brand.Icon("widgets", Ui.Ink, 16), ToolTip = L.Text("Каждый виджет появится отдельной панелью в текущем наборе.") };
        AutomationProperties.SetAutomationId(widget, "inspector-add-widget");
        widget.Click += (_, _) => Ui.Guard(() =>
        {
            var current = _app.Workspace.Fences.FirstOrDefault(candidate => candidate.Id == fence.Id);
            _app.AddWidgetPanel(this, current);
        });
        contentMenu.Items.Add(widget);
        contentMenu.Opened += (_, _) =>
        {
            var current = _app.Workspace.Fences.FirstOrDefault(candidate => candidate.Id == fence.Id);
            files.IsEnabled = current is { ContentLocked: false };
            applications.IsEnabled = current is { ContentLocked: false } && string.IsNullOrEmpty(InspectorContentTab(current).PortalPath);
            applications.ToolTip = applications.IsEnabled ? null : L.Text("В портал нельзя добавить ссылку: выберите обычную вкладку.");
            if (current != null) FenceMenuTheme.Apply(contentMenu, current, InspectorContentTab(current));
        };
        var addContent = Ui.Button(L.Text("Добавить содержимое…"), () => { contentMenu.PlacementTarget = _inspectorHost; contentMenu.Placement = PlacementMode.MousePoint; contentMenu.IsOpen = true; }, id: "inspector-add-content");
        addContent.FontSize = 11; addContent.Padding = new Thickness(7, 5, 7, 5); addContent.Margin = new Thickness(0, 0, 0, 7); body.Children.Add(addContent);
        var actions = new Grid(); actions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var advanced = Ui.Button(L.Text("Все настройки…"), () => Editors.Fence(_app, SelectedFence), id: "edit-" + fence.Id); advanced.FontSize = 11; advanced.Padding = new Thickness(6, 6, 6, 6); advanced.Margin = new Thickness(0, 0, 5, 0); advanced.ToolTip = L.Text("Все настройки…"); actions.Children.Add(advanced);
        var save = Ui.Button(L.Text("Сохранить"), () => { _app.SaveNow(); _app.SetStatus(L.Text("Изменения сохранены")); }, true, "inspector-save"); save.FontSize = 11; save.Padding = new Thickness(12, 6, 12, 6); save.Margin = new Thickness(0); Grid.SetColumn(save, 1); actions.Children.Add(save);
        var inspector = new Grid(); inspector.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); inspector.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; AutomationProperties.SetAutomationId(scroll, "inspector-scroll"); inspector.Children.Add(scroll);
        var actionDock = new Border { Child = actions, Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 9, 18, 12) }; AutomationProperties.SetAutomationId(actionDock, "inspector-actions"); Grid.SetRow(actionDock, 1); inspector.Children.Add(actionDock);
        _inspectorHost.Child = inspector;
    }
    private Slider InspectorSlider(Panel panel, string label, double min, double max, double value, Action<double> commitValue, string id, string suffix, double labelWidth = 70)
    {
        var labelText = Ellipsis(L.Text(label), 11, Ui.Muted); labelText.ToolTip = L.Text(label);
        var measured = new FormattedText(labelText.Text, L.Culture, FlowDirection.LeftToRight, new Typeface(labelText.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 11, Ui.Muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(Math.Max(labelWidth, Math.Ceiling(measured.WidthIncludingTrailingWhitespace) + 3)) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(34) });
        row.Children.Add(labelText); var readout = Ellipsis(value.ToString("0", L.Culture) + suffix, 11, Ui.Ink); readout.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(readout, 2); row.Children.Add(readout);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(2, 0, 7, 0), Height = 24 }; AutomationProperties.SetAutomationId(slider, id); AutomationProperties.SetName(slider, L.Text(label)); Grid.SetColumn(slider, 1); row.Children.Add(slider); panel.Children.Add(row);
        var last = value;
        slider.ValueChanged += (_, _) => readout.Text = slider.Value.ToString("0", L.Culture) + suffix;
        void Commit() { if (Math.Abs(last - slider.Value) < .01) return; Ui.Guard(() => commitValue(slider.Value)); last = slider.Value; }
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => Commit()));
        slider.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(Commit);
        slider.PreviewKeyUp += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageDown or Key.PageUp) Commit(); };
        slider.LostKeyboardFocus += (_, _) => Commit(); return slider;
    }
    private static UIElement EdgeGlyph(DockEdge edge)
    {
        var canvas = new Canvas { Width = 22, Height = 17 };
        canvas.Children.Add(new Rectangle { Width = 21, Height = 16, RadiusX = 2, RadiusY = 2, Stroke = Ui.Muted, StrokeThickness = .75, Opacity = .7 });
        var strip = new Border { Background = Ui.Accent, CornerRadius = new CornerRadius(1), Width = edge is DockEdge.Top or DockEdge.Bottom ? 13 : 2, Height = edge is DockEdge.Top or DockEdge.Bottom ? 2 : 11 };
        Canvas.SetLeft(strip, edge switch { DockEdge.Right => 17, DockEdge.Left => 2, _ => 4 }); Canvas.SetTop(strip, edge switch { DockEdge.Top => 2, DockEdge.Bottom => 12, _ => 3 }); canvas.Children.Add(strip); return canvas;
    }
}
