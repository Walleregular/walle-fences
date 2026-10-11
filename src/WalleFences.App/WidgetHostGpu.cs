using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class WidgetHost
{
    private void AddGpu(DesktopWidget widget)
    {
        var body = new StackPanel(); body.Children.Add(WidgetHeading(widget, "gpu", L.Text("Видеокарта")));
        var adapterName = Ui.Text(L.Text("Видеокарта"), 11, _muted); adapterName.TextWrapping = TextWrapping.NoWrap;
        adapterName.TextTrimming = TextTrimming.CharacterEllipsis; adapterName.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetAutomationId(adapterName, "widget-gpu-name-" + widget.Id); body.Children.Add(adapterName);
        var selector = new ComboBox { FontSize = 11, MinHeight = 30, Margin = new Thickness(0, 0, 0, 10), IsEnabled = !_preview && !_fence.ContentLocked, Visibility = Visibility.Collapsed };
        selector.Resources["Ink"] = _ink; selector.Resources["Muted"] = _muted; selector.Resources["Line"] = _line;
        selector.Resources["Surface"] = Ui.Brush(_fence.Color); selector.Resources["Raised"] = new SolidColorBrush(_ink.Color) { Opacity = .08 };
        selector.Resources["Hover"] = new SolidColorBrush(_ink.Color) { Opacity = .09 };
        selector.Resources["AccentWash"] = Ui.Brush(_fence.Accent, .16);
        selector.Resources["Accent"] = selector.Resources["FocusAccent"] = Ui.Brush(_fence.Accent);
        AutomationProperties.SetName(selector, L.Text("Выбрать видеокарту")); AutomationProperties.SetAutomationId(selector, "widget-gpu-adapter-" + widget.Id);
        body.Children.Add(selector);

        var value = Ui.Text("— %", 38, _ink, FontWeights.Medium); value.TextWrapping = TextWrapping.NoWrap;
        value.SizeChanged += (_, _) => FitValue(value, 38);
        AutomationProperties.SetAutomationId(value, "widget-gpu-load-" + widget.Id); body.Children.Add(value);
        var loadLabel = Ui.Text(L.Text("Загрузка GPU"), 10, _muted); loadLabel.Margin = new Thickness(0, 2, 0, 8); body.Children.Add(loadLabel);
        var loadMeter = Meter("widget-gpu-load-progress-" + widget.Id); body.Children.Add(loadMeter);

        var memoryValue = Ui.Text("—", 11, _ink); memoryValue.Margin = new Thickness(8, 0, 0, 0); memoryValue.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetAutomationId(memoryValue, "widget-gpu-memory-" + widget.Id);
        var memoryRow = Split(Ui.Text(L.Text("Видеопамять"), 10, _muted), memoryValue); memoryRow.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(memoryRow);
        var memoryMeter = Meter("widget-gpu-memory-progress-" + widget.Id, height: 3); memoryMeter.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(memoryMeter);
        var temperatureValue = Ui.Text("— °C", 11, _ink); temperatureValue.Margin = new Thickness(8, 0, 0, 0);
        AutomationProperties.SetAutomationId(temperatureValue, "widget-gpu-temperature-" + widget.Id);
        var temperatureRow = Split(Ui.Text(L.Text("Температура"), 10, _muted), temperatureValue); temperatureRow.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(temperatureRow);
        var state = Ui.Text("", 10, _muted); state.Margin = new Thickness(0, 10, 0, 0);
        AutomationProperties.SetAutomationId(state, "widget-gpu-state-" + widget.Id); body.Children.Add(state);

        var syncingSelector = false; string? adapterSignature = null;
        selector.SelectionChanged += (_, _) =>
        {
            if (syncingSelector || _preview || _fence.ContentLocked || selector.SelectedItem is not ComboBoxItem item || item.Tag is not string id || widget.GpuAdapterId == id) return;
            widget.GpuAdapterId = id; _app.ScheduleSave(); Update(DateTimeOffset.UtcNow);
        };
        void Update(DateTimeOffset now)
        {
            if (!_preview && IsVisible) _app.Gpu.Request();
            var sample = _preview ? new GpuSnapshot(DateTimeOffset.MinValue, []) : _app.Gpu.Current;
            var fresh = sample.At != DateTimeOffset.MinValue && now - sample.At >= TimeSpan.Zero && now - sample.At < TimeSpan.FromSeconds(15);
            var signature = string.Join("\n", sample.Adapters.Select(a => a.Id + "\t" + a.Name)) + "\n" + widget.GpuAdapterId;
            if (signature != adapterSignature)
            {
                adapterSignature = signature; syncingSelector = true;
                try
                {
                    selector.Items.Clear();
                    var automatic = new ComboBoxItem { Content = L.Text("Автоматически"), Tag = "", Foreground = _ink }; selector.Items.Add(automatic);
                    ComboBoxItem selected = automatic;
                    foreach (var adapter in sample.Adapters)
                    {
                        var item = new ComboBoxItem { Content = adapter.Name, Tag = adapter.Id, Foreground = _ink, ToolTip = adapter.Name };
                        selector.Items.Add(item); if (adapter.Id == widget.GpuAdapterId) selected = item;
                    }
                    if (widget.GpuAdapterId.Length > 0 && selected == automatic)
                    {
                        selected = new ComboBoxItem { Content = L.Text("Выбранная видеокарта недоступна"), Tag = widget.GpuAdapterId, IsEnabled = false, Foreground = _muted };
                        selector.Items.Add(selected);
                    }
                    selector.SelectedItem = selected;
                    selector.Visibility = sample.Adapters.Length > 1 || widget.GpuAdapterId.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                finally { syncingSelector = false; }
            }
            var gpu = widget.GpuAdapterId.Length > 0 ? sample.Adapters.FirstOrDefault(a => a.Id == widget.GpuAdapterId)
                : sample.Adapters.FirstOrDefault(a => a.LoadPercent != null || a.TemperatureC != null || a.UsedMemoryBytes != null) ?? sample.Adapters.FirstOrDefault();
            adapterName.Text = gpu?.Name ?? L.Text("Видеокарта"); adapterName.ToolTip = gpu?.Name;
            var load = fresh ? gpu?.LoadPercent : null;
            value.Text = load is { } percentage ? percentage.ToString("0", L.Culture) + "%" : "— %";
            FitValue(value, 38); SetMeter(loadMeter, load);
            var used = fresh ? gpu?.UsedMemoryBytes : null; var total = fresh ? gpu?.TotalMemoryBytes : null;
            memoryValue.Text = used is { } bytes ? bytes / 1073741824d < .1 && total == null
                ? (bytes / 1048576d).ToString("0", L.Culture) + " " + L.Text("МБ")
                : (bytes / 1073741824d).ToString("0.#", L.Culture) + (total is { } capacity ? " / " + (capacity / 1073741824d).ToString("0.#", L.Culture) : "") + " " + L.Text("ГБ")
                : total is { } availableCapacity ? "— / " + (availableCapacity / 1073741824d).ToString("0.#", L.Culture) + " " + L.Text("ГБ") : "—";
            memoryValue.ToolTip = memoryValue.Text; SetMeter(memoryMeter, used is { } usedBytes && total is > 0 ? 100d * usedBytes / total.Value : null);
            memoryMeter.Visibility = used != null && total is > 0 ? Visibility.Visible : Visibility.Collapsed;
            var temperature = fresh ? gpu?.TemperatureC : null;
            temperatureValue.Text = temperature is { } celsius ? celsius.ToString("0.#", L.Culture) + " °C" : "— °C";
            var missingSelected = widget.GpuAdapterId.Length > 0 && gpu == null;
            state.Text = _preview ? L.Text("Показания появятся на рабочем столе.")
                : sample.At == DateTimeOffset.MinValue ? L.Text("Чтение GPU…")
                : missingSelected ? L.Text("Выбранная видеокарта недоступна. Выберите другую.")
                : gpu == null ? L.Text("GPU недоступен. Проверьте поддержку видеокарты и её драйвер.")
                : !fresh ? L.Text("Обновление показаний…")
                : load == null && temperature == null && used == null ? gpu.Id.StartsWith("windows-gpu/", StringComparison.Ordinal)
                    ? L.Text("Видеокарта найдена в Windows. Показания датчиков недоступны.") : L.Text("Драйвер GPU не предоставил показания.") : "";
            state.Visibility = state.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            state.ToolTip = sample.Detail ?? (state.Text.Length > 0 ? state.Text : null);
            value.ToolTip = load == null && state.Text.Length > 0 ? state.Text : null;
            AutomationProperties.SetHelpText(value, L.Text("Загрузка GPU") + " · " + value.Text);
        }
        _updates.Add(Update); Children.Add(Module(body, widget));
    }
}
