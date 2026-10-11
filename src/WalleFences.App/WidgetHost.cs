using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class WidgetHost : StackPanel, IDisposable
{
    private readonly AppController _app;
    private readonly Fence _fence;
    private readonly bool _preview;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<Action<DateTimeOffset>> _updates = [];
    private readonly Dictionary<string, List<TemperaturePoint>> _temperatureHistory = [];
    private readonly SolidColorBrush _ink, _muted, _line, _accentInk;
    private string? _signature;
    private bool _disposed;

    internal bool IsPreview => _preview;
    internal bool TimerRunning => _timer.IsEnabled;
    internal WidgetHost(AppController app, Fence fence, bool preview = false)
    {
        _app = app; _fence = fence; _preview = preview; Margin = new Thickness(12, 4, 12, 2);
        var background = (Color)ColorConverter.ConvertFromString(fence.Color);
        _ink = new(GroupColorAppearance.Text(background)); _muted = new(GroupColorAppearance.Muted(background)); _line = new(_ink.Color) { Opacity = .085 };
        var accent = (Color)ColorConverter.ConvertFromString(fence.Accent); _accentInk = GroupColorAppearance.Contrast(accent, background) >= 4.5 ? new(accent) : _ink;
        _timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Tick(); if (!_preview && _updates.Count > 0) _timer.Start(); } else _timer.Stop(); };
    }

    internal void Sync()
    {
        var signature = JsonSerializer.Serialize(new { Tab = _fence.ActiveTab.Id, Widgets = _fence.ActiveTab.Widgets.Select(w => new { w.Id, w.Kind, w.Title, w.Text, w.FocusMinutes, w.Use24HourClock, w.ProcessSort, w.ProcessCount, w.GpuAdapterId, w.NetworkAdapterId }) });
        if (_signature == signature) return;
        _signature = signature; _updates.Clear(); Children.Clear();
        foreach (var widget in _fence.ActiveTab.Widgets)
        {
            switch (widget.Kind)
            {
                case WidgetKind.Clock: AddClock(widget); break;
                case WidgetKind.Note: AddNote(widget); break;
                case WidgetKind.Focus: AddFocus(widget); break;
                case WidgetKind.System: AddSystem(widget); break;
                case WidgetKind.CpuTemperature: AddTemperature(widget); break;
                case WidgetKind.Drives: AddDrives(widget, signature); break;
                case WidgetKind.Processes: AddProcesses(widget); break;
                case WidgetKind.Memory: AddMemory(widget); break;
                case WidgetKind.Gpu: AddGpu(widget); break;
                case WidgetKind.Network: AddNetwork(widget); break;
                case WidgetKind.Battery: AddBattery(widget); break;
                case WidgetKind.Cpu: AddCpu(widget); break;
                case WidgetKind.Uptime: AddUptime(widget); break;
            }
        }
        Visibility = Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Tick(); if (!_preview && IsVisible && _updates.Count > 0) _timer.Start(); else _timer.Stop();
    }

    private string Title(DesktopWidget widget, string? fallback = null) => string.IsNullOrWhiteSpace(widget.Title) || widget.Title == WidgetEditor.Labels[(int)widget.Kind] ? fallback ?? WidgetEditor.Labels[(int)widget.Kind] : widget.Title;
    private Border Module(UIElement child, DesktopWidget widget)
    {
        var module = new Border { Child = child, Padding = new Thickness(12, 11, 12, 12), Background = new SolidColorBrush(_ink.Color) { Opacity = .023 }, BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 0, 10) };
        AutomationProperties.SetAutomationId(module, "widget-module-" + widget.Id); return module;
    }
    private Grid WidgetHeading(DesktopWidget widget, string icon, string? fallback = null, UIElement? action = null)
    {
        var label = new Grid(); label.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); label.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var glyph = Brand.Icon(icon, _accentInk, 16); glyph.Margin = new Thickness(0, 0, 8, 0); AutomationProperties.SetAutomationId(glyph, "widget-kind-" + widget.Id); label.Children.Add(glyph);
        var heading = Heading(Title(widget, fallback)); heading.Margin = new Thickness(0); heading.TextWrapping = TextWrapping.NoWrap; heading.TextTrimming = TextTrimming.CharacterEllipsis; heading.VerticalAlignment = VerticalAlignment.Center; heading.ToolTip = Title(widget, fallback); Grid.SetColumn(heading, 1); label.Children.Add(heading);
        var row = action == null ? label : Split(label, action); row.Margin = new Thickness(0, 0, 0, 11); return row;
    }
    private TextBlock Heading(string title) { var text = Ui.Text(title, 11, _ink); text.Margin = new Thickness(0, 0, 0, 10); return text; }
    private Border Meter(string id, Brush? accent = null, double height = 7)
    {
        var fill = new Border { Background = accent ?? Ui.Brush(_fence.Accent), CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        var track = new Border { Background = new SolidColorBrush(_ink.Color) { Opacity = .12 }, CornerRadius = new CornerRadius(height / 2), Height = height, Child = fill, ClipToBounds = true };
        AutomationProperties.SetAutomationId(track, id); track.Tag = 0d;
        track.SizeChanged += (_, _) => fill.Width = Math.Max(0, track.ActualWidth * (double)track.Tag);
        return track;
    }
    private static void SetMeter(Border track, double? percentage)
    {
        var fraction = percentage is { } value && double.IsFinite(value) ? Math.Clamp(value / 100, 0, 1) : 0;
        track.Tag = fraction;
        if (track.Child is Border fill) fill.Width = Math.Max(0, track.ActualWidth * fraction);
        AutomationProperties.SetHelpText(track, percentage is { } number && double.IsFinite(number) ? number.ToString("0", L.Culture) + "%" : L.Text("Недоступно"));
    }
    private Button IconAction(string icon, Action action, string tooltip, double size = 32)
    {
        var button = Ui.Tiny("", action, tooltip);
        button.Resources["Accent"] = button.Resources["FocusAccent"] = Ui.Brush(_fence.Accent);
        button.Content = Brand.Icon(icon, _muted, 18); button.Width = button.Height = size; button.MinHeight = 0; button.Padding = new Thickness(0); button.Margin = new Thickness(0); button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
        button.IsEnabled = !_preview;
        AutomationProperties.SetName(button, tooltip); return button;
    }
    private Grid Split(UIElement left, UIElement right)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(left); Grid.SetColumn(right, 1); row.Children.Add(right); return row;
    }
    private static void FitValue(TextBlock value, double maximum)
    {
        if (value.ActualWidth <= 0 || string.IsNullOrEmpty(value.Text)) return;
        var text = new FormattedText(value.Text, L.Culture, value.FlowDirection, new Typeface(value.FontFamily, value.FontStyle, value.FontWeight, value.FontStretch), maximum, value.Foreground, VisualTreeHelper.GetDpi(value).PixelsPerDip);
        value.FontSize = Math.Min(maximum, Math.Max(14, maximum * Math.Max(0, value.ActualWidth - 1) / Math.Max(1, text.WidthIncludingTrailingWhitespace)));
    }

    private void AddClock(DesktopWidget widget)
    {
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(WidgetHeading(widget, "clock"));
        var value = Ui.Text("", 52, _ink, FontWeights.Medium); value.TextWrapping = TextWrapping.NoWrap; value.TextAlignment = TextAlignment.Center;
        AutomationProperties.SetAutomationId(value, "widget-clock-value"); body.Children.Add(value);
        var date = Ui.Text("", 12, _muted); date.Margin = new Thickness(0, 0, 0, 4); date.TextAlignment = TextAlignment.Center; body.Children.Add(date);
        value.SizeChanged += (_, _) => FitValue(value, 54);
        _updates.Add(now => { value.Text = now.ToLocalTime().ToString(widget.Use24HourClock ? "HH:mm" : "h:mm tt", L.Culture); FitValue(value, 54); date.Text = now.ToLocalTime().ToString("dddd, d MMMM", L.Culture); });
        Children.Add(Module(body, widget));
    }

    private void AddNote(DesktopWidget widget)
    {
        var body = new StackPanel();
        var edit = IconAction("edit", () => WidgetEditor.Edit(_app, _fence, _fence.ActiveTab, widget), L.Text("Редактировать") + " · " + Title(widget)); edit.IsEnabled = !_preview && !_fence.ContentLocked;
        AutomationProperties.SetAutomationId(edit, "widget-note-edit-" + widget.Id);
        body.Children.Add(WidgetHeading(widget, "note", action: edit));
        var value = Ui.Text(string.IsNullOrWhiteSpace(widget.Text) ? L.Text("Добавьте текст в настройках виджета.") : widget.Text, 12, _ink);
        value.LineHeight = 20; value.TextTrimming = TextTrimming.CharacterEllipsis; value.MaxHeight = 180;
        AutomationProperties.SetAutomationId(value, "widget-note-value-" + widget.Id);
        body.Children.Add(value); Children.Add(Module(body, widget));
    }

    private void AddFocus(DesktopWidget widget)
    {
        var body = new StackPanel();
        var reset = IconAction("reset", () => { FocusClock.Reset(widget); _app.ScheduleSave(); Tick(); }, L.Text("Сбросить") + " · " + Title(widget));
        AutomationProperties.SetAutomationId(reset, "widget-focus-reset-" + widget.Id);
        body.Children.Add(WidgetHeading(widget, "clock", action: reset));
        var value = Ui.Text("", 34, _ink, FontWeights.Medium); value.TextWrapping = TextWrapping.NoWrap;
        value.SizeChanged += (_, _) => FitValue(value, 34);
        AutomationProperties.SetAutomationId(value, "widget-focus-value");
        var toggle = Ui.Button("", () => { if (widget.FocusEndsAt == null) FocusClock.Start(widget, DateTimeOffset.UtcNow); else FocusClock.Pause(widget, DateTimeOffset.UtcNow); _app.ScheduleSave(); Tick(); });
        toggle.Background = Ui.Brush(_fence.Accent); toggle.Foreground = new SolidColorBrush(GroupColorAppearance.Text((Color)ColorConverter.ConvertFromString(_fence.Accent))); toggle.BorderThickness = new Thickness(0); toggle.Width = toggle.Height = 43; toggle.MinHeight = 0; toggle.Padding = new Thickness(11); toggle.Margin = new Thickness(8, 0, 0, 0);
        toggle.Resources["FocusAccent"] = toggle.Foreground;
        toggle.IsEnabled = !_preview;
        toggle.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding Foreground}' CornerRadius='24' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Opacity' Value='.85'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Opacity' Value='.65'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderThickness' Value='2'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        AutomationProperties.SetAutomationId(toggle, "widget-focus-toggle"); body.Children.Add(Split(value, toggle));
        var progress = Meter("widget-focus-progress", height: 5); progress.Margin = new Thickness(0, 12, 0, 9); body.Children.Add(progress);
        var detail = Ui.Text("", 10, _muted); AutomationProperties.SetAutomationId(detail, "widget-focus-state");
        var duration = Ui.Text(L.Format("{0} мин", widget.FocusMinutes), 10, _muted); duration.Margin = new Thickness(8, 0, 0, 0); body.Children.Add(Split(detail, duration));
        bool? previousPaused = null;
        _updates.Add(now =>
        {
            var seconds = FocusClock.Remaining(widget, now); value.Text = $"{seconds / 60:00}:{seconds % 60:00}"; FitValue(value, 34);
            var paused = widget.FocusEndsAt == null;
            var total = Math.Max(1, widget.FocusMinutes * 60); SetMeter(progress, 100d * (1 - Math.Clamp(seconds / (double)total, 0, 1)));
            detail.Text = seconds == 0 ? L.Text("Завершено") : !paused ? L.Text("Фокус включён") : seconds < total ? L.Text("На паузе") : L.Text("Готов к работе");
            detail.Foreground = !paused && seconds > 0 ? _accentInk : _muted;
            if (previousPaused != paused)
            {
                previousPaused = paused;
                toggle.Content = Brand.Icon(paused ? "play" : "pause", toggle.Foreground, 18);
                toggle.ToolTip = L.Text(paused ? "Начать" : "Пауза"); AutomationProperties.SetName(toggle, L.Text(paused ? "Начать" : "Пауза"));
            }
        });
        Children.Add(Module(body, widget));
    }

    private void AddSystem(DesktopWidget widget)
    {
        var body = new StackPanel(); body.Children.Add(WidgetHeading(widget, "memory", L.Text("Память")));
        var value = Ui.Text("", 24, _ink, FontWeights.Medium); value.TextWrapping = TextWrapping.NoWrap; value.TextTrimming = TextTrimming.CharacterEllipsis;
        value.SizeChanged += (_, _) =>
        {
            value.FontSize = Math.Clamp(value.ActualWidth * .16, 18, 24);
            if (value.Inlines.LastInline is System.Windows.Documents.Run units) units.FontSize = Math.Clamp(value.ActualWidth * .08, 10, 13);
        };
        AutomationProperties.SetAutomationId(value, "widget-memory-value");
        var percentage = Ui.Text("", 11, _ink); percentage.Margin = new Thickness(10, 0, 0, 0); body.Children.Add(Split(value, percentage));
        var meter = Meter("widget-memory-progress"); meter.Margin = new Thickness(0, 9, 0, 0); body.Children.Add(meter);
        TextBlock? cpu = null; Border? cpuMeter = null;
        if (!_fence.ActiveTab.Widgets.Any(w => w.Kind == WidgetKind.CpuTemperature))
        {
            cpu = Ui.Text("", 11, _muted); cpu.Margin = new Thickness(0, 12, 0, 0); AutomationProperties.SetAutomationId(cpu, "widget-cpu-load-value"); body.Children.Add(cpu);
            cpuMeter = Meter("widget-cpu-load-progress", height: 3); cpuMeter.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(cpuMeter);
        }
        var memoryHint = Ui.Text(L.Text("Использовано памяти"), 10, _muted); memoryHint.Margin = new Thickness(0, 7, 0, 0); body.Children.Add(memoryHint);
        string? lastMemoryText = null;
        _updates.Add(_ =>
        {
            var sample = SystemTelemetry.Snapshot();
            var usedText = sample.UsedMemoryGb is { } usedGb ? usedGb.ToString("0.#", L.Culture) : "—";
            var totalText = sample.TotalMemoryGb is { } totalGb ? " / " + totalGb.ToString("0.#", L.Culture) + " " + L.Text("ГБ") : " " + L.Text("ГБ");
            if (lastMemoryText != usedText + totalText)
            {
                lastMemoryText = usedText + totalText; value.ToolTip = lastMemoryText; value.Inlines.Clear();
                value.Inlines.Add(new System.Windows.Documents.Run(usedText));
                value.Inlines.Add(new System.Windows.Documents.Run(totalText) { FontSize = value.ActualWidth > 0 ? Math.Clamp(value.ActualWidth * .08, 10, 13) : 13, Foreground = _muted });
            }
            percentage.Text = sample.MemoryPercent is { } memory ? memory.ToString("0", L.Culture) + "%" : "—"; SetMeter(meter, sample.MemoryPercent);
            if (cpu != null) cpu.Text = L.Text("Загрузка CPU") + "  " + CpuPercent(sample);
            if (cpuMeter != null) SetMeter(cpuMeter, sample.CpuPercent);
        });
        Children.Add(Module(body, widget));
    }

    private static string CpuPercent(SystemSnapshot sample) => sample.CpuPercent is { } cpu ? cpu.ToString("0", L.Culture) + "%" : "—";
    private void AddTemperature(DesktopWidget widget)
    {
        var body = new StackPanel(); body.Children.Add(WidgetHeading(widget, "temperature", L.Text("Процессор")));
        var state = Ui.Text("", 10, _muted); state.Margin = new Thickness(0, 0, 0, 6); AutomationProperties.SetAutomationId(state, "cpu-sensor-state"); body.Children.Add(state);
        var value = Ui.Text("— °C", 36, _ink, FontWeights.Medium); value.TextWrapping = TextWrapping.NoWrap; AutomationProperties.SetAutomationId(value, "cpu-temperature-value");
        value.SizeChanged += (_, _) => { if (value.TextWrapping == TextWrapping.NoWrap) FitValue(value, 36); };
        var graph = new TemperatureSparkline(Ui.Brush(_fence.Accent)) { Width = 72, Height = 49, Margin = new Thickness(10, 0, 0, 0), ToolTip = L.Text("Температура за последние 90 секунд") };
        AutomationProperties.SetAutomationId(graph, "cpu-temperature-sparkline");
        var temperature = new Grid(); temperature.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); temperature.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        temperature.Children.Add(value); Grid.SetColumn(graph, 1); temperature.Children.Add(graph); body.Children.Add(temperature);
        var detail = Ui.Text("", 10, _muted); detail.Margin = new Thickness(0, 3, 0, 7); body.Children.Add(detail);
        var load = Ui.Text(L.Text("Загрузка"), 10, _muted); var cpu = Ui.Text("—", 11, _ink); AutomationProperties.SetAutomationId(cpu, "widget-cpu-load-value"); body.Children.Add(Split(load, cpu));
        var cpuMeter = Meter("widget-cpu-load-progress", height: 3); cpuMeter.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(cpuMeter);
        var driver = Ui.Button(L.Text("Включить аппаратные датчики"), () => _ = _app.EnableHardwareSensorsAsync()); driver.FontSize = 10; driver.Padding = new Thickness(8, 5, 8, 5); driver.Margin = new Thickness(0, 8, 0, 0); driver.Background = Brushes.Transparent; driver.BorderBrush = _line;
        driver.Resources["Accent"] = driver.Resources["FocusAccent"] = Ui.Brush(_fence.Accent);
        driver.IsEnabled = !_preview && !_app.HardwareSensors.IsBusy;
        var driverLabel = new Grid(); driverLabel.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); driverLabel.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var driverIcon = Brand.Icon("temperature", _muted, 16); driverIcon.Margin = new Thickness(0, 0, 7, 0); driverLabel.Children.Add(driverIcon);
        var driverText = Ui.Text(L.Text("Включить аппаратные датчики"), 10, _muted); driverText.TextWrapping = TextWrapping.Wrap; Grid.SetColumn(driverText, 1); driverLabel.Children.Add(driverText); driver.Content = driverLabel;
        driver.ToolTip = L.Text("Подтвердите доступ к датчикам в окне Windows. Интерфейс программы продолжит работать с обычными правами."); AutomationProperties.SetName(driver, L.Text("Включить аппаратные датчики")); AutomationProperties.SetAutomationId(driver, "cpu-sensor-driver"); body.Children.Add(driver);
        if (!_temperatureHistory.TryGetValue(widget.Id, out var history)) _temperatureHistory[widget.Id] = history = [];
        _updates.Add(now =>
        {
            if (!_preview && IsVisible) _app.CpuTemperature.Request();
            var sample = _app.CpuTemperature.Current;
            var fresh = sample.At <= now && now - sample.At < TimeSpan.FromSeconds(15);
            var available = sample.State == TemperatureState.Available && fresh && sample.Cpus.Length > 0 && sample.Cpus.All(c => float.IsFinite(c.Primary.Value) && c.Primary.Value is > 0 and <= 150);
            state.Text = available ? L.Text("Датчик доступен") : sample.State == TemperatureState.Reading && !_preview ? L.Text("Чтение датчиков…") : L.Text("Нет показаний");
            state.Foreground = available ? _accentInk : _muted;
            value.Text = available ? string.Join(" / ", sample.Cpus.Select(c => c.Primary.Value.ToString("0.#", L.Culture) + " °C")) : "— °C";
            value.FontSize = sample.Cpus.Length > 1 ? 24 : 36;
            value.TextWrapping = sample.Cpus.Length > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap;
            if (value.TextWrapping == TextWrapping.NoWrap) FitValue(value, 36);
            detail.Text = sample.State switch
            {
                TemperatureState.Available when available => string.Join("\n", sample.Cpus.Select(c => c.Name)),
                TemperatureState.DriverMissing => L.Text("Компонент датчиков уже входит в программу. Подтвердите установку в окне Windows."),
                TemperatureState.AccessRequired => L.Text("Разрешите доступ к аппаратным датчикам. Компонент уже входит в программу."),
                TemperatureState.Reading => _preview ? "" : L.Text("Чтение датчиков…"),
                _ => L.Text("Датчик CPU недоступен. Проверьте поддержку процессора и права доступа.")
            };
            value.ToolTip = available ? string.Join("\n", sample.Cpus.SelectMany(c => c.Sensors.Select(s => c.Name + " · " + s.Name + " · " + s.Value.ToString("0.#", L.Culture) + " °C"))) : sample.Detail;
            history.RemoveAll(point => now - point.At > TimeSpan.FromSeconds(90));
            if (available && (history.Count == 0 || history[^1].At != sample.At)) history.Add(new(sample.At, sample.Cpus[0].Primary.Value));
            graph.SetSamples(available ? history : []); graph.Visibility = available && history.Count > 1 && body.ActualWidth >= 210 ? Visibility.Visible : Visibility.Collapsed;
            var needsAccess = sample.State is TemperatureState.DriverMissing or TemperatureState.AccessRequired;
            driver.Visibility = needsAccess || _app.HardwareSensors.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            driver.IsEnabled = !_preview && !_app.HardwareSensors.IsBusy;
            driverText.Text = L.Text(_app.HardwareSensors.IsBusy ? "Настройка датчиков…" : "Включить аппаратные датчики");
            if (!_preview && (needsAccess || _app.HardwareSensors.IsBusy) && _app.HardwareSensors.StatusKey.Length > 0)
                detail.Text = L.Text(_app.HardwareSensors.StatusKey);
            var system = SystemTelemetry.Snapshot(); cpu.Text = CpuPercent(system); SetMeter(cpuMeter, system.CpuPercent);
        });
        Children.Add(Module(body, widget));
    }

    private void AddDrives(DesktopWidget widget, string signature)
    {
        var body = new StackPanel(); body.Children.Add(WidgetHeading(widget, "drive"));
        var stack = new StackPanel(); body.Children.Add(stack); Children.Add(Module(body, widget));
        stack.Children.Add(Ui.Text(L.Text("Загрузка…"), 11, _muted));
        void RenderDrives(DriveSnapshot[] drives)
        {
            stack.Children.Clear();
            if (drives.Length == 0) { stack.Children.Add(Ui.Text(L.Text("Недоступно"), 11, _muted)); return; }
            foreach (var drive in drives)
            {
                if (stack.Children.Count > 0) stack.Children.Add(new Border { Height = 1, Background = _line, Margin = new Thickness(0, 11, 0, 11) });
                var row = new StackPanel();
                var name = drive.System ? L.Text("Система") : drive.Label.Length > 0 ? drive.Label : L.Text("Диск");
                var title = Ui.Text(name + " (" + drive.Name + ")", 11, _ink, FontWeights.Medium); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; title.ToolTip = title.Text; row.Children.Add(title);
                var value = Ui.Text(drive.Total is > 0 && drive.Free is { } free ? L.Format("Свободно {0:0.#} ГБ", Math.Clamp(free, 0, drive.Total.Value) / 1073741824.0) : L.Text("Недоступно"), 11, _muted); value.Margin = new Thickness(0, 4, 0, 0); value.TextWrapping = TextWrapping.NoWrap; value.TextTrimming = TextTrimming.CharacterEllipsis;
                var percentage = drive.Total is > 0 && drive.Free is { } available ? Math.Clamp(100.0 * (drive.Total.Value - available) / drive.Total.Value, 0, 100) : (double?)null;
                value.ToolTip = drive.Total is > 0 ? L.Format("Всего {0:0.#} ГБ", drive.Total.Value / 1073741824.0) : L.Text("Недоступно");
                var percent = Ui.Text(percentage is { } used ? used.ToString("0", L.Culture) + "%" : "—", 10, _ink); percent.Margin = new Thickness(8, 4, 0, 0); percent.ToolTip = L.Text("Использовано места");
                var id = drive.Name.Replace(":", "").Replace("\\", "-").Replace("/", "-"); AutomationProperties.SetAutomationId(value, "widget-drive-value-" + id);
                row.Children.Add(Split(value, percent)); var meter = Meter("widget-drive-progress-" + id, drive.System ? Ui.Brush("#68B8E8") : null); meter.Margin = new Thickness(0, 7, 0, 0); SetMeter(meter, percentage); row.Children.Add(meter); stack.Children.Add(row);
            }
        }
        if (_preview) { RenderDrives([new("C:", "", 512L * 1073741824, 320L * 1073741824, true)]); return; }
        var last = long.MinValue; var reading = false;
        _updates.Add(now =>
        {
            if (!IsVisible || reading || last != long.MinValue && Environment.TickCount64 - last < 30000) return;
            reading = true; last = Environment.TickCount64; _ = ReadDrives();
            async Task ReadDrives()
            {
                var drives = await Task.Run(() =>
                {
                    try
                    {
                        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                        return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).Select(d =>
                        {
                            var name = d.Name.TrimEnd('\\'); var system = string.Equals(d.Name, systemRoot, StringComparison.OrdinalIgnoreCase);
                            try { return new DriveSnapshot(name, d.VolumeLabel, d.TotalSize, d.AvailableFreeSpace, system); }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new DriveSnapshot(name, "", null, null, system); }
                        }).OrderByDescending(d => d.System).ThenBy(d => d.Name).ToArray();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<DriveSnapshot>(); }
                });
                reading = false; if (_disposed || signature != _signature) return;
                RenderDrives(drives);
            }
        });
    }

    private void Tick() { if (!_disposed && IsVisible) foreach (var update in _updates.ToArray()) update(DateTimeOffset.UtcNow); }
    public void Dispose() { _disposed = true; _timer.Stop(); _updates.Clear(); _temperatureHistory.Clear(); _networkHistory.Clear(); }
    private sealed record DriveSnapshot(string Name, string Label, long? Total, long? Free, bool System);
}

internal sealed record TemperaturePoint(DateTimeOffset At, double Value);
internal sealed class TemperatureSparkline(Brush accent) : FrameworkElement
{
    private TemperaturePoint[] _samples = [];
    internal void SetSamples(IEnumerable<TemperaturePoint> points)
    {
        var samples = points.Where(p => double.IsFinite(p.Value) && p.Value is > 0 and <= 150).ToArray();
        if (_samples.SequenceEqual(samples)) return;
        _samples = samples; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing); if (_samples.Length < 2 || ActualWidth < 4 || ActualHeight < 4) return;
        var min = _samples.Min(p => p.Value); var max = _samples.Max(p => p.Value); var range = Math.Max(8, max - min + 4); var low = (min + max - range) / 2;
        var first = _samples[0].At; var seconds = Math.Max(1, (_samples[^1].At - first).TotalSeconds);
        var geometry = new StreamGeometry(); using (var context = geometry.Open())
        {
            for (var i = 0; i < _samples.Length; i++)
            {
                var point = new Point(2 + (_samples[i].At - first).TotalSeconds / seconds * (ActualWidth - 4), 3 + (1 - (_samples[i].Value - low) / range) * (ActualHeight - 6));
                if (i == 0) context.BeginFigure(point, false, false); else context.LineTo(point, true, false);
            }
        }
        geometry.Freeze(); drawing.DrawGeometry(null, new Pen(accent, 1.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
    }
}

internal sealed record SystemSnapshot(double? CpuPercent, double? UsedMemoryGb, double? TotalMemoryGb, double? MemoryPercent);
internal static class SystemTelemetry
{
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    private static ulong _idle, _total;
    private static long _last = long.MinValue;
    private static SystemSnapshot _cached = new(null, null, null, null);
    internal static SystemSnapshot Snapshot()
    {
        if (_last != long.MinValue && Environment.TickCount64 - _last < 1500) return _cached;
        _last = Environment.TickCount64; double? cpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel.Value + user.Value;
            if (_total != 0 && total > _total && idle.Value >= _idle) cpu = Math.Clamp(100.0 * (1 - (idle.Value - _idle) / (double)(total - _total)), 0, 100);
            _total = total; _idle = idle.Value;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        _cached = GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0
            ? new(cpu, (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824.0, memory.TotalPhysical / 1073741824.0, 100.0 * (memory.TotalPhysical - memory.AvailablePhysical) / memory.TotalPhysical)
            : new(cpu, null, null, null);
        return _cached;
    }
    internal static string Read()
    {
        var sample = Snapshot();
        return "CPU  " + (sample.CpuPercent is { } cpu ? cpu.ToString("0", L.Culture) + "%" : "—") + "\n" + (sample.TotalMemoryGb is { } total ? L.Format("Память · {0:0.#} / {1:0.#} ГБ", sample.UsedMemoryGb, total) : L.Text("Недоступно"));
    }
}
