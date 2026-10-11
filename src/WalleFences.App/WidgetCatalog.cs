using System.Windows.Automation;

namespace WalleFences.App;

internal static class WidgetCatalog
{
    internal static string Icon(WidgetKind kind) => kind switch
    {
        WidgetKind.Clock => "clock", WidgetKind.Note => "note", WidgetKind.Focus => "clock",
        WidgetKind.System => "cpu", WidgetKind.Drives => "drive", WidgetKind.CpuTemperature => "temperature",
        WidgetKind.Processes => "processes", WidgetKind.Memory => "memory", WidgetKind.Gpu => "gpu",
        WidgetKind.Network => "network", WidgetKind.Battery => "battery", WidgetKind.Cpu => "cpu",
        WidgetKind.Uptime => "uptime", _ => "widgets"
    };
    internal static string Description(WidgetKind kind) => L.Text(kind switch
    {
        WidgetKind.Clock => "Время и дата всегда перед глазами",
        WidgetKind.Note => "Задачи, идеи и важные записи",
        WidgetKind.Focus => "Рабочие сессии с паузой и сбросом",
        WidgetKind.System => "Загрузка CPU и использование памяти",
        WidgetKind.Drives => "Свободное место на каждом диске",
        WidgetKind.CpuTemperature => "Температура CPU и история показаний",
        WidgetKind.Processes => "Приложения с наибольшей нагрузкой на CPU или память",
        WidgetKind.Memory => "Занято, доступно и всего оперативной памяти",
        WidgetKind.Gpu => "Нагрузка GPU, видеопамять и температура",
        WidgetKind.Network => "Скорость приёма и отправки данных",
        WidgetKind.Battery => "Заряд, питание и оставшееся время",
        WidgetKind.Cpu => "Текущая загрузка процессора и история",
        WidgetKind.Uptime => "Время работы компьютера с последней загрузки",
        _ => "Виджет"
    });
    internal static Grid Choices(Action<WidgetKind> choose, string idPrefix, bool compact, bool enabled = true, WidgetKind? selected = null)
    {
        var grid = new Grid(); AutomationProperties.SetAutomationId(grid, idPrefix + "choices");
        for (var column = 0; column < 3; column++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var kinds = Enum.GetValues<WidgetKind>();
        for (var row = 0; row < (kinds.Length + 2) / 3; row++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        foreach (var kind in kinds)
        {
            var choice = Choice(kind, () => choose(kind), compact, selected == kind, idPrefix + kind); choice.IsEnabled = enabled;
            var index = (int)kind; choice.Margin = new Thickness(index % 3 == 0 ? 0 : 4, 0, index % 3 == 2 ? 0 : 4, 8);
            Grid.SetColumn(choice, index % 3); Grid.SetRow(choice, index / 3); grid.Children.Add(choice);
        }
        return grid;
    }
    internal static Button Choice(WidgetKind kind, Action action, bool compact, bool selected, string id)
    {
        var button = Ui.Button("", action, id: id); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Top;
        button.Padding = new Thickness(compact ? 10 : 12); button.Background = selected ? Ui.AccentWash : Ui.Surface; button.BorderBrush = selected ? Ui.Accent : Ui.Line; button.Tag = selected ? "selected" : null;
        var body = new StackPanel(); var title = new Grid(); title.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); title.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        title.Children.Add(Brand.Icon(Icon(kind), Ui.Muted, 17));
        var label = Ui.Text(WidgetEditor.Labels[(int)kind], compact ? 10 : 11, Ui.Ink, FontWeights.SemiBold); Grid.SetColumn(label, 1); title.Children.Add(label); body.Children.Add(title);
        if (!compact)
        {
            var example = Ui.Text(L.Text("Пример оформления"), 9, Ui.Muted); example.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(example);
            var sample = Sample(kind); sample.Margin = new Thickness(0, 11, 0, 10); body.Children.Add(sample);
        }
        var description = Ui.Text(Description(kind), 10, Ui.Muted); description.Margin = new Thickness(0, compact ? 5 : 0, 0, 0); description.LineHeight = 15; body.Children.Add(description);
        button.Content = body; AutomationProperties.SetName(button, WidgetEditor.Labels[(int)kind]); AutomationProperties.SetHelpText(button, Description(kind)); AutomationProperties.SetItemStatus(button, selected ? L.Text("Выбрано") : ""); return button;
    }
    private static FrameworkElement Sample(WidgetKind kind)
    {
        var sample = new StackPanel { MinHeight = 36 };
        Border Line(double width, double opacity = .35) => new() { Height = 3, Width = width, Background = Ui.Muted, Opacity = opacity, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 5, 0, 3), HorizontalAlignment = HorizontalAlignment.Left };
        void Meter(double fraction)
        {
            var fill = new Border { Background = Ui.Accent, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
            var track = new Border { Background = Ui.Line, Height = 4, CornerRadius = new CornerRadius(2), Child = fill, Margin = new Thickness(0, 7, 0, 0) };
            track.SizeChanged += (_, _) => fill.Width = Math.Max(0, track.ActualWidth * fraction); sample.Children.Add(track);
        }
        switch (kind)
        {
            case WidgetKind.Clock:
                sample.Children.Add(Ui.Text("13:08", 24, Ui.Ink, FontWeights.Medium)); break;
            case WidgetKind.Note:
                sample.Children.Add(Line(100)); sample.Children.Add(Line(74)); sample.Children.Add(Line(88, .18)); break;
            case WidgetKind.Focus:
                var timer = Ui.Row(Ui.Text("25:00", 24, Ui.Ink, FontWeights.Medium), new Border { Width = 27, Height = 27, Background = Ui.AccentWash, CornerRadius = new CornerRadius(14), Child = Brand.Icon("play", Ui.Accent, 13), Margin = new Thickness(9, 0, 0, 0) }); sample.Children.Add(timer); break;
            case WidgetKind.System:
                sample.Children.Add(Ui.Text("CPU  ·  RAM", 11, Ui.Muted)); Meter(.32); Meter(.56); break;
            case WidgetKind.Drives:
                sample.Children.Add(Ui.Text("C:\\", 15, Ui.Ink, FontWeights.Medium)); Meter(.64); break;
            case WidgetKind.CpuTemperature:
                sample.Children.Add(Ui.Text("— °C", 24, Ui.Ink, FontWeights.Medium)); break;
            case WidgetKind.Processes:
                sample.Children.Add(Ui.Text("CPU  ·  RAM", 11, Ui.Muted));
                sample.Children.Add(Line(112)); sample.Children.Add(Line(83)); sample.Children.Add(Line(63, .18)); break;
            case WidgetKind.Memory:
                sample.Children.Add(Ui.Text("RAM", 15, Ui.Ink, FontWeights.Medium)); Meter(.58); break;
            case WidgetKind.Gpu:
                sample.Children.Add(Ui.Text("GPU  ·  VRAM", 11, Ui.Muted)); Meter(.39); Meter(.61); break;
            case WidgetKind.Network:
                sample.Children.Add(Ui.Row(Brand.Icon("download", Ui.Accent, 15), Ui.Text("— MB/s", 15, Ui.Ink, FontWeights.Medium)));
                sample.Children.Add(Ui.Row(Brand.Icon("upload", Ui.Muted, 15), Ui.Text("— MB/s", 12, Ui.Muted))); break;
            case WidgetKind.Battery:
                sample.Children.Add(Ui.Row(Brand.Icon("battery", Ui.Accent, 27), Ui.Text("— %", 22, Ui.Ink, FontWeights.Medium))); Meter(.74); break;
            case WidgetKind.Cpu:
                sample.Children.Add(Ui.Text("— %", 24, Ui.Ink, FontWeights.Medium)); Meter(.42); break;
            case WidgetKind.Uptime:
                sample.Children.Add(Ui.Text("— : — : —", 24, Ui.Ink, FontWeights.Medium)); break;
        }
        sample.IsHitTestVisible = false; AutomationProperties.SetHelpText(sample, L.Text("Пример оформления")); return sample;
    }
    internal static DesktopWidget Copy(DesktopWidget widget) => new()
    {
        Id = widget.Id, Kind = widget.Kind, Title = widget.Title, Text = widget.Text, Use24HourClock = widget.Use24HourClock,
        FocusMinutes = widget.FocusMinutes, RemainingSeconds = widget.RemainingSeconds, FocusEndsAt = widget.FocusEndsAt,
        ProcessSort = widget.ProcessSort, ProcessCount = widget.ProcessCount, GpuAdapterId = widget.GpuAdapterId, NetworkAdapterId = widget.NetworkAdapterId
    };
    internal static Border Preview(AppController app, Fence fence, DesktopWidget widget)
    {
        var tab = new FenceTab { Widgets = [Copy(widget)] };
        var previewFence = new Fence { Id = fence.Id, Name = fence.Name, Icon = fence.Icon, Color = fence.Color, Accent = fence.Accent, CornerRadius = fence.CornerRadius, ContentLocked = true, Tabs = [tab], ActiveTabId = tab.Id };
        var host = new WidgetHost(app, previewFence, preview: true); host.Sync();
        var border = new Border { Background = Ui.Brush(fence.Color), BorderBrush = Ui.Brush(fence.Accent, .24), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(0, 6, 0, 4), Child = host, ClipToBounds = true };
        border.Unloaded += (_, _) => host.Dispose(); return border;
    }
}

internal static class WidgetEditor
{
    internal static string[] Labels => [L.Text("Часы и дата"), L.Text("Заметка"), L.Text("Фокус-таймер"), L.Text("CPU и память"), L.Text("Свободное место"), L.Text("Температура CPU"),
        L.Text("Топ процессов"), L.Text("Оперативная память"), L.Text("Видеокарта"), L.Text("Сеть"), L.Text("Батарея"), L.Text("Загрузка CPU"), L.Text("Время работы ПК")];

    private sealed record AdapterChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private static ComboBox AdapterPicker(StackPanel fields, string label, string id, string savedId, IEnumerable<(string Id, string Name)> available)
    {
        // Read the cached snapshot only. Opening an editor must not start a sampler.
        var choices = new List<AdapterChoice> { new("", L.Text("Автоматически")) };
        choices.AddRange(available.Where(adapter => !string.IsNullOrWhiteSpace(adapter.Id))
            .DistinctBy(adapter => adapter.Id, StringComparer.OrdinalIgnoreCase).Select(adapter => new AdapterChoice(adapter.Id, adapter.Name)));
        if (!string.IsNullOrWhiteSpace(savedId) && !choices.Any(choice => choice.Id.Equals(savedId, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new(savedId, L.Format("Недоступный адаптер: {0}", savedId)));
        Ui.Label(fields, label);
        var picker = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(choice => choice.Id.Equals(savedId, StringComparison.OrdinalIgnoreCase)) ?? choices[0], Margin = new Thickness(0, 4, 0, 12) };
        AutomationProperties.SetAutomationId(picker, id); AutomationProperties.SetName(picker, label); fields.Children.Add(picker); return picker;
    }
    internal static (Fence Fence, FenceTab Tab, DesktopWidget? Widget) Resolve(AppController app, string fenceId, string tabId, string? widgetId = null)
    {
        var fence = app.Workspace.Fences.FirstOrDefault(f => f.Id == fenceId) ?? throw new InvalidOperationException(L.Text("Группа больше не существует."));
        var tab = fence.Tabs.FirstOrDefault(t => t.Id == tabId) ?? throw new InvalidOperationException(L.Text("Вкладка больше не существует."));
        var widget = widgetId == null ? null : tab.Widgets.FirstOrDefault(w => w.Id == widgetId) ?? throw new InvalidOperationException(L.Text("Виджет больше не существует."));
        return (fence, tab, widget);
    }
    internal static void Edit(AppController app, Fence fence, FenceTab tab, DesktopWidget? existing = null, WidgetKind? initialKind = null)
        => EditCore(app, fence, tab, existing, initialKind, false);

    internal static Fence? CreatePanel(AppController app, WidgetKind? initialKind = null, Fence? styleSource = null, DisplayArea? display = null)
    {
        var panel = SingleWidgetPanels.Create(app, initialKind ?? WidgetKind.Clock, styleSource, display);
        return EditCore(app, panel, panel.ActiveTab, null, initialKind, true, display) ? panel : null;
    }

    private static bool EditCore(AppController app, Fence fence, FenceTab tab, DesktopWidget? existing, WidgetKind? initialKind, bool separatePanel, DisplayArea? display = null)
    {
        var fenceId = fence.Id; var tabId = tab.Id; var existingId = existing?.Id;
        var current = separatePanel ? (Fence: fence, Tab: tab, Widget: (DesktopWidget?)null) : Resolve(app, fenceId, tabId, existingId); WorkspaceActions.RequireEditable(current.Fence);
        if (existingId == null && current.Tab.Widgets.Count >= 20) throw new InvalidOperationException(L.Text("Не больше 20 виджетов во вкладке."));
        var draft = current.Widget == null ? new DesktopWidget { Kind = initialKind ?? WidgetKind.Clock } : WidgetCatalog.Copy(current.Widget);
        var dialog = new FormWindow(L.Text(separatePanel ? "Новый виджет" : "Настройки виджета"), Math.Min(740, Math.Max(540, SystemParameters.WorkArea.Width - 70)), resizable: true) { Tag = fenceId };
        if (app.Manager is { IsVisible: true } manager) { dialog.Owner = manager; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        AutomationProperties.SetAutomationId(dialog, "widget-editor");
        var context = Ui.Text(separatePanel ? L.Text("Один виджет — отдельная панель со своим размером и положением.") : L.Format("{0} / {1}", current.Fence.Name, current.Tab.Name), 11, Ui.Muted); context.Margin = new Thickness(0, 0, 0, 12); dialog.Body.Children.Add(context);
        // The selector remains available to keyboard/UI automation; people use the labelled visual choices.
        var kind = new ComboBox { ItemsSource = Labels, SelectedIndex = (int)draft.Kind, Visibility = Visibility.Collapsed, IsEnabled = existingId == null };
        AutomationProperties.SetAutomationId(kind, "widget-editor-kind"); AutomationProperties.SetName(kind, L.Text("Виджет")); dialog.Body.Children.Add(kind);
        var pickerHost = new Border { Margin = new Thickness(0, 0, 0, 6) }; dialog.Body.Children.Add(pickerHost);
        var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var fields = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; var previewSide = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; Grid.SetColumn(previewSide, 1); content.Children.Add(fields); content.Children.Add(previewSide); dialog.Body.Children.Add(content);
        var title = Ui.Input(fields, L.Text("Название"), draft.Title); title.MaxLength = 100; AutomationProperties.SetAutomationId(title, "widget-editor-title");
        var noteFields = new StackPanel(); fields.Children.Add(noteFields);
        var text = Ui.Input(noteFields, L.Text("Текст заметки"), draft.Text); text.AcceptsReturn = true; text.TextWrapping = TextWrapping.Wrap; text.Height = 126; text.MaxLength = 20000; text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; AutomationProperties.SetAutomationId(text, "widget-editor-text");
        var focusFields = new StackPanel(); fields.Children.Add(focusFields);
        var minutes = Ui.Slider(focusFields, L.Text("Длительность, минут"), 1, 180, draft.FocusMinutes); AutomationProperties.SetAutomationId(minutes, "widget-editor-minutes");
        var durations = new WrapPanel(); foreach (var duration in new[] { 15, 25, 50, 90 }) { var button = Ui.Button(L.Format("{0} мин", duration), () => minutes.Value = duration); button.FontSize = 10; button.Padding = new Thickness(8, 5, 8, 5); durations.Children.Add(button); } focusFields.Children.Add(durations);
        var clockFields = new StackPanel(); fields.Children.Add(clockFields);
        var hours = Ui.Check(clockFields, L.Text("24-часовой формат"), draft.Use24HourClock); AutomationProperties.SetAutomationId(hours, "widget-editor-hours");
        var processFields = new StackPanel(); fields.Children.Add(processFields);
        Ui.Label(processFields, L.Text("Сортировать процессы"));
        var processSort = new ComboBox { ItemsSource = new[] { L.Text("По загрузке CPU"), L.Text("По использованию памяти") }, SelectedIndex = (int)draft.ProcessSort, Margin = new Thickness(0, 4, 0, 12) };
        AutomationProperties.SetAutomationId(processSort, "widget-editor-process-sort"); AutomationProperties.SetName(processSort, L.Text("Сортировать процессы")); processFields.Children.Add(processSort);
        Ui.Label(processFields, L.Text("Количество процессов"));
        var processCounts = new[] { 3, 5, 8, 10 }.Append(Math.Clamp(draft.ProcessCount, 3, 10)).Distinct().OrderBy(count => count).ToArray();
        var processCount = new ComboBox { ItemsSource = processCounts, SelectedItem = Math.Clamp(draft.ProcessCount, 3, 10), Margin = new Thickness(0, 4, 0, 12) };
        AutomationProperties.SetAutomationId(processCount, "widget-editor-process-count"); AutomationProperties.SetName(processCount, L.Text("Количество процессов")); processFields.Children.Add(processCount);
        var gpuFields = new StackPanel(); fields.Children.Add(gpuFields);
        var gpuAdapter = AdapterPicker(gpuFields, L.Text("Видеокарта"), "widget-editor-gpu-adapter", draft.GpuAdapterId,
            app.Gpu.Current.Adapters.Select(adapter => (adapter.Id, adapter.Name)));
        var networkFields = new StackPanel(); fields.Children.Add(networkFields);
        var networkAdapter = AdapterPicker(networkFields, L.Text("Сетевой адаптер"), "widget-editor-network-adapter", draft.NetworkAdapterId,
            app.SystemMetrics.CurrentNetwork.Interfaces.Select(adapter => (adapter.Id, adapter.Name)));
        var details = Ui.Text("", 11, Ui.Muted); details.LineHeight = 17; details.Margin = new Thickness(0, 5, 0, 0); fields.Children.Add(details);
        var previewHeading = Ui.Text(L.Text("Предпросмотр"), 12, Ui.Ink, FontWeights.SemiBold); previewHeading.Margin = new Thickness(0, 0, 0, 6); previewSide.Children.Add(previewHeading);
        var example = Ui.Text(L.Text("Пример оформления"), 10, Ui.Muted); example.Margin = new Thickness(0, 0, 0, 8); previewSide.Children.Add(example);
        var preview = new Border(); AutomationProperties.SetAutomationId(preview, "widget-editor-preview"); previewSide.Children.Add(preview);
        var draftHint = Ui.Text(L.Text("Изменения применятся после сохранения."), 10, Ui.Muted); draftHint.Margin = new Thickness(0, 8, 0, 0); previewSide.Children.Add(draftHint);
        Grid? picker = null;
        if (existingId == null)
        {
            picker = WidgetCatalog.Choices(selected => kind.SelectedIndex = (int)selected, "widget-editor-type-", compact: true, selected: draft.Kind); pickerHost.Child = picker;
        }
        else
        {
            var selected = Ui.Row(Brand.Icon(WidgetCatalog.Icon(draft.Kind), Ui.Accent, 20), Ui.Text(Labels[(int)draft.Kind], 15, Ui.Ink, FontWeights.SemiBold)); selected.Children[0].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0)); pickerHost.Child = selected; pickerHost.Margin = new Thickness(0, 0, 0, 18);
        }
        void State()
        {
            if (kind.SelectedIndex < 0 || kind.SelectedIndex >= Labels.Length) return;
            draft.Kind = (WidgetKind)kind.SelectedIndex; draft.Title = title.Text.Trim(); draft.Text = text.Text; draft.Use24HourClock = hours.IsChecked == true;
            draft.ProcessSort = (ProcessSortMode)Math.Clamp(processSort.SelectedIndex, 0, 1);
            draft.ProcessCount = processCount.SelectedItem is int count ? count : 5;
            draft.GpuAdapterId = (gpuAdapter.SelectedItem as AdapterChoice)?.Id ?? "";
            draft.NetworkAdapterId = (networkAdapter.SelectedItem as AdapterChoice)?.Id ?? "";
            if (draft.FocusMinutes != (int)minutes.Value) { draft.FocusMinutes = (int)minutes.Value; FocusClock.Reset(draft); }
            noteFields.Visibility = draft.Kind == WidgetKind.Note ? Visibility.Visible : Visibility.Collapsed;
            focusFields.Visibility = draft.Kind == WidgetKind.Focus ? Visibility.Visible : Visibility.Collapsed;
            clockFields.Visibility = draft.Kind == WidgetKind.Clock ? Visibility.Visible : Visibility.Collapsed;
            processFields.Visibility = draft.Kind == WidgetKind.Processes ? Visibility.Visible : Visibility.Collapsed;
            gpuFields.Visibility = draft.Kind == WidgetKind.Gpu ? Visibility.Visible : Visibility.Collapsed;
            networkFields.Visibility = draft.Kind == WidgetKind.Network ? Visibility.Visible : Visibility.Collapsed;
            details.Text = L.Text(draft.Kind switch
            {
                WidgetKind.Clock => "Формат даты использует язык приложения и часовой пояс Windows.",
                WidgetKind.Note => "Текст переносится автоматически. В группе заметку можно открыть для редактирования.",
                WidgetKind.Focus => "Запускайте, приостанавливайте и сбрасывайте таймер прямо в группе.",
                WidgetKind.System => "Загрузка CPU и память обновляются автоматически, когда группа видна.",
                WidgetKind.Drives => "Показывает локальные диски, свободное место и долю занятого пространства.",
                WidgetKind.CpuTemperature => "Для чтения температуры компонент датчиков устанавливается из комплекта программы. Интернет не нужен.",
                WidgetKind.Processes => "Показывает самые ресурсоёмкие процессы. Нагрузка CPU считается для всего процессора; недоступные процессы пропускаются.",
                WidgetKind.Memory => "Показывает физическую оперативную память: занято, доступно и всего. Файл подкачки не входит в эти значения.",
                WidgetKind.Gpu => "Выберите видеокарту или оставьте автоматический выбор. Показатели, которые адаптер не предоставляет, отображаются прочерком.",
                WidgetKind.Network => "Показывает скорость выбранного активного адаптера. Автоматический режим выбирает интерфейс с текущим трафиком.",
                WidgetKind.Battery => "На ноутбуке показывает заряд и состояние питания. Если батареи нет, виджет сообщает об этом.",
                WidgetKind.Cpu => "Отдельный виджет загрузки CPU. История появляется по мере получения реальных показаний.",
                WidgetKind.Uptime => "Показывает время с последней загрузки Windows. Режим быстрого запуска может сохранять время предыдущего сеанса.",
                _ => "Виджет"
            });
            if (picker != null)
            {
                foreach (Button button in picker.Children)
                {
                    var selected = AutomationProperties.GetAutomationId(button) == "widget-editor-type-" + draft.Kind;
                    button.Tag = selected ? "selected" : null; button.Background = selected ? Ui.AccentWash : Ui.Surface; button.BorderBrush = selected ? Ui.Accent : Ui.Line;
                    AutomationProperties.SetItemStatus(button, selected ? L.Text("Выбрано") : "");
                }
            }
            if (preview.Child is Border previous && previous.Child is WidgetHost host) host.Dispose();
            var source = app.Workspace.Fences.FirstOrDefault(f => f.Id == fenceId) ?? current.Fence;
            preview.Child = WidgetCatalog.Preview(app, source, draft);
        }
        kind.SelectionChanged += (_, _) => State(); title.TextChanged += (_, _) => State(); text.TextChanged += (_, _) => State(); minutes.ValueChanged += (_, _) => State(); hours.Checked += (_, _) => State(); hours.Unchecked += (_, _) => State();
        processSort.SelectionChanged += (_, _) => State(); processCount.SelectionChanged += (_, _) => State(); gpuAdapter.SelectionChanged += (_, _) => State(); networkAdapter.SelectionChanged += (_, _) => State(); State();
        dialog.AddSave(() => app.Mutate(() =>
        {
            if (separatePanel)
            {
                if (!app.Workspace.Pages.Any(page => page.Id == fence.PageId)) throw new InvalidOperationException(L.Text("Набор больше не существует."));
                draft.Title = title.Text.Trim(); draft.Text = text.Text; draft.Use24HourClock = hours.IsChecked == true;
                if (draft.FocusMinutes != (int)minutes.Value) { draft.FocusMinutes = (int)minutes.Value; FocusClock.Reset(draft); }
                SingleWidgetPanels.ApplySize(fence, draft.Kind); SingleWidgetPanels.Place(app, fence, display);
                fence.Name = string.IsNullOrWhiteSpace(draft.Title) ? Labels[(int)draft.Kind] : draft.Title;
                tab.Name = fence.Name; tab.Widgets.Clear(); tab.Widgets.Add(draft);
                app.Workspace.Fences.Add(fence); return;
            }
            var destination = Resolve(app, fenceId, tabId, existingId); WorkspaceActions.RequireEditable(destination.Fence);
            if (existingId == null && destination.Tab.Widgets.Count >= 20) throw new InvalidOperationException(L.Text("Не больше 20 виджетов во вкладке."));
            var widget = destination.Widget ?? new DesktopWidget { Id = draft.Id };
            widget.Kind = draft.Kind; widget.Title = title.Text.Trim(); widget.Text = text.Text; widget.Use24HourClock = hours.IsChecked == true;
            widget.ProcessSort = draft.ProcessSort; widget.ProcessCount = draft.ProcessCount; widget.GpuAdapterId = draft.GpuAdapterId; widget.NetworkAdapterId = draft.NetworkAdapterId;
            if (widget.FocusMinutes != (int)minutes.Value) { widget.FocusMinutes = (int)minutes.Value; FocusClock.Reset(widget); }
            if (existingId == null) destination.Tab.Widgets.Add(widget);
        }));
        dialog.Closed += (_, _) => { if (preview.Child is Border border && border.Child is WidgetHost host) host.Dispose(); }; return dialog.ShowDialog() == true;
    }
}
