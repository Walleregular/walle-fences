using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private Action? _refreshHardwareSensors;

    private UIElement HardwareSensorSettings()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text(L.Text("Температуры и аппаратные датчики"), 17, Ui.Ink, FontWeights.SemiBold));
        var state = Ui.Text("", 12, Ui.Muted); state.Margin = new Thickness(0, 9, 0, 9);
        AutomationProperties.SetAutomationId(state, "settings-hardware-sensors-state"); panel.Children.Add(state);
        var enable = LabelAction("Включить аппаратные датчики", "temperature", () => _ = _app.EnableHardwareSensorsAsync(), id: "settings-hardware-sensors-enable");
        enable.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(enable);
        var hint = Ui.Text(L.Text("Подтвердите доступ к датчикам в окне Windows. Интерфейс программы продолжит работать с обычными правами."), 11, Ui.Muted);
        hint.Margin = new Thickness(0, 9, 0, 0); panel.Children.Add(hint);
        _refreshHardwareSensors = () =>
        {
            var sensors = _app.HardwareSensors;
            var connected = sensors.IsConnected && !sensors.IsBusy;
            var ready = connected && sensors.Current.State == TemperatureState.Available;
            state.Text = ready ? L.Text("Доступ к датчикам включён") : sensors.StatusKey.Length > 0 ? L.Text(sensors.StatusKey)
                : L.Text(connected ? "Доступ к датчикам включён" : "Доступ к датчикам не включён");
            state.Foreground = ready ? Ui.Accent : Ui.Muted;
            enable.IsEnabled = !sensors.IsBusy && !connected;
        };
        _refreshHardwareSensors();
        return Ui.Card(panel);
    }
}
