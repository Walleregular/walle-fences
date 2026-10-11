using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class WidgetHost
{
    private ComboBox MetricSelector(string id)
    {
        var background = (Color)ColorConverter.ConvertFromString(_fence.Color);
        var accent = (Color)ColorConverter.ConvertFromString(_fence.Accent);
        Color Mix(double amount) => Color.FromRgb((byte)(background.R * (1 - amount) + accent.R * amount), (byte)(background.G * (1 - amount) + accent.G * amount), (byte)(background.B * (1 - amount) + accent.B * amount));
        var choice = new ComboBox { MinHeight = 28, FontSize = 10, Margin = new Thickness(0, 0, 0, 9), IsEnabled = !_preview && !_fence.ContentLocked };
        choice.Resources["Ink"] = _ink; choice.Resources["Muted"] = _muted;
        choice.Resources["Surface"] = new SolidColorBrush(background); choice.Resources["Raised"] = new SolidColorBrush(Mix(.12));
        choice.Resources["Hover"] = new SolidColorBrush(Mix(.16)); choice.Resources["Line"] = new SolidColorBrush(Mix(.28));
        choice.Resources["Accent"] = Ui.Brush(_fence.Accent); choice.Resources["FocusAccent"] = _accentInk;
        AutomationProperties.SetAutomationId(choice, id); return choice;
    }

    private TextBlock MetricValue(string id, double maximum = 36)
    {
        var value = Ui.Text("—", maximum, _ink, FontWeights.Medium); value.TextWrapping = TextWrapping.NoWrap;
        value.SizeChanged += (_, _) => FitValue(value, maximum);
        AutomationProperties.SetAutomationId(value, id); return value;
    }

    private static void AddMetricPoint(List<double> history, double? value)
    {
        if (value is not { } number || !double.IsFinite(number) || number < 0) { history.Clear(); return; }
        history.Add(number); if (history.Count > 60) history.RemoveAt(0);
    }

    private SystemSnapshot VisibleSystemSample() => _preview ? new(null, null, null, null) : SystemTelemetry.Snapshot();

    private void AddMemory(DesktopWidget widget)
    {
        var body = new StackPanel(); body.Children.Add(WidgetHeading(widget, "memory"));
        var value = MetricValue("widget-ram-value-" + widget.Id);
        var chart = new MetricSparkline(_accentInk, percentage: true) { Width = 76, Height = 42, Margin = new Thickness(10, 0, 0, 0), ToolTip = L.Text("История за последнюю минуту") };
        body.Children.Add(Split(value, chart));
        var meter = Meter("widget-ram-progress-" + widget.Id); meter.Margin = new Thickness(0, 9, 0, 9); body.Children.Add(meter);
        var used = Ui.Text("—", 11, _ink); var free = Ui.Text("—", 11, _muted); free.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(used); body.Children.Add(free);
        var history = new List<double>();
        _updates.Add(_ =>
        {
            var sample = VisibleSystemSample();
            value.Text = sample.MemoryPercent is { } percent ? percent.ToString("0", L.Culture) + "%" : "—"; FitValue(value, 36);
            SetMeter(meter, sample.MemoryPercent);
            used.Text = sample.UsedMemoryGb is { } usedGb && sample.TotalMemoryGb is { } totalGb ? L.Format("Занято: {0:0.#} / {1:0.#} ГБ", usedGb, totalGb) : L.Text(_preview ? "Показания появятся на рабочем столе." : "Нет показаний");
            free.Text = sample.UsedMemoryGb is { } usedMemory && sample.TotalMemoryGb is { } totalMemory ? L.Format("Свободно: {0:0.#} ГБ", Math.Max(0, totalMemory - usedMemory)) : "";
            AddMetricPoint(history, sample.MemoryPercent); chart.SetSamples(history);
            chart.Visibility = history.Count > 1 && body.ActualWidth >= 200 ? Visibility.Visible : Visibility.Collapsed;
        });
        Children.Add(Module(body, widget));
    }

    private void AddCpu(DesktopWidget widget)
    {
        var body = new StackPanel(); body.Children.Add(WidgetHeading(widget, "cpu"));
        var value = MetricValue("widget-cpu-only-value-" + widget.Id, 42);
        var chart = new MetricSparkline(_accentInk, percentage: true) { Width = 82, Height = 45, Margin = new Thickness(10, 0, 0, 0), ToolTip = L.Text("История за последнюю минуту") };
        body.Children.Add(Split(value, chart));
        var meter = Meter("widget-cpu-only-progress-" + widget.Id); meter.Margin = new Thickness(0, 10, 0, 8); body.Children.Add(meter);
        var detail = Ui.Text(L.Text("Загрузка CPU"), 10, _muted); body.Children.Add(detail);
        var history = new List<double>();
        _updates.Add(_ =>
        {
            var sample = VisibleSystemSample(); value.Text = CpuPercent(sample); FitValue(value, 42); SetMeter(meter, sample.CpuPercent);
            detail.Text = sample.CpuPercent != null ? L.Text("Загрузка CPU") : L.Text(_preview ? "Показания появятся на рабочем столе." : "Нет показаний");
            AddMetricPoint(history, sample.CpuPercent); chart.SetSamples(history);
            chart.Visibility = history.Count > 1 && body.ActualWidth >= 200 ? Visibility.Visible : Visibility.Collapsed;
        });
        Children.Add(Module(body, widget));
    }

    private sealed record ProcessView(Grid Row, TextBlock Name, TextBlock Cpu, TextBlock Memory, Border Activity);

    private void AddProcesses(DesktopWidget widget)
    {
        var body = new StackPanel();
        var sort = IconAction("sort", () => { widget.ProcessSort = widget.ProcessSort == ProcessSortMode.Cpu ? ProcessSortMode.Memory : ProcessSortMode.Cpu; _app.ScheduleSave(); Tick(); }, L.Text("Сортировать по памяти"), 28);
        sort.IsEnabled = !_preview && !_fence.ContentLocked;
        var taskManager = IconAction("external-link", () => Native.Open("taskmgr.exe"), L.Text("Открыть диспетчер задач"), 28);
        body.Children.Add(WidgetHeading(widget, "processes", action: Ui.Row(sort, taskManager)));
        var columns = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new GridLength(48) }); columns.ColumnDefinitions.Add(new() { Width = new GridLength(68) });
        var order = Ui.Text("", 9, _muted); columns.Children.Add(order);
        var cpuHeading = Ui.Text(L.Text("CPU"), 9, _muted); cpuHeading.TextAlignment = TextAlignment.Right; Grid.SetColumn(cpuHeading, 1); columns.Children.Add(cpuHeading);
        var ramHeading = Ui.Text(L.Text("RAM"), 9, _muted); ramHeading.TextAlignment = TextAlignment.Right; Grid.SetColumn(ramHeading, 2); columns.Children.Add(ramHeading); body.Children.Add(columns);
        var rows = new StackPanel(); var views = new Dictionary<int, ProcessView>();
        body.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var status = Ui.Text("", 10, _muted); status.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(status);
        var lastAt = DateTimeOffset.MinValue; var lastSort = (ProcessSortMode)(-1); var lastCount = -1;

        ProcessView CreateRow(int id)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 5) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(48) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(68) });
            row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var name = Ui.Text("", 11, _ink); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.Margin = new Thickness(0, 0, 6, 0);
            var cpu = Ui.Text("—", 10, _ink); cpu.TextWrapping = TextWrapping.NoWrap; cpu.TextAlignment = TextAlignment.Right;
            var memory = Ui.Text("—", 10, _muted); memory.TextWrapping = TextWrapping.NoWrap; memory.TextAlignment = TextAlignment.Right;
            row.Children.Add(name); Grid.SetColumn(cpu, 1); row.Children.Add(cpu); Grid.SetColumn(memory, 2); row.Children.Add(memory);
            var activity = Meter("widget-process-progress-" + widget.Id + "-" + id, height: 2); activity.Margin = new Thickness(0, 5, 0, 0); Grid.SetColumnSpan(activity, 3); Grid.SetRow(activity, 1); row.Children.Add(activity);
            AutomationProperties.SetAutomationId(row, "widget-process-" + widget.Id + "-" + id); return new(row, name, cpu, memory, activity);
        }

        _updates.Add(now =>
        {
            if (!_preview && IsVisible) _app.SystemMetrics.RequestProcesses();
            var sample = _app.SystemMetrics.CurrentProcesses;
            order.Text = L.Text(widget.ProcessSort == ProcessSortMode.Cpu ? "Сортировка: CPU" : "Сортировка: память");
            sort.ToolTip = L.Text(widget.ProcessSort == ProcessSortMode.Cpu ? "Сортировать по памяти" : "Сортировать по CPU"); AutomationProperties.SetName(sort, (string)sort.ToolTip);
            var fresh = !_preview && sample.At != DateTimeOffset.MinValue && sample.At <= now && now - sample.At < TimeSpan.FromSeconds(12);
            if (!fresh || sample.Error != null)
            {
                foreach (var view in views.Values) rows.Children.Remove(view.Row); views.Clear();
                lastAt = DateTimeOffset.MinValue; lastSort = (ProcessSortMode)(-1);
                status.Text = L.Text(_preview ? "Показания появятся на рабочем столе." : sample.Error != null ? "Процессы недоступны" : "Измеряем загрузку процессов…");
                status.Visibility = Visibility.Visible; status.ToolTip = sample.Error; return;
            }
            var count = Math.Clamp(widget.ProcessCount, 3, 10);
            status.Text = sample.Processes.Length == 0 ? L.Text("Нет доступных процессов") : sample.Sampling && widget.ProcessSort == ProcessSortMode.Cpu ? L.Text("Измеряем загрузку процессов…") : "";
            status.Visibility = status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; status.ToolTip = null;
            if (lastAt == sample.At && lastSort == widget.ProcessSort && lastCount == count) return;
            lastAt = sample.At; lastSort = widget.ProcessSort; lastCount = count;
            var ranked = (widget.ProcessSort == ProcessSortMode.Memory
                ? sample.Processes.OrderByDescending(process => process.MemoryBytes ?? -1).ThenByDescending(process => process.CpuPercent ?? -1)
                : sample.Processes.OrderByDescending(process => process.CpuPercent ?? -1).ThenByDescending(process => process.MemoryBytes ?? -1)).ThenBy(process => process.Id).Take(count).ToArray();
            var ids = ranked.Select(process => process.Id).ToHashSet();
            foreach (var stale in views.Keys.Where(id => !ids.Contains(id)).ToArray()) { rows.Children.Remove(views[stale].Row); views.Remove(stale); }
            var totalMemory = VisibleSystemSample().TotalMemoryGb * 1073741824;
            for (var index = 0; index < ranked.Length; index++)
            {
                var process = ranked[index];
                if (!views.TryGetValue(process.Id, out var view)) views[process.Id] = view = CreateRow(process.Id);
                var currentIndex = rows.Children.IndexOf(view.Row); if (currentIndex != index) { if (currentIndex >= 0) rows.Children.RemoveAt(currentIndex); rows.Children.Insert(index, view.Row); }
                view.Name.Text = process.Name; view.Name.ToolTip = process.Name + " · PID " + process.Id;
                view.Cpu.Text = process.CpuPercent is { } cpu ? cpu.ToString("0.#", L.Culture) + "%" : "—";
                view.Memory.Text = process.MemoryBytes is { } bytes ? bytes >= 1073741824 ? (bytes / 1073741824.0).ToString("0.#", L.Culture) + " " + L.Text("ГБ") : (bytes / 1048576.0).ToString("0", L.Culture) + " " + L.Text("МБ") : "—";
                double? percent = widget.ProcessSort == ProcessSortMode.Cpu ? process.CpuPercent : process.MemoryBytes is { } memoryBytes && totalMemory is > 0 ? 100 * memoryBytes / totalMemory.Value : null;
                SetMeter(view.Activity, percent);
                AutomationProperties.SetName(view.Row, process.Name); AutomationProperties.SetHelpText(view.Row, "CPU " + view.Cpu.Text + "; RAM " + view.Memory.Text);
            }
        });
        Children.Add(Module(body, widget));
    }
}

internal sealed class MetricSparkline(Brush accent, bool percentage = false, Brush? secondary = null) : FrameworkElement
{
    private double[] _values = [], _secondary = [];
    internal void SetSamples(IEnumerable<double> values, IEnumerable<double>? secondaryValues = null)
    {
        var first = values.Where(value => double.IsFinite(value) && value >= 0).TakeLast(60).ToArray();
        var second = secondaryValues?.Where(value => double.IsFinite(value) && value >= 0).TakeLast(60).ToArray() ?? [];
        if (_values.SequenceEqual(first) && _secondary.SequenceEqual(second)) return;
        _values = first; _secondary = second; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing); if (ActualWidth < 4 || ActualHeight < 4 || _values.Length < 2 && _secondary.Length < 2) return;
        var ceiling = percentage ? 100 : Math.Max(1, _values.Concat(_secondary).DefaultIfEmpty(0).Max() * 1.08);
        void Draw(double[] values, Brush brush, double thickness)
        {
            if (values.Length < 2) return;
            var geometry = new StreamGeometry(); using (var context = geometry.Open())
            {
                for (var index = 0; index < values.Length; index++)
                {
                    var point = new Point(1 + index * (ActualWidth - 2) / (values.Length - 1), 1 + (1 - Math.Clamp(values[index] / ceiling, 0, 1)) * (ActualHeight - 2));
                    if (index == 0) context.BeginFigure(point, false, false); else context.LineTo(point, true, false);
                }
            }
            geometry.Freeze(); drawing.DrawGeometry(null, new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
        }
        if (secondary != null) Draw(_secondary, secondary, 1.3);
        Draw(_values, accent, 1.8);
    }
}
