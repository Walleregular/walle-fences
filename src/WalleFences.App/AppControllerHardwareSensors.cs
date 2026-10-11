using System.Threading.Tasks;
using System.Windows.Interop;

namespace WalleFences.App;

internal sealed partial class AppController
{
    private string HardwareSensorsStatus => L.Text(HardwareSensors.IsConnected && !HardwareSensors.IsBusy && HardwareSensors.Current.State == TemperatureState.Available
        ? "Доступ к датчикам включён" : HardwareSensors.StatusKey);

    private void HardwareSensorsChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (_disposed || dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            SetStatus(HardwareSensorsStatus);
        });
    }

    internal async Task EnableHardwareSensorsAsync()
    {
        if (_disposed || HardwareSensors.IsBusy) return;
        try
        {
            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive && window.IsVisible) ?? Manager;
            var operation = HardwareSensors.EnableAsync(owner == null ? 0 : new WindowInteropHelper(owner).Handle);
            SetStatus(HardwareSensorsStatus);
            await operation;
            if (_disposed) return;
            CpuTemperature.RequestRefresh();
            SetStatus(HardwareSensorsStatus);
        }
        catch (Exception ex)
        {
            if (_disposed) return;
            Log(ex); SetStatus(L.Text("Не удалось установить компонент датчиков. Попробуйте ещё раз."));
        }
    }
}
