using System.Windows.Automation;
using System.Windows.Markup;
using System.Windows.Media.Animation;

namespace WalleFences.App;

// The exercises own only local UI state. No workspace, sensor or background timer is involved.
internal sealed class OnboardingWindow : Window
{
    internal const int StepCount = 4;
    internal int StepIndex { get; private set; }
    private readonly Action _closed, _createGroup, _addWidget;
    private readonly TextBlock _headerLabel = Ui.Text("", 12, Ui.Ink, FontWeights.SemiBold);
    private readonly TextBlock _progress = Ui.Text("", 10.5, Ui.Muted);
    private readonly TextBlock _title = Ui.Text("", 23, Ui.Ink, FontWeights.Bold);
    private readonly TextBlock _description = Ui.Text("", 12, Ui.Muted);
    private readonly Grid _example = new() { MinHeight = 190 };
    private readonly StackPanel _body = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back, _next, _skip;
    private Button? _close;
    private Border? _demoGroup;
    private AnimationClock? _groupClock;
    private bool _groupExpanded, _focusSet, _widgetAdded, _finished;
    private int _animationGeneration;

    internal OnboardingWindow(Action closed, Action createGroup, Action addWidget)
    {
        _closed = closed; _createGroup = createGroup; _addWidget = addWidget;
        Title = L.Text("Знакомство с Walle Fences"); Icon = Brand.WindowIcon;
        Language = XmlLanguage.GetLanguage(L.Culture.Name); FontFamily = Ui.AppFont;
        Width = 640; Height = 520; MinWidth = 600; MinHeight = 490;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = Ui.Paper; Foreground = Ui.Ink;
        AutomationProperties.SetAutomationId(this, "onboarding-window"); Ui.WindowFrame(this);
        var layout = new DockPanel();
        var header = Header(); DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var content = new DockPanel { Margin = new Thickness(24, 16, 24, 20) };
        _back = ActionButton("Назад", () => ShowStep(StepIndex - 1), "onboarding-back");
        _next = ActionButton("Далее", () => { if (StepIndex == StepCount - 1) Finish(); else ShowStep(StepIndex + 1); }, "onboarding-next", true);
        _skip = ActionButton("Пропустить", Finish, "onboarding-skip");
        _skip.Background = Brushes.Transparent; _skip.BorderThickness = new Thickness(0); _skip.Foreground = Ui.Muted;
        var footer = Footer(); DockPanel.SetDock(footer, Dock.Bottom); content.Children.Add(footer);
        var progress = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        progress.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        progress.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        AutomationProperties.SetAutomationId(_progress, "onboarding-step");
        AutomationProperties.SetLiveSetting(_progress, AutomationLiveSetting.Polite);
        progress.Children.Add(_progress); Grid.SetColumn(_dots, 1); progress.Children.Add(_dots);
        _body.Children.Add(progress);
        _title.Margin = new Thickness(0, 0, 0, 7); _body.Children.Add(_title);
        _description.Margin = new Thickness(0, 0, 0, 14); _body.Children.Add(_description);
        _body.Children.Add(_example);
        content.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        layout.Children.Add(content);
        Content = new Border { Child = layout, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Finish(); e.Handled = true; } };
        Closed += (_, _) => { _finished = true; StopDemoAnimation(); Ui.Guard(_closed); };
        ShowStep(0);
    }

    internal void RefreshLanguage()
    {
        if (_finished) return;
        Title = L.Text("Знакомство с Walle Fences");
        Language = XmlLanguage.GetLanguage(L.Culture.Name); _headerLabel.Text = Title;
        void Translate(Button button, string key)
        {
            button.Content = L.Text(key); AutomationProperties.SetName(button, (string)button.Content);
        }
        Translate(_back, "Назад"); Translate(_skip, "Пропустить");
        if (_close != null)
        {
            _close.ToolTip = L.Text("Закрыть"); AutomationProperties.SetName(_close, L.Text("Закрыть"));
        }
        ShowStep(StepIndex, focusNext: false);
    }

    private UIElement Header()
    {
        var row = new DockPanel { LastChildFill = true };
        var close = _close = Ui.IconButton("close", Finish, L.Text("Закрыть"), "onboarding-close");
        close.Margin = new Thickness(0); close.Width = 32; close.Height = 32; DockPanel.SetDock(close, Dock.Right); row.Children.Add(close);
        _headerLabel.Text = L.Text("Знакомство с Walle Fences");
        var brand = Ui.Row(Brand.Logo(25), _headerLabel); _headerLabel.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(brand);
        var header = new Border { Child = row, Height = 42, Padding = new Thickness(16, 0, 7, 0), Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock or Border or DockPanel) { DragMove(); e.Handled = true; } };
        return header;
    }

    private UIElement Footer()
    {
        var row = new Grid { Margin = new Thickness(0, 17, 0, 0) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _skip.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(_skip);
        var navigation = Ui.Row(_back, _next); Grid.SetColumn(navigation, 1); row.Children.Add(navigation);
        return row;
    }

    private static Button ActionButton(string key, Action action, string id, bool primary = false)
    {
        var button = Ui.Button(L.Text(key), action, primary, id);
        button.MinHeight = 34; button.Padding = new Thickness(14, 7, 14, 7); button.Margin = new Thickness(0, 0, 7, 0);
        AutomationProperties.SetName(button, L.Text(key)); return button;
    }

    private void ShowStep(int step, bool focusNext = true)
    {
        if (_finished || step < 0 || step >= StepCount) return;
        StopDemoAnimation(); _demoGroup = null; StepIndex = step;
        _progress.Text = L.Format("Шаг {0} из {1}", step + 1, StepCount);
        _dots.Children.Clear();
        for (var i = 0; i < StepCount; i++)
            _dots.Children.Add(new Border { Width = i == step ? 20 : 6, Height = 6, CornerRadius = new CornerRadius(3), Background = i == step ? Ui.FocusAccent : Ui.Line, Margin = new Thickness(4, 0, 0, 0), ToolTip = L.Format("Шаг {0} из {1}", i + 1, StepCount) });
        _back.IsEnabled = step > 0;
        _next.Content = L.Text(step == StepCount - 1 ? "Готово" : "Далее");
        AutomationProperties.SetName(_next, (string)_next.Content);
        _skip.Visibility = step == StepCount - 1 ? Visibility.Collapsed : Visibility.Visible;
        _example.Children.Clear();
        var (title, description, demo) = step switch
        {
            0 => ("Группа освобождает рабочий стол", "Соберите ссылки в группу. Перетаскивайте её за заголовок, меняйте размер за края. У края экрана группа может раскрыться из тонкой полоски.", GroupExercise()),
            1 => ("Один рабочий стол — разные задачи", "Набор хранит свои группы и виджеты. Переключайте работу и фокус прямо в главном окне.", SetsExercise()),
            2 => ("Добавьте то, что нужно видеть", "Каждый виджет — отдельная панель со своим размером и положением. Добавьте часы, заметку или показатели системы прямо из каталога.", WidgetExercise()),
            _ => ("Теперь попробуйте на своём столе", "Создайте первую группу или откройте каталог виджетов. Вернуться к этому знакомству можно из меню рабочего стола.", Completion())
        };
        _title.Text = L.Text(title); _description.Text = L.Text(description); _example.Children.Add(demo);
        if (focusNext && IsLoaded) _next.Focus();
    }

    private UIElement ExampleFrame(UIElement content)
    {
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var caption = Ui.Text(L.Text("Интерактивный пример · ваша раскладка не меняется"), 10, Ui.Muted);
        caption.Margin = new Thickness(12, 9, 12, 8); grid.Children.Add(caption); Grid.SetRow(content, 1); grid.Children.Add(content);
        return new Border { Child = grid, Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Height = 211, ClipToBounds = true };
    }

    private UIElement GroupExercise()
    {
        var desk = new Grid { Background = Ui.Paper, Margin = new Thickness(10, 0, 10, 10), ClipToBounds = true };
        var wallpaper = Ui.Text(L.Text("Пример рабочего стола"), 11, Ui.Muted);
        wallpaper.HorizontalAlignment = HorizontalAlignment.Right; wallpaper.VerticalAlignment = VerticalAlignment.Center; wallpaper.Margin = new Thickness(16, 0, 16, 20); desk.Children.Add(wallpaper);
        var groupBody = new StackPanel { Margin = new Thickness(10, 7, 10, 8) };
        var name = Ui.Text(L.Text("Работа"), 12, Ui.Ink, FontWeights.SemiBold); name.Margin = new Thickness(7, 0, 0, 0);
        groupBody.Children.Add(Ui.Row(Brand.Icon("folder", Ui.FocusAccent, 16), name));
        var tab = Ui.Text(L.Text("Приложения"), 10.5, Ui.Ink); tab.Margin = new Thickness(0, 8, 0, 8);
        groupBody.Children.Add(new Border { Child = tab, BorderBrush = Ui.FocusAccent, BorderThickness = new Thickness(0, 0, 0, 2), Background = Ui.AccentWash, Padding = new Thickness(7, 0, 7, 0), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4) });
        groupBody.Children.Add(Ui.Row(FileTile("folder", "Документы"), FileTile("file", "Проект"), FileTile("note", "Заметки")));
        var group = new Border { Child = groupBody, Width = 272, Height = _groupExpanded ? 128 : 4, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 12, 0, 0), Background = Ui.Raised, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        _demoGroup = group; desk.Children.Add(group);
        var edge = new Border { Width = 272, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 0, 0, 0), Background = Brushes.Transparent };
        edge.Child = new Border { Height = 4, VerticalAlignment = VerticalAlignment.Top, Background = Ui.FocusAccent, CornerRadius = new CornerRadius(0, 0, 3, 3) }; desk.Children.Add(edge);
        AutomationProperties.SetAutomationId(edge, "onboarding-edge-example");
        Button? toggle = null;
        toggle = ActionButton("Раскрыть пример", () => SetExpanded(!_groupExpanded), "onboarding-example-action");
        toggle.HorizontalAlignment = HorizontalAlignment.Right; toggle.VerticalAlignment = VerticalAlignment.Bottom; toggle.Margin = new Thickness(0, 0, 8, 5); toggle.FontSize = 10.5; desk.Children.Add(toggle);
        void SetExpanded(bool expanded)
        {
            if (_finished || StepIndex != 0 || !ReferenceEquals(_demoGroup, group) || _groupExpanded == expanded) return;
            _groupExpanded = expanded; AnimateGroup(group, expanded ? 128 : 4);
            toggle!.Content = L.Text(expanded ? "Свернуть пример" : "Раскрыть пример"); AutomationProperties.SetName(toggle, (string)toggle.Content);
        }
        if (_groupExpanded) { toggle.Content = L.Text("Свернуть пример"); AutomationProperties.SetName(toggle, (string)toggle.Content); }
        edge.MouseEnter += (_, _) => SetExpanded(true);
        desk.MouseLeave += (_, _) => SetExpanded(false);
        return ExampleFrame(desk);
    }

    private static UIElement FileTile(string icon, string key)
    {
        var tile = new StackPanel { Width = 78 }; tile.Children.Add(Brand.Icon(icon, Ui.Muted, 25));
        var label = Ui.Text(L.Text(key), 9.5, Ui.Muted); label.TextAlignment = TextAlignment.Center; label.Margin = new Thickness(0, 5, 0, 0); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; label.ToolTip = label.Text; tile.Children.Add(label); return tile;
    }

    private UIElement SetsExercise()
    {
        var grid = new Grid { Margin = new Thickness(12, 0, 12, 12) }; grid.RowDefinitions.Add(new() { Height = new GridLength(34) }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        var scene = new Grid { Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(scene, 1); grid.Children.Add(scene);
        Button? work = null, focus = null;
        void Select(bool selected)
        {
            _focusSet = selected;
            work!.Tag = !selected ? "selected" : null; focus!.Tag = selected ? "selected" : null;
            work.Foreground = !selected ? Ui.Ink : Ui.Muted; focus.Foreground = selected ? Ui.Ink : Ui.Muted;
            scene.Children.Clear();
            var columns = new Grid(); columns.ColumnDefinitions.Add(new()); columns.ColumnDefinitions.Add(new());
            var first = DemoCard(selected ? "clock" : "folder", selected ? "Фокус" : "Работа", selected ? "25:00" : "Проект · Документы", selected ? "Пример таймера" : "Пример группы");
            var second = DemoCard(selected ? "note" : "folder", selected ? "Заметки" : "Личное", selected ? "Одна задача за раз" : "Фото · Ссылки", selected ? "Пример заметки" : "Пример группы");
            first.Margin = new Thickness(0, 0, 5, 0); second.Margin = new Thickness(5, 0, 0, 0); columns.Children.Add(first); Grid.SetColumn(second, 1); columns.Children.Add(second); scene.Children.Add(columns);
        }
        work = Ui.Segment(L.Text("Работа"), () => Select(false), !_focusSet, "onboarding-example-restore");
        focus = Ui.Segment(L.Text("Фокус"), () => Select(true), _focusSet, "onboarding-example-action");
        foreach (var button in new[] { work, focus }) { button.MinWidth = 108; button.Height = 32; button.Margin = new Thickness(0, 0, 5, 0); AutomationProperties.SetName(button, button.Content?.ToString() ?? ""); tabs.Children.Add(button); }
        grid.Children.Add(tabs); Select(_focusSet); return ExampleFrame(grid);
    }

    private UIElement WidgetExercise()
    {
        var grid = new Grid { Margin = new Thickness(12, 0, 12, 12) };
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = new GridLength(38) });
        var widgets = new Grid { Margin = new Thickness(0, 0, 0, 10) }; widgets.ColumnDefinitions.Add(new()); widgets.ColumnDefinitions.Add(new()); grid.Children.Add(widgets);
        var clock = DemoCard("clock", "Часы", "10:24", "Пример времени"); clock.Margin = new Thickness(0, 0, 5, 0); widgets.Children.Add(clock);
        var noteHost = new Border { Margin = new Thickness(5, 0, 0, 0) }; Grid.SetColumn(noteHost, 1); widgets.Children.Add(noteHost);
        Button? action = null;
        void Update()
        {
            noteHost.Child = _widgetAdded ? DemoCard("note", "Заметки", "Одна задача за раз", "Пример заметки") : new Border { Child = Ui.Text(L.Text("Место для вашего виджета"), 11, Ui.Muted), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(12) };
            action!.Content = L.Text(_widgetAdded ? "Убрать пример виджета" : "Добавить пример виджета"); AutomationProperties.SetName(action, (string)action.Content);
        }
        action = ActionButton("Добавить пример виджета", () => { _widgetAdded = !_widgetAdded; Update(); }, "onboarding-example-action");
        action.HorizontalAlignment = HorizontalAlignment.Left; action.Margin = new Thickness(0); Grid.SetRow(action, 1); grid.Children.Add(action);
        Update(); return ExampleFrame(grid);
    }

    private static Border DemoCard(string icon, string title, string value, string note)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var label = Ui.Text(L.Text(title), 11, Ui.Ink, FontWeights.SemiBold); label.Margin = new Thickness(6, 0, 0, 0); stack.Children.Add(Ui.Row(Brand.Icon(icon, Ui.FocusAccent, 16), label));
        var detail = Ui.Text(value is "25:00" or "10:24" ? value : L.Text(value), value is "25:00" or "10:24" ? 25 : 12, Ui.Ink, FontWeights.Medium); detail.Margin = new Thickness(0, 9, 0, 6); stack.Children.Add(detail);
        stack.Children.Add(Ui.Text(L.Text(note), 9.5, Ui.Muted));
        return new Border { Child = stack, Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(12, 9, 12, 9) };
    }

    private UIElement Completion()
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) }; grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        Border Choice(string icon, string title, string description, Action action, string id)
        {
            var panel = new StackPanel(); var glyph = Brand.Icon(icon, Ui.FocusAccent, 25); glyph.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(glyph);
            var name = Ui.Text(L.Text(title), 15, Ui.Ink, FontWeights.SemiBold); name.Margin = new Thickness(0, 10, 0, 7); panel.Children.Add(name);
            var detail = Ui.Text(L.Text(description), 11, Ui.Muted); detail.MinHeight = 42; panel.Children.Add(detail);
            var button = ActionButton(title, () => Finish(action), id); button.HorizontalAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 13, 0, 0); panel.Children.Add(button);
            return new Border { Child = panel, Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(16) };
        }
        var group = Choice("folder-add", "Добавить группу", "Для приложений, папок и файлов.", _createGroup, "onboarding-create-group");
        var widget = Choice("widgets", "Добавить виджет", "Выберите виджет и настройте его собственную панель.", _addWidget, "onboarding-add-widget");
        group.Margin = new Thickness(0, 0, 6, 0); widget.Margin = new Thickness(6, 0, 0, 0); grid.Children.Add(group); Grid.SetColumn(widget, 1); grid.Children.Add(widget); return grid;
    }

    private void AnimateGroup(Border target, double height)
    {
        var current = target.Height; StopDemoAnimation(); var generation = _animationGeneration;
        if (!SystemParameters.ClientAreaAnimation || Math.Abs(current - height) < .5) { target.Height = height; return; }
        target.Height = current;
        var clock = (AnimationClock)new DoubleAnimation(current, height, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd }.CreateClock(true);
        _groupClock = clock;
        clock.Completed += (_, _) => { if (generation != _animationGeneration) return; target.Height = height; target.ApplyAnimationClock(HeightProperty, null); _groupClock = null; };
        target.ApplyAnimationClock(HeightProperty, clock, HandoffBehavior.SnapshotAndReplace);
        clock.Controller!.Begin(); clock.Controller.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
    }

    private void StopDemoAnimation()
    {
        _animationGeneration++;
        if (_demoGroup != null) { var height = _demoGroup.Height; _demoGroup.ApplyAnimationClock(HeightProperty, null); _demoGroup.Height = height; }
        _groupClock?.Controller?.Stop(); _groupClock = null;
    }

    private void Finish() => Finish(null);
    private void Finish(Action? next)
    {
        if (_finished) return; _finished = true; Close();
        if (next != null) Ui.Guard(next);
    }
}
