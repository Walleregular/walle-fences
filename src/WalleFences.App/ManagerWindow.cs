using Microsoft.Win32;
using System.Windows.Automation;
using Forms = System.Windows.Forms;

namespace WalleFences.App;

internal sealed partial class ManagerWindow : Window
{
    private readonly AppController _app;
    private readonly StackPanel _page = new();
    private readonly ScrollViewer _pageScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _status = Ui.Text("", 11, Ui.Muted);
    private readonly TextBlock _peekHint = Ui.Text("", 11, Ui.Brush("#91AAA2"));
    private readonly Dictionary<string, Button> _nav = [];
    internal string CurrentSection { get; private set; } = "groups";
    private readonly TextBlock _heading = Ui.Text("", 24, Ui.Ink, FontWeights.Bold);
    private readonly TextBlock _subheading = Ui.Text("", 12, Ui.Muted);
    private readonly Grid _shell = new() { Background = Ui.Paper };
    private readonly Border _titlebarHost = new();
    private readonly TextBlock _setShortcutHint = Ui.Text("Ctrl + Alt + ← / →", 9, Ui.Muted);
    private readonly Border _sidebarHost = new() { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0) };
    private readonly Border _inspectorHost = new() { Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly ColumnDefinition _inspectorColumn = new() { Width = new GridLength(0) };
    private readonly StackPanel _header = new() { Margin = new Thickness(0, 0, 0, 15) };
    private int _suppressRefresh;
    private Action? _refreshStartup;
    private Dictionary<string, string> Names => new() { ["groups"] = L.Text("Рабочий стол"), ["pages"] = L.Text("Наборы"), ["widgets"] = L.Text("Виджеты"), ["appearance"] = L.Text("Оформление"), ["monitors"] = L.Text("Мониторы"), ["shortcuts"] = L.Text("Сочетания клавиш"), ["rules"] = L.Text("Автоматизация"), ["snapshots"] = L.Text("Снимки раскладки"), ["sidebars"] = L.Text("Боковые панели"), ["settings"] = L.Text("Настройки") };

    internal ManagerWindow(AppController app)
    {
        _app = app; _pageMotion = new SurfaceMotion(_page); Title = L.Text("Walle Fences · Настройки рабочего стола"); Icon = Brand.WindowIcon; Language = System.Windows.Markup.XmlLanguage.GetLanguage(L.Culture.Name); Width = 1160; Height = 670; MinWidth = 900; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AutomationProperties.SetAutomationId(this, "manager");
        Ui.WindowFrame(this);
        var frame = new Grid(); frame.RowDefinitions.Add(new() { Height = new GridLength(42) }); frame.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _titlebarHost.Child = Titlebar(); frame.Children.Add(_titlebarHost); Grid.SetRow(_shell, 1); frame.Children.Add(_shell);
        _shell.ColumnDefinitions.Add(new() { Width = new GridLength(_app.Workspace.Preferences.CompactNavigation ? 64 : 180) }); _shell.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _shell.ColumnDefinitions.Add(_inspectorColumn);
        _sidebarHost.Child = Sidebar(); _shell.Children.Add(_sidebarHost); Grid.SetColumn(_inspectorHost, 2); _shell.Children.Add(_inspectorHost);
        var main = new DockPanel { Margin = new Thickness(20, 17, 18, 12), LastChildFill = true }; Grid.SetColumn(main, 1); _shell.Children.Add(main);
        DockPanel.SetDock(_header, Dock.Top); main.Children.Add(_header);
        _header.Children.Add(_heading); _subheading.Margin = new Thickness(0, 2, 0, 0); AutomationProperties.SetAutomationId(_subheading, "workspace-description"); _header.Children.Add(_subheading);
        var footer = new Grid { Margin = new Thickness(0, 13, 0, 0) }; footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); DockPanel.SetDock(footer, Dock.Bottom); main.Children.Add(footer);
        var state = new Grid(); state.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); state.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); state.Children.Add(new Border { Width = 7, Height = 7, Background = Ui.Accent, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(_status, 1); state.Children.Add(_status); footer.Children.Add(state);
        _status.TextTrimming = TextTrimming.CharacterEllipsis; _status.TextWrapping = TextWrapping.NoWrap;
        _setShortcutHint.Margin = new Thickness(12, 0, 0, 0); _setShortcutHint.ToolTip = L.Text("Сменить набор"); Grid.SetColumn(_setShortcutHint, 1); footer.Children.Add(_setShortcutHint);
        _pageScroll.Content = _page; AutomationProperties.SetAutomationId(_pageScroll, "workspace-scroll"); main.Children.Add(_pageScroll);
        Content = new Border { Child = frame, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Background = Ui.Paper, ClipToBounds = true };
        InitializeGuidedTour(frame);
        app.Changed += OnChanged; DesktopWallpaper.Changed += WallpaperChanged; Closed += (_, _) => { app.Changed -= OnChanged; DesktopWallpaper.Changed -= WallpaperChanged; _pageMotion.Dispose(); };
        Activated += (_, _) => { _app.SyncManagerLayer(); _refreshStartup?.Invoke(); _refreshHardwareSensors?.Invoke(); _ = DesktopWallpaper.RefreshAsync(force: true); };
        Loaded += (_, _) => _app.SyncManagerLayer();
        IsVisibleChanged += (_, _) => _app.SyncManagerLayer();
        StateChanged += (_, _) => { _refreshCaption?.Invoke(); _app.SyncManagerLayer(); };
        PreviewKeyDown += (_, e) => { if (_guidedTourActive && e.Key == Key.Escape) { EndGuidedTour(completed: false); e.Handled = true; return; } if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox) { _app.Undo(); Refresh(); e.Handled = true; } if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { _app.ShowSearch(); e.Handled = true; } if (e.Key == Key.Escape && _app.IsPeek) _app.Peek(); };
        SizeChanged += (_, _) => _inspectorColumn.Width = new GridLength(CurrentSection == "groups" ? (ActualWidth < 1010 ? 260 : 280) : 0);
        Navigate("groups"); InitializeDesktopPreviewSync();
    }
    private UIElement Sidebar()
    {
        var compact = _app.Workspace.Preferences.CompactNavigation;
        var dock = new DockPanel { Background = Ui.Sidebar };
        var bottom = new StackPanel { Margin = new Thickness(6, 8, 6, 10) }; DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
        bottom.Children.Add(NavigationButton("settings", compact));
        var footer = new Grid { Margin = new Thickness(compact ? 6 : 10, 8, 6, 0) }; footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var languages = new StackPanel { Orientation = Orientation.Horizontal, Visibility = compact ? Visibility.Collapsed : Visibility.Visible };
        foreach (var (code, label) in new[] { ("ru", "RU"), ("en", "EN"), ("uk", "UA") })
        {
            var button = Ui.Button(label, () => _app.SetLanguage(code), id: "language-" + code); button.Padding = new Thickness(2, 3, 2, 3); button.Margin = new Thickness(0, 0, 9, 0); button.FontSize = 10; button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0, 0, 0, code == L.Language ? 1 : 0); button.BorderBrush = Ui.Accent; button.Foreground = code == L.Language ? Ui.Ink : Ui.Muted; languages.Children.Add(button);
        }
        footer.Children.Add(languages);
        var collapse = Ui.IconButton(Brand.Icon(compact ? "sidebar-expand" : "sidebar-collapse", Ui.Muted), () => { _app.Mutate(() => _app.Workspace.Preferences.CompactNavigation = !_app.Workspace.Preferences.CompactNavigation, false); RefreshLanguage(); }, L.Text(compact ? "Развернуть навигацию" : "Свернуть навигацию"), "navigation-toggle"); Grid.SetColumn(collapse, 1); footer.Children.Add(collapse); bottom.Children.Add(footer);
        var list = new StackPanel { Margin = new Thickness(6, 16, 6, 0) };
        foreach (var id in new[] { "groups", "appearance", "rules" }) list.Children.Add(NavigationButton(id, compact));
        var peek = Ui.Button("", _app.Peek, id: "peek");
        var peekContent = new Grid(); peekContent.ColumnDefinitions.Add(new() { Width = new GridLength(28) }); peekContent.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); peekContent.Children.Add(Brand.Icon("peek", Ui.Muted));
        var peekLabel = Ellipsis(L.Text("Быстрый доступ"), 11, Ui.Muted); peekLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; Grid.SetColumn(peekLabel, 1); peekContent.Children.Add(peekLabel); peek.Content = peekContent;
        peek.ToolTip = L.Text("Быстрый доступ") + " · " + _app.Workspace.Preferences.PeekHotkey; peek.HorizontalContentAlignment = HorizontalAlignment.Stretch; peek.Padding = new Thickness(compact ? 9 : 11, 7, 5, 7); peek.MinHeight = 34; peek.Background = Brushes.Transparent; peek.BorderThickness = new Thickness(3, 0, 0, 0); peek.BorderBrush = Brushes.Transparent; peek.Margin = new Thickness(0, 7, 0, 0); AutomationProperties.SetName(peek, L.Text("Быстрый доступ")); list.Children.Add(peek);
        dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); return dock;
    }
    private Button NavigationButton(string id, bool compact)
    {
        var name = Names[id]; var button = RouteButton(Ui.Button(name, () => { }, id: "nav-" + id), id);
        var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new GridLength(28) }); content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); content.Children.Add(Brand.NavigationIcon(id, Ui.Muted));
        var label = Ui.Text(name, 11, Ui.Ink, FontWeights.Medium); label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(label, 1); content.Children.Add(label); button.Content = content; button.ToolTip = name; AutomationProperties.SetName(button, name);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(compact ? 9 : 11, 7, 5, 7); button.Margin = new Thickness(0, 0, 0, 4); button.MinHeight = 34; button.BorderThickness = new Thickness(3, 0, 0, 0); button.Foreground = Ui.Ink; button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; _nav[id] = button; return button;
    }
    internal void RefreshLanguage()
    {
        Title = L.Text("Walle Fences · Настройки рабочего стола"); Language = System.Windows.Markup.XmlLanguage.GetLanguage(L.Culture.Name);
        _titlebarHost.Child = Titlebar(); _setShortcutHint.ToolTip = L.Text("Сменить набор");
        _shell.ColumnDefinitions[0].Width = new GridLength(_app.Workspace.Preferences.CompactNavigation ? 64 : 180);
        if (_peekHint.Parent is Panel oldParent) oldParent.Children.Remove(_peekHint);
        _nav.Clear(); _sidebarHost.Child = Sidebar(); Refresh();
        _guideDisplayKey = null; QueueGuidedTourTarget();
    }
    internal void RefreshStatusOnly()
    {
        _status.Text = _app.Status;
        _peekHint.Text = _app.Workspace.Preferences.PeekHotkey.Replace("+", " + ");
    }
    private void OnChanged()
    {
        RefreshStatusOnly();
        _refreshSettingsMode?.Invoke();
        _refreshStartup?.Invoke();
        _refreshHardwareSensors?.Invoke();
        _refreshToolbar?.Invoke();
        QueueGuidedTourTarget();
        if (_suppressRefresh > 0 || Keyboard.FocusedElement is TextBox focused && IsAncestorOf(focused) || Mouse.Captured is System.Windows.Controls.Primitives.Thumb) return;
        if (CurrentSection is not ("settings" or "appearance" or "shortcuts")) Refresh();
    }
    internal void Refresh()
    {
        RefreshPage(preserveMotion: false);
    }
    private void RefreshPage(bool preserveMotion)
    {
        if (!preserveMotion) _pageMotion.Reset();
        _status.Text = _app.Status; _page.Children.Clear(); _heading.Text = Names[CurrentSection is "widgets" or "pages" ? "groups" : CurrentSection]; _subheading.Text = "";
        _refreshStartup = null;
        _refreshHardwareSensors = null;
        _refreshToolbar = null;
        _refreshSettingsMode = null;
        _peekHint.Text = _app.Workspace.Preferences.PeekHotkey.Replace("+", " + ");
        var primarySection = PrimarySection(CurrentSection);
        foreach (var (id, button) in _nav) { button.Background = id == primarySection ? Ui.AccentWash : Brushes.Transparent; button.BorderBrush = id == primarySection ? Ui.Accent : Brushes.Transparent; }
        _inspectorHost.Visibility = CurrentSection == "groups" ? Visibility.Visible : Visibility.Collapsed; _inspectorColumn.Width = new GridLength(CurrentSection == "groups" ? (ActualWidth > 0 && ActualWidth < 1010 ? 260 : 280) : 0);
        if (primarySection == "groups")
        {
            _page.Children.Add(DesktopWorkspaceNavigation());
            if (CurrentSection is "groups" or "widgets" or "pages") _page.Children.Add(SetTabs());
        }
        if (CurrentSection == "groups" && OnboardingInvitation() is { } invitation) _page.Children.Add(invitation);
        if (CurrentSection == "shortcuts") _page.Children.Add(RouteButton(LabelAction("Назад к настройкам", "chevron-left", () => { }, id: "settings-back"), "settings"));
        switch (CurrentSection) { case "groups": Groups(); break; case "monitors": Monitors(); break; case "appearance": Appearance(); break; case "sidebars": Sidebars(); break; case "widgets": Widgets(); break; case "shortcuts": Shortcuts(); break; case "rules": Rules(); break; case "pages": Intro(L.Text("Переключайте независимые наборы файлов, вкладок и виджетов.")); Pages(); break; case "snapshots": Snapshots(); break; case "settings": Settings(); break; }
        UpdateDesktopPreviewTimer();
        QueueGuidedTourTarget();
    }
    private void Intro(string text) => _subheading.Text = text;
    private void Toolbar(params UIElement[] controls) { var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) }; foreach (var control in controls) row.Children.Add(control); _page.Children.Add(row); }
    private void Groups()
    {
        DesktopPage();
    }
    private UIElement GroupCard(Fence fence)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(128) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var preview = new Border { Width = 110, Height = 80, Background = Ui.Brush(fence.Color), CornerRadius = new CornerRadius(Math.Min(fence.CornerRadius, 12)), Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
        var inside = new StackPanel(); var previewTitle = Ui.Text(fence.Name, 10, Ui.Brush(fence.Accent), FontWeights.SemiBold); previewTitle.TextWrapping = TextWrapping.NoWrap; previewTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        var previewHeading = new DockPanel(); var previewIcon = GroupIcons.Create(fence, Ui.Brush(fence.Accent), 12); previewIcon.Margin = new Thickness(0, 0, 4, 0); DockPanel.SetDock(previewIcon, Dock.Left); previewHeading.Children.Add(previewIcon); previewHeading.Children.Add(previewTitle); inside.Children.Add(previewHeading);
        var mini = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var items = fence.ActiveTab.PortalPath == null ? fence.ActiveTab.Items.Take(2).ToList() : new List<string> { fence.ActiveTab.PortalPath };
        foreach (var item in items) { var icon = Native.Icon(item); if (icon != null) mini.Children.Add(new Image { Source = icon, Width = 28, Height = 28, Margin = new Thickness(0, 0, 7, 0) }); }
        if (items.Count == 0) mini.Children.Add(Ui.Text(L.Text("Добавьте файлы"), 10, Ui.Brush("#B6C5BE")));
        inside.Children.Add(mini); preview.Child = inside; row.Children.Add(preview);
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 15, 0) };
        details.Children.Add(Ui.Text(fence.Name, 17, Ui.Ink, FontWeights.SemiBold));
        var tabs = Ui.Text(L.Format("{0} вкладок  ·  {1} ссылок", fence.Tabs.Count, fence.Tabs.Sum(t => t.Items.Count)) + (fence.Tabs.Any(t => t.PortalPath != null) ? L.Text("  ·  Портал") : ""), 11, Ui.Muted); tabs.Margin = new Thickness(0, 5, 0, 7); details.Children.Add(tabs);
        var paths = string.Join("  /  ", fence.Tabs.Select(t => t.PortalPath ?? t.Name)); var detail = Ui.Text(paths, 11, Ui.Muted); detail.MaxHeight = 32; detail.TextTrimming = TextTrimming.CharacterEllipsis; details.Children.Add(detail);
        var badges = Ui.Text(Editors.SortLabels[(int)fence.Sort] + (fence.Locked ? L.Text("  ·  Закреплена") : "") + (fence.ContentCollapsed ? L.Text("  ·  Свёрнута") : ""), 10, Ui.Accent); badges.Margin = new Thickness(0, 7, 0, 0); details.Children.Add(badges); Grid.SetColumn(details, 1); row.Children.Add(details);
        var monitor = AppController.Displays().FirstOrDefault(d => d.Id.Equals(fence.Placement?.MonitorId, StringComparison.OrdinalIgnoreCase));
        var displayHint = Ui.Text(monitor != null ? L.Format("Экран: {0}", monitor.Name) : L.Text("Экран отключён — группа временно на основном"), 10, Ui.Muted); displayHint.Margin = new Thickness(0, 6, 0, 0); details.Children.Add(displayHint);
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 127 };
        var edit = Ui.Button(L.Text("Настроить"), () => Editors.Fence(_app, fence), id: "edit-" + fence.Id); edit.Margin = new Thickness(0, 0, 0, 7); actions.Children.Add(edit);
        var more = LabelAction("Вкладки", "tabs", () => ShowGroupMenu(fence, actions)); more.Margin = new Thickness(0); actions.Children.Add(more);
        Grid.SetColumn(actions, 2); row.Children.Add(actions); return new Border { Child = row, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 16, 0, 16) };
    }
    private void ShowLayoutMenu()
    {
        if (_toolbarOverflow == null) return;
        var menu = CreateLayoutMenu(_toolbarCompact); _toolbarOverflow.ContextMenu = menu;
        menu.PlacementTarget = _toolbarOverflow; menu.IsOpen = true;
    }
    private ContextMenu CreateLayoutMenu(bool includeOverflowActions)
    {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        AutomationProperties.SetAutomationId(menu, "group-toolbar-overflow"); AutomationProperties.SetName(menu, L.Text("Дополнительные действия"));
        if (includeOverflowActions)
        {
            menu.Items.Add(MenuAction("Нарисовать новую группу на рабочем столе", "draw", () => DrawFenceWindow.Draw(_app), "draw-fence"));
            var undo = MenuAction("Отменить изменение", "undo", _app.Undo, "undo"); undo.IsEnabled = _app.CanUndo; menu.Items.Add(undo);
            menu.Items.Add(MenuAction("Свернуть все", "collapse", () => _app.CollapseAll(true), "collapse-all"));
            menu.Items.Add(MenuAction("Развернуть все", "expand", () => _app.CollapseAll(false), "expand-all"));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(MenuAction("Тонкая полоса сверху", "collapse", _app.TopShelf));
        menu.Items.Add(MenuAction("Разделить край на блоки…", "groups", ShowEdgeBlocksEditor, "arrange-edge-blocks"));
        menu.Items.Add(MenuAction("Распределить весь рабочий стол", "sort", () => _ = RedistributeDesktop()));
        menu.Items.Add(RouteMenuItem(MenuAction("Боковые панели", "sidebars", () => { }), "sidebars"));
        menu.Items.Add(MenuAction("Поиск и команды", "search", _app.ShowSearch)); return menu;
    }
    private static MenuItem MenuAction(string label, string icon, Action action, string? id = null)
    {
        var text = L.Text(label); var item = new MenuItem { Header = text, Icon = Brand.Icon(icon, Ui.Ink, 16) };
        item.Click += (_, _) => Ui.Guard(action); AutomationProperties.SetName(item, text); if (id != null) AutomationProperties.SetAutomationId(item, id); return item;
    }
    private static Button LabelAction(string label, string icon, Action action, bool primary = false, string? id = null)
    {
        var text = L.Text(label); var button = Ui.Button("", action, primary, id); button.Content = IconText(icon, text, primary ? Ui.OnAccent : Ui.Ink);
        button.ToolTip = text; AutomationProperties.SetName(button, text); return button;
    }
    private void ShowGroupMenu(Fence fence, UIElement target)
    {
        var menu = new ContextMenu();
        foreach (var tab in fence.Tabs)
        {
            var item = new MenuItem { Header = tab.Name + (tab.PortalPath == null ? "" : L.Text(" · портал")), Icon = Brand.Icon(tab.PortalPath == null ? "tabs" : "folder", Ui.Ink, 16) };
            var actions = CreateTabActionsMenu(fence, tab);
            foreach (var action in actions.Items.Cast<object>().ToArray()) { actions.Items.Remove(action); item.Items.Add(action); }
            item.ContextMenu = CreateTabActionsMenu(fence, tab);
            menu.Items.Add(item);
        }
        var add = MenuAction("Добавить вкладку", "plus", () => Editors.Tab(_app, fence)); add.IsEnabled = !fence.ContentLocked; menu.Items.Add(add);
        var merge = new MenuItem { Header = L.Text("Объединить с группой"), Icon = Brand.Icon("layers", Ui.Ink, 16) };
        foreach (var group in _app.Workspace.Fences.Where(f => f != fence && f.PageId == fence.PageId))
        {
            var item = new MenuItem { Header = group.Name, Icon = Brand.Icon("groups", Ui.Ink, 16) };
            item.Click += (_, _) => { if (Ui.Confirm(L.Format("Добавить вкладки «{0}» в группу «{1}»?", fence.Name, group.Name))) _app.Merge(fence, group); }; merge.Items.Add(item);
        }
        merge.IsEnabled = merge.Items.Count > 0 && !fence.ContentLocked; menu.Items.Add(merge);
        var monitors = new MenuItem { Header = L.Text("Перенести на монитор"), Icon = Brand.Icon("monitor", Ui.Ink, 16) };
        foreach (var (display, index) in AppController.Displays().Select((d, i) => (d, i)))
        {
            var item = new MenuItem { Header = L.Format("Монитор {0} · {1} · {2:0}%", index + 1, display.Name, display.Scale / AppController.GlobalScale * 100), Icon = Brand.Icon("monitor", Ui.Ink, 16) }; item.Click += (_, _) => _app.MoveToMonitor(fence, display); monitors.Items.Add(item);
        }
        menu.Items.Add(monitors);
        menu.Items.Add(new Separator()); menu.Items.Add(MenuAction("Удалить группу…", "trash", () => Editors.DeleteFence(_app, fence))); menu.PlacementTarget = target; menu.IsOpen = true;
    }
    private async System.Threading.Tasks.Task RedistributeDesktop()
    {
        try { await _app.RedistributeDesktopAsync(); }
        catch (Exception ex) { Ui.Error(ex.Message); }
    }
    private void Appearance()
    {
        Intro(L.Text("Настройте цвет, прозрачность и поведение групп."));
        var advancedFields = new List<FrameworkElement>();
        _page.Children.Add(SettingsModePicker(() => SetAdvancedVisibility(advancedFields)));
        var selectedGroup = StyleSettings();
        if (selectedGroup != null) IndividualGroupModes(selectedGroup);
        var edgePanel = new StackPanel(); edgePanel.Children.Add(Ui.Text(L.Text("Раскладка по блокам"), 17, Ui.Ink, FontWeights.SemiBold));
        edgePanel.Children.Add(Ui.Text(L.Text("Размещайте выбранные группы вдоль края монитора равными блоками."), 12, Ui.Muted));
        edgePanel.Children.Add(LabelAction("Разделить край на блоки…", "groups", ShowEdgeBlocksEditor, id: "arrange-edge-blocks"));
        var disableEdge = Ui.Button(L.Text("Отключить полоску выбранной группы"), () =>
        {
            if (selectedGroup == null) return;
            ApplyIndividualProfile(selectedGroup.Id, fence => fence.Edge = DockEdge.None); Refresh();
        }, id: "disable-edge-dock"); disableEdge.IsEnabled = selectedGroup != null; edgePanel.Children.Add(disableEdge);
        _page.Children.Add(Ui.Card(edgePanel));
        if (selectedGroup != null) IndividualGroupAppearance(selectedGroup, advancedFields);
        var panel = new StackPanel(); panel.Children.Add(Ui.Text(L.Text("Поведение приложения"), 17, Ui.Ink, FontWeights.SemiBold));
        var chameleon = Ui.Check(panel, L.Text("Приглушать группы, когда курсор находится за их пределами"), _app.Workspace.Preferences.Chameleon);
        var faded = Ui.Slider(panel, L.Text("Видимость приглушённых групп, %"), 10, 80, _app.Workspace.Preferences.ChameleonOpacity * 100);
        advancedFields.Add(chameleon);
        AdvancedField(advancedFields, panel, faded);
        panel.Children.Add(Ui.Button(L.Text("Сохранить поведение приложения"), () =>
        {
            if (!_app.Workspace.Preferences.AdvancedSettings) return;
            _app.Mutate(() =>
            {
                _app.Workspace.Preferences.Chameleon = chameleon.IsChecked == true;
                _app.Workspace.Preferences.ChameleonOpacity = faded.Value / 100;
            });
            _app.SaveNow(); _app.SetStatus(L.Text("Изменения сохранены"));
        }, true, "apply-application-behavior"));
        var behaviorCard = Ui.Card(panel); advancedFields.Add(behaviorCard); _page.Children.Add(behaviorCard);
        _refreshSettingsMode?.Invoke();
    }
    private void Rules()
    {
        Intro(L.Text("Новые файлы рабочего стола автоматически получают свою группу."));
        Toolbar(LabelAction("Новое правило", "plus", () => Editors.Rule(_app), true, "new-rule"), Ui.Button(L.Text("Предпросмотр"), () => SortingPreviewWindow.Open(_app, this), id: "organize-preview"));
        var automatic = new CheckBox { Content = L.Text("Автоматически распределять новые файлы"), IsChecked = _app.Workspace.Preferences.AutoOrganize };
        automatic.Click += (_, _) => _app.Mutate(() => _app.Workspace.Preferences.AutoOrganize = automatic.IsChecked == true, false); _page.Children.Add(Ui.Card(automatic));
        foreach (var rule in _app.Workspace.Rules)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var description = new StackPanel(); description.Children.Add(Ui.Text(rule.Name + (rule.Enabled ? "" : L.Text(" · выключено")), 15, Ui.Ink, FontWeights.SemiBold));
            var fence = _app.Workspace.Fences.FirstOrDefault(f => f.Id == rule.FenceId); var tab = fence?.Tabs.FirstOrDefault(t => t.Id == rule.TabId);
            var hint = Ui.Text(Editors.RuleLabels[(int)rule.Kind] + "  " + rule.Pattern + "  →  " + (fence?.Name ?? L.Text("Группа удалена")) + " / " + (tab?.Name ?? L.Text("Вкладка удалена")), 11, Ui.Muted); hint.Margin = new Thickness(0, 7, 14, 0); description.Children.Add(hint); row.Children.Add(description);
            var up = CompactAction("Выше", "move-up", () => MoveRule(rule, -1)); up.IsEnabled = _app.Workspace.Rules.IndexOf(rule) > 0;
            var down = CompactAction("Ниже", "move-down", () => MoveRule(rule, 1)); down.IsEnabled = _app.Workspace.Rules.IndexOf(rule) < _app.Workspace.Rules.Count - 1;
            var actions = Ui.Row(up, down, CompactAction("Изменить правило", "edit", () => Editors.Rule(_app, rule)), CompactAction("Удалить правило", "trash", () => _app.Mutate(() => _app.Workspace.Rules.Remove(rule), false)));
            Grid.SetColumn(actions, 1); row.Children.Add(actions); _page.Children.Add(Ui.Card(row));
        }
        _page.Children.Add(Ui.Text(L.Text("Первое подходящее правило имеет приоритет. Сортировка добавляет ссылки; сами файлы остаются на месте."), 12, Ui.Muted));
    }
    private void MoveRule(SortingRule rule, int delta)
    {
        var index = _app.Workspace.Rules.IndexOf(rule); var target = index + delta;
        if (target < 0 || target >= _app.Workspace.Rules.Count) return;
        _app.Mutate(() => { _app.Workspace.Rules.RemoveAt(index); _app.Workspace.Rules.Insert(target, rule); }, false);
    }
    private void Snapshots()
    {
        Intro(L.Text("Сохраняйте расположение групп и возвращайтесь к нему в любой момент."));
        Toolbar(Ui.Button(L.Text("Сохранить снимок"), () => { var name = Ui.Ask(L.Text("Снимок раскладки"), L.Text("Название"), L.Text("Моя раскладка")); if (name != null) _app.Snapshot(name); }, true, "save-snapshot"), Ui.Button(L.Text("Экспорт JSON"), Export, id: "export"), Ui.Button(L.Text("Импорт JSON"), Import, id: "import"));
        var snapshots = _app.Store.Snapshots().ToList();
        if (snapshots.Count == 0) _page.Children.Add(Ui.Card(Ui.Text(L.Text("Снимков пока нет. Сохраните текущую раскладку перед изменениями."), 14, Ui.Muted)));
        foreach (var path in snapshots)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var label = Ui.Text(Path.GetFileNameWithoutExtension(path).Replace('_', ' '), 13); label.TextTrimming = TextTrimming.CharacterEllipsis; label.Margin = new Thickness(0, 0, 15, 0); row.Children.Add(label);
            var restore = Ui.Button(L.Text("Восстановить"), () => { if (Ui.Confirm(L.Text("Восстановить эту раскладку? Текущее состояние будет сохранено отдельным снимком."))) _app.Restore(path); }); Grid.SetColumn(restore, 1); row.Children.Add(restore); _page.Children.Add(Ui.Card(row));
        }
        _page.Children.Add(Ui.Text(L.Text("Снимок содержит настройки и ссылки на файлы. Данные самих файлов в снимок не входят."), 12, Ui.Muted));
    }
    private void Export()
    {
        var picker = new SaveFileDialog { Title = L.Text("Экспорт раскладки"), FileName = "walle-layout.json", Filter = L.Text("Раскладка JSON|*.json") };
        if (picker.ShowDialog() == true) { File.WriteAllText(picker.FileName, System.Text.Json.JsonSerializer.Serialize(_app.Workspace, WorkspaceStore.JsonOptions)); _app.SetStatus(L.Text("Раскладка экспортирована")); }
    }
    private void Import()
    {
        var picker = new OpenFileDialog { Title = L.Text("Импорт раскладки"), Filter = L.Text("Раскладка JSON|*.json") };
        if (picker.ShowDialog() == true && Ui.Confirm(L.Text("Загрузить раскладку из файла? Текущее состояние будет сохранено снимком."))) _app.Restore(picker.FileName);
    }
    private void Settings()
    {
        Intro(L.Text("Поведение рабочего стола, горячие клавиши и запуск приложения."));
        var advancedFields = new List<FrameworkElement>();
        _page.Children.Add(SettingsModePicker(() => SetAdvancedVisibility(advancedFields)));
        var shortcuts = RouteButton(LabelAction("Сочетания клавиш", "shortcuts", () => { }, id: "settings-shortcuts"), "shortcuts"); advancedFields.Add(shortcuts);
        Toolbar(shortcuts,
            LabelAction("Показать обучение", "help", StartOnboarding, id: "settings-onboarding"),
            LabelAction("Начать обучение заново", "undo", RestartOnboarding, id: "settings-onboarding-restart"));
        var languagePanel = new StackPanel(); languagePanel.Children.Add(Ui.Text(L.Text("Оформление и язык"), 17, Ui.Ink, FontWeights.SemiBold));
        var language = Ui.Combo(languagePanel, L.Text("Язык интерфейса"), L.Names, Array.IndexOf(L.Codes, _app.Workspace.Preferences.Language)); AutomationProperties.SetAutomationId(language, "language-choice"); language.Width = 250; language.HorizontalAlignment = HorizontalAlignment.Left;
        var languageHint = Ui.Text(L.Text("Язык изменится сразу. Ваши названия групп, вкладок и файлов сохранятся."), 12, Ui.Muted); languageHint.Margin = new Thickness(0, 0, 0, 14); languagePanel.Children.Add(languageHint);
        languagePanel.Children.Add(Ui.Button(L.Text("Применить язык"), () => { if (language.SelectedIndex >= 0) _app.SetLanguage(L.Codes[language.SelectedIndex]); }, true, "apply-language")); _page.Children.Add(Ui.Card(languagePanel));
        var monitorPanel = new StackPanel(); monitorPanel.Children.Add(Ui.Text(L.Text("Мониторы и масштаб"), 17, Ui.Ink, FontWeights.SemiBold));
        foreach (var (display, index) in AppController.Displays().Select((d, i) => (d, i)))
        {
            var description = Ui.Text(L.Format("Монитор {0} · {1} · {2:0}%", index + 1, display.Name, display.Scale / AppController.GlobalScale * 100) + (display.Primary ? L.Text(" · основной") : ""), 13, Ui.Ink, FontWeights.Medium); description.Margin = new Thickness(0, 12, 0, 4); monitorPanel.Children.Add(description);
            var geometry = Ui.Text(L.Format("Рабочая область: {0:0} × {1:0} · позиция {2:0}, {3:0}", display.Width / AppController.GlobalScale, display.Height / AppController.GlobalScale, display.X / AppController.GlobalScale, display.Y / AppController.GlobalScale), 11, Ui.Muted);
            monitorPanel.Children.Add(geometry); advancedFields.Add(geometry);
        }
        var restoreMonitors = Ui.Check(monitorPanel, L.Text("Возвращать группы на прежний монитор после подключения"), _app.Workspace.Preferences.RestoreMonitorPositions); restoreMonitors.Margin = new Thickness(0, 17, 0, 12); AutomationProperties.SetAutomationId(restoreMonitors, "restore-monitor-positions");
        restoreMonitors.Click += (_, _) => _app.Mutate(() => _app.Workspace.Preferences.RestoreMonitorPositions = restoreMonitors.IsChecked == true);
        var monitorHint = Ui.Text(L.Text("Если экран отключён, группы остаются доступны на основном. Сохранённые позиции и размеры восстанавливаются после подключения. Ручной перенос задаёт новое место."), 12, Ui.Muted); monitorHint.Margin = new Thickness(0, 0, 0, 14); monitorPanel.Children.Add(monitorHint);
        monitorPanel.Children.Add(Ui.Button(L.Text("Настройки экранов Windows…"), Native.OpenDisplaySettings, id: "display-settings"));
        _page.Children.Add(Ui.Card(new Expander { Header = Ui.Text(L.Text("Мониторы и масштаб"), 14, Ui.Ink, FontWeights.SemiBold), Content = monitorPanel, IsExpanded = false }));
        var startupPanel = new StackPanel(); startupPanel.Children.Add(Ui.Text(L.Text("Автозагрузка с Windows"), 17, Ui.Ink, FontWeights.SemiBold));
        var startupState = Ui.Text("", 13); startupState.Margin = new Thickness(0, 10, 0, 12); startupPanel.Children.Add(startupState);
        AutomationProperties.SetAutomationId(startupState, "startup-state");
        var enableStartup = Ui.Button(L.Text("Включить"), () => _app.SetAutoStart(true), true, "startup-enable");
        var disableStartup = Ui.Button(L.Text("Выключить"), () => _app.SetAutoStart(false), id: "startup-disable");
        var startupControls = new WrapPanel(); startupControls.Children.Add(enableStartup); startupControls.Children.Add(disableStartup); startupControls.Children.Add(Ui.Button(L.Text("Автозагрузка Windows…"), Native.OpenStartupSettings, id: "startup-windows")); startupPanel.Children.Add(startupControls);
        var startupHint = Ui.Text(L.Text("Изменение применяется сразу. При входе в Windows приложение запускается в фоне и открывает вашу сохранённую раскладку."), 12, Ui.Muted); startupHint.Margin = new Thickness(0, 12, 0, 0); startupPanel.Children.Add(startupHint);
        _refreshStartup = () =>
        {
            try
            {
                var enabled = _app.AutoStart; startupState.Text = enabled ? L.Text("Включена") : L.Text("Выключена"); startupState.Foreground = enabled ? Ui.Accent : Ui.Muted;
                enableStartup.IsEnabled = !enabled; disableStartup.IsEnabled = enabled;
            }
            catch (Exception ex)
            {
                startupState.Text = L.Text("Не удалось прочитать настройку: ") + ex.Message; startupState.Foreground = Ui.Muted;
                enableStartup.IsEnabled = disableStartup.IsEnabled = false;
            }
        };
        _refreshStartup(); _page.Children.Add(Ui.Card(startupPanel));
        _page.Children.Add(HardwareSensorSettings());
        _page.Children.Add(GameModeSettings());
        var panel = new StackPanel(); var prefs = _app.Workspace.Preferences;
        var doubleClick = Ui.Check(panel, L.Text("Двойной щелчок по пустому рабочему столу скрывает группы"), prefs.DoubleClickHide);
        var nativeIcons = Ui.Check(panel, L.Text("Скрывать стандартные значки рабочего стола во время работы"), prefs.HideDesktopIcons);
        var hidden = Ui.Check(panel, L.Text("Показывать скрытые файлы в порталах"), prefs.ShowHiddenFiles);
        var grid = Ui.Check(panel, L.Text("Привязывать группы к сетке"), prefs.SnapToGrid);
        var smartSpacing = Ui.Check(panel, L.Text("Выравнивать группы с одинаковыми отступами"), prefs.SmartSpacing);
        var gap = Ui.Slider(panel, L.Text("Расстояние между группами, px"), 0, 48, prefs.FenceGap);
        var hotCorner = Ui.Check(panel, L.Text("Быстрый доступ при наведении в правый нижний угол экрана"), prefs.PeekHotCorner);
        var pageWheel = Ui.Check(panel, L.Text("Переключать страницы Ctrl + колесо на пустом рабочем столе"), prefs.PageWheel);
        var size = Ui.Slider(panel, L.Text("Шаг сетки"), 4, 64, prefs.GridSize);
        advancedFields.AddRange([hidden, smartSpacing, hotCorner, pageWheel]);
        AdvancedField(advancedFields, panel, gap); AdvancedField(advancedFields, panel, size);
        var manager = Ui.Check(panel, L.Text("Открывать настройки при запуске"), prefs.ShowManagerOnStart);
        var collapsed = Ui.Check(panel, L.Text("При запуске сворачивать группы до верхних вкладок"), prefs.StartCollapsed);
        panel.Children.Add(Ui.Button(L.Text("Сохранить настройки"), () =>
        {
            _app.Mutate(() =>
            {
                var current = _app.Workspace.Preferences;
                current.DoubleClickHide = doubleClick.IsChecked == true; current.HideDesktopIcons = nativeIcons.IsChecked == true;
                current.SnapToGrid = grid.IsChecked == true; current.ShowManagerOnStart = manager.IsChecked == true; current.StartCollapsed = collapsed.IsChecked == true;
                if (current.AdvancedSettings)
                {
                    current.ShowHiddenFiles = hidden.IsChecked == true; current.SmartSpacing = smartSpacing.IsChecked == true;
                    current.FenceGap = (int)gap.Value; current.PeekHotCorner = hotCorner.IsChecked == true;
                    current.PageWheel = pageWheel.IsChecked == true; current.GridSize = (int)size.Value;
                }
            });
            _app.UpdateIntegration(); _app.SetStatus(L.Text("Настройки сохранены")); Refresh();
        }, true, "save-settings")); _page.Children.Add(Ui.Card(panel));
        var status = new StackPanel(); status.Children.Add(Ui.Text(_app.DesktopStatus, 13, Ui.Muted)); status.Children.Add(Ui.Text(_app.HotkeyStatus, 12, Ui.Muted)); var data = Ui.Text(L.Text("Данные: ") + _app.Store.Root, 11, Ui.Muted); data.Margin = new Thickness(0, 12, 0, 12); status.Children.Add(data);
        status.Children.Add(Ui.Button(L.Text("Открыть папку данных"), () => Native.Open(_app.Store.Root)));
        var diagnostic = Ui.Card(new Expander { Header = Ui.Text(L.Text("Диагностика и данные"), 14, Ui.Ink, FontWeights.SemiBold), Content = status, IsExpanded = false }); advancedFields.Add(diagnostic); _page.Children.Add(diagnostic);
        _refreshSettingsMode?.Invoke();
    }
}
