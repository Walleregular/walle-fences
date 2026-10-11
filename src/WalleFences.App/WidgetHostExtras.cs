using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class WidgetHost
{
    private sealed record NetworkHistoryPoint(DateTimeOffset At, double Receive, double Send);
    private sealed class NetworkHistoryState
    {
        internal string? AdapterId;
        internal readonly List<NetworkHistoryPoint> Points = [];
    }
    private readonly Dictionary<string, NetworkHistoryState> _networkHistory = new();

    private void AddNetwork(DesktopWidget widget)
    {
        var body = new StackPanel(); body.Children.Add(WidgetHeading(widget, "network", L.Text("Сеть")));
        var selector = MetricSelector("widget-network-adapter-" + widget.Id);
        selector.IsEnabled = !_preview && !_fence.ContentLocked;
        AutomationProperties.SetName(selector, L.Text("Сетевой адаптер")); body.Children.Add(selector);
        var adapterName = Ui.Text("", 10, _muted); adapterName.TextWrapping = TextWrapping.NoWrap;
        adapterName.TextTrimming = TextTrimming.CharacterEllipsis; adapterName.Margin = new Thickness(0, 0, 0, 10);
        AutomationProperties.SetAutomationId(adapterName, "widget-network-name-" + widget.Id); body.Children.Add(adapterName);

        var secondary = new SolidColorBrush(_ink.Color) { Opacity = .65 };
        var receiveValue = Ui.Text("—", 29, _ink, FontWeights.Medium);
        var sendValue = Ui.Text("—", 29, _ink, FontWeights.Medium);
        var rates = new Grid(); rates.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        rates.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var receivePanel = NetworkRatePanel(receiveValue, "download", L.Text("Приём"), _accentInk, "widget-network-receive-" + widget.Id);
        var sendPanel = NetworkRatePanel(sendValue, "upload", L.Text("Передача"), secondary, "widget-network-send-" + widget.Id);
        receivePanel.Margin = new Thickness(0, 0, 10, 0); sendPanel.Margin = new Thickness(10, 0, 0, 0);
        rates.Children.Add(receivePanel); Grid.SetColumn(sendPanel, 1); rates.Children.Add(sendPanel);
        body.Children.Add(rates);
        var graph = new MetricSparkline(_accentInk, secondary: secondary) { Height = 45, Margin = new Thickness(0, 13, 0, 4) };
        AutomationProperties.SetAutomationId(graph, "widget-network-history-" + widget.Id); body.Children.Add(graph);
        var linkSpeed = Ui.Text("", 10, _muted); linkSpeed.Margin = new Thickness(0, 7, 0, 0);
        AutomationProperties.SetAutomationId(linkSpeed, "widget-network-link-" + widget.Id); body.Children.Add(linkSpeed);
        var state = Ui.Text("", 10, _muted); state.Margin = new Thickness(0, 7, 0, 0);
        AutomationProperties.SetAutomationId(state, "widget-network-state-" + widget.Id); body.Children.Add(state);

        if (!_networkHistory.TryGetValue(widget.Id, out var historyState)) _networkHistory[widget.Id] = historyState = new();
        var history = historyState.Points;
        string? selectorSignature = null, autoAdapterId = historyState.AdapterId;
        var syncingSelector = false;
        selector.SelectionChanged += (_, _) =>
        {
            if (syncingSelector || _preview || _fence.ContentLocked
                || selector.SelectedItem is not ComboBoxItem item || item.Tag is not string id || widget.NetworkAdapterId == id) return;
            widget.NetworkAdapterId = id; _app.ScheduleSave(); Update(DateTimeOffset.UtcNow);
        };
        void Update(DateTimeOffset now)
        {
            var placeholder = _preview;
            if (!placeholder && IsVisible) _app.SystemMetrics.RequestNetwork();
            var sample = placeholder ? new NetworkSnapshot(DateTimeOffset.MinValue, []) : _app.SystemMetrics.CurrentNetwork;
            var fresh = sample.At != DateTimeOffset.MinValue && now - sample.At >= TimeSpan.Zero && now - sample.At < TimeSpan.FromSeconds(8);
            var signature = string.Join("\n", sample.Interfaces.Select(a => a.Id + "\t" + a.Name)) + "\n" + widget.NetworkAdapterId;
            if (signature != selectorSignature)
            {
                selectorSignature = signature; syncingSelector = true;
                try
                {
                    selector.Items.Clear();
                    var automatic = new ComboBoxItem { Content = L.Text("Автоматически"), Tag = "", Foreground = _ink };
                    selector.Items.Add(automatic); ComboBoxItem selected = automatic;
                    foreach (var availableAdapter in sample.Interfaces)
                    {
                        var item = new ComboBoxItem { Content = availableAdapter.Name, ToolTip = availableAdapter.Name, Tag = availableAdapter.Id, Foreground = _ink };
                        selector.Items.Add(item); if (availableAdapter.Id == widget.NetworkAdapterId) selected = item;
                    }
                    if (widget.NetworkAdapterId.Length > 0 && selected == automatic)
                    {
                        selected = new ComboBoxItem { Content = L.Text("Выбранный адаптер недоступен"), Tag = widget.NetworkAdapterId, IsEnabled = false, Foreground = _muted };
                        selector.Items.Add(selected);
                    }
                    selector.SelectedItem = selected;
                }
                finally { syncingSelector = false; }
            }
            NetworkMetric? adapter;
            if (widget.NetworkAdapterId.Length > 0)
                adapter = sample.Interfaces.FirstOrDefault(a => a.Id == widget.NetworkAdapterId);
            else
            {
                var previous = sample.Interfaces.FirstOrDefault(a => a.Id == autoAdapterId);
                // Keep the chosen connection while it carries traffic; do not alternate between busy adapters.
                adapter = previous != null && (NetworkTraffic(previous) > 0 || sample.Interfaces.All(a => NetworkTraffic(a) <= 0))
                    ? previous
                    : sample.Interfaces.OrderByDescending(NetworkTraffic).ThenByDescending(a => a.SpeedBitsPerSecond).ThenBy(a => a.Name).FirstOrDefault();
                autoAdapterId = adapter?.Id;
            }
            if (historyState.AdapterId != adapter?.Id)
            {
                historyState.AdapterId = adapter?.Id; history.Clear();
            }
            var receive = fresh ? NetworkValidRate(adapter?.ReceiveBytesPerSecond) : null;
            var send = fresh ? NetworkValidRate(adapter?.SendBytesPerSecond) : null;
            receiveValue.Text = NetworkRateText(receive); sendValue.Text = NetworkRateText(send);
            FitValue(receiveValue, 29); FitValue(sendValue, 29);
            adapterName.Text = adapter?.Name ?? ""; adapterName.ToolTip = adapterName.Text;
            adapterName.Visibility = adapterName.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            linkSpeed.Text = fresh && adapter?.SpeedBitsPerSecond is > 0
                ? L.Format("Скорость подключения: {0} Мбит/с", (adapter.SpeedBitsPerSecond / 1000000d).ToString("0.#", L.Culture)) : "";
            linkSpeed.Visibility = linkSpeed.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var missingSelected = widget.NetworkAdapterId.Length > 0 && adapter == null;
            state.Text = placeholder ? L.Text("Показания появятся на рабочем столе.")
                : sample.At == DateTimeOffset.MinValue ? L.Text("Чтение сети…")
                : sample.Error != null ? L.Text("Сеть недоступна.")
                : missingSelected ? L.Text("Выбранный адаптер недоступен. Выберите другой.")
                : adapter == null ? L.Text("Нет активного сетевого подключения.")
                : !fresh ? L.Text("Обновление показаний…")
                : receive == null || send == null ? L.Text("Измеряем скорость…") : "";
            state.Visibility = state.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed; state.ToolTip = sample.Error;
            if (receive is { } rx && send is { } tx && (history.Count == 0 || history[^1].At != sample.At))
            {
                history.Add(new(sample.At, rx, tx));
                if (history.Count > 60) history.RemoveRange(0, history.Count - 60);
            }
            graph.SetSamples(history.Select(p => p.Receive), history.Select(p => p.Send));
            graph.Visibility = fresh && receive != null && send != null && history.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(receiveValue, L.Text("Приём") + " · " + receiveValue.Text + " " + L.Text("Мбит/с"));
            AutomationProperties.SetHelpText(sendValue, L.Text("Передача") + " · " + sendValue.Text + " " + L.Text("Мбит/с"));
        }
        _updates.Add(Update); Children.Add(Module(body, widget));
    }

    private StackPanel NetworkRatePanel(TextBlock value, string icon, string title, Brush accent, string id)
    {
        var panel = new StackPanel();
        var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
        var glyph = Brand.Icon(icon, accent, 13); glyph.Margin = new Thickness(0, 0, 5, 0); label.Children.Add(glyph);
        label.Children.Add(Ui.Text(title, 10, _muted)); panel.Children.Add(label);
        value.TextWrapping = TextWrapping.NoWrap; value.SizeChanged += (_, _) => FitValue(value, 29);
        AutomationProperties.SetAutomationId(value, id); panel.Children.Add(value);
        var unit = Ui.Text(L.Text("Мбит/с"), 10, _muted); unit.Margin = new Thickness(0, 2, 0, 0); panel.Children.Add(unit); return panel;
    }

    private static double NetworkTraffic(NetworkMetric adapter) => (NetworkValidRate(adapter.ReceiveBytesPerSecond) ?? 0) + (NetworkValidRate(adapter.SendBytesPerSecond) ?? 0);
    private static double? NetworkValidRate(double? rate) => rate is { } value && double.IsFinite(value) && value >= 0 ? value : null;
    private static string NetworkRateText(double? bytesPerSecond)
    {
        if (bytesPerSecond is not { } bytes) return "—";
        var megabits = bytes * 8 / 1000000d;
        return megabits.ToString(megabits == 0 ? "0" : megabits < 1 ? "0.00" : megabits < 1000 ? "0.#" : "N0", L.Culture);
    }

    private void AddBattery(DesktopWidget widget)
    {
        var body = new StackPanel(); body.Children.Add(WidgetHeading(widget, "battery", L.Text("Батарея")));
        var value = Ui.Text("— %", 40, _ink, FontWeights.Medium); value.TextWrapping = TextWrapping.NoWrap;
        value.SizeChanged += (_, _) => FitValue(value, 40);
        AutomationProperties.SetAutomationId(value, "widget-battery-charge-" + widget.Id); body.Children.Add(value);
        var state = Ui.Text("", 11, _muted); state.Margin = new Thickness(0, 3, 0, 11);
        AutomationProperties.SetAutomationId(state, "widget-battery-state-" + widget.Id); body.Children.Add(state);
        var meter = Meter("widget-battery-progress-" + widget.Id); body.Children.Add(meter);
        var remaining = Ui.Text("—", 11, _ink); remaining.Margin = new Thickness(8, 0, 0, 0); remaining.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetAutomationId(remaining, "widget-battery-remaining-" + widget.Id);
        var timeRow = Split(Ui.Text(L.Text("Осталось примерно"), 10, _muted), remaining); timeRow.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(timeRow);
        var warning = Ui.Text(L.Text("Низкий заряд аккумулятора"), 10, _muted); warning.Margin = new Thickness(0, 9, 0, 0); body.Children.Add(warning);
        _updates.Add(now =>
        {
            var placeholder = _preview;
            if (!placeholder && IsVisible) _app.SystemMetrics.RequestBattery();
            var sample = placeholder ? new BatterySnapshot(DateTimeOffset.MinValue, true, null, null, null, null) : _app.SystemMetrics.CurrentBattery;
            var fresh = sample.At != DateTimeOffset.MinValue && now - sample.At >= TimeSpan.Zero && now - sample.At < TimeSpan.FromSeconds(25) && sample.Error == null;
            var charge = fresh && sample.Present && sample.ChargePercent is { } percentage && double.IsFinite(percentage) && percentage is >= 0 and <= 100
                ? sample.ChargePercent : null;
            value.Text = charge is { } level ? level.ToString("0", L.Culture) + " %" : "— %"; FitValue(value, 40); SetMeter(meter, charge);
            meter.Visibility = charge != null ? Visibility.Visible : Visibility.Collapsed;
            state.Text = placeholder ? L.Text("Показания появятся на рабочем столе.")
                : sample.At == DateTimeOffset.MinValue ? L.Text("Чтение батареи…")
                : sample.Error != null ? L.Text("Батарея недоступна.")
                : !fresh ? L.Text("Обновление показаний…")
                : !sample.Present ? L.Text("Нет аккумулятора")
                : sample.IsCharging == true ? L.Text("Заряжается")
                : sample.OnAcPower == true ? L.Text("Питание от сети")
                : sample.OnAcPower == false ? L.Text("От батареи") : L.Text("Состояние питания неизвестно");
            state.Foreground = fresh && sample.IsCharging == true ? _accentInk : _muted; state.ToolTip = sample.Error;
            var estimate = fresh && sample.Present && sample.OnAcPower == false && sample.Remaining is { } time && time >= TimeSpan.Zero
                ? sample.Remaining : null;
            remaining.Text = estimate is { } left ? BatteryRemainingText(left) : "—";
            timeRow.Visibility = estimate != null ? Visibility.Visible : Visibility.Collapsed;
            warning.Visibility = charge is <= 20 && sample.OnAcPower == false ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(value, state.Text + " · " + value.Text);
        });
        Children.Add(Module(body, widget));
    }

    private static string BatteryRemainingText(TimeSpan remaining) => remaining.TotalMinutes < 1
        ? L.Text("Меньше минуты")
        : remaining.TotalHours >= 1 ? L.Format("{0} ч {1:00} мин", (int)remaining.TotalHours, remaining.Minutes)
        : L.Format("{0} мин", (int)remaining.TotalMinutes);

    private void AddUptime(DesktopWidget widget)
    {
        var body = new StackPanel(); body.Children.Add(WidgetHeading(widget, "uptime", L.Text("Время работы")));
        var value = Ui.Text("—", 29, _ink, FontWeights.Medium); value.TextWrapping = TextWrapping.NoWrap;
        value.SizeChanged += (_, _) => FitValue(value, 29);
        AutomationProperties.SetAutomationId(value, "widget-uptime-value-" + widget.Id); body.Children.Add(value);
        var caption = Ui.Text(L.Text("С момента загрузки Windows"), 10, _muted); caption.Margin = new Thickness(0, 4, 0, 12); body.Children.Add(caption);
        var bootTime = Ui.Text("—", 10, _ink); bootTime.Margin = new Thickness(8, 0, 0, 0); bootTime.TextWrapping = TextWrapping.NoWrap;
        bootTime.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetAutomationId(bootTime, "widget-uptime-started-" + widget.Id);
        body.Children.Add(Split(Ui.Text(L.Text("Система запущена"), 10, _muted), bootTime));
        var detail = Ui.Text("", 10, _muted); detail.Margin = new Thickness(0, 9, 0, 0); body.Children.Add(detail);
        _updates.Add(now =>
        {
            if (_preview)
            {
                value.Text = "—"; bootTime.Text = "—"; detail.Text = L.Text("Показания появятся на рабочем столе."); return;
            }
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            value.Text = L.Format("{0} дн {1:00}:{2:00}:{3:00}", uptime.Days, uptime.Hours, uptime.Minutes, uptime.Seconds); FitValue(value, 29);
            var started = (now - uptime).ToLocalTime(); bootTime.Text = started.ToString("d MMM, HH:mm", L.Culture);
            bootTime.ToolTip = started.ToString("F", L.Culture);
            detail.Text = L.Text("Включает время сна и гибернации.");
            value.ToolTip = L.Text("Быстрый запуск Windows может сохранять отсчёт после выключения.");
            AutomationProperties.SetHelpText(value, L.Text("Время работы") + " · " + value.Text);
        });
        Children.Add(Module(body, widget));
    }
}
