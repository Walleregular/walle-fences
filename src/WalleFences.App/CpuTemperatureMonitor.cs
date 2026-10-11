using LibreHardwareMonitor.Hardware;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace WalleFences.App;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum TemperatureState { Reading, Available, DriverMissing, Unavailable, Error, AccessRequired }
internal sealed record CpuReading(string Name, TemperatureSensor Primary, TemperatureSensor[] Sensors);
internal sealed record TemperatureSnapshot(TemperatureState State, DateTimeOffset At, CpuReading[] Cpus, string? Detail = null);

// One serialized hardware reader per application; WPF only reads immutable cached snapshots.
internal sealed class CpuTemperatureMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly object _hardwareGate = new();
    private readonly HardwareSensorSetup? _access;
    private Computer? _computer;
    private TemperatureSnapshot _current = new(TemperatureState.Reading, DateTimeOffset.UtcNow, []);
    private long _lastRequest = long.MinValue;
    private bool _reading;
    private int _restartRequested;
    private volatile bool _disposed;
    internal CpuTemperatureMonitor(HardwareSensorSetup? access = null) => _access = access;
    internal TemperatureSnapshot Current => Volatile.Read(ref _current);
    internal void Request()
    {
        lock (_gate)
        {
            if (_disposed || _reading || _lastRequest != long.MinValue && Environment.TickCount64 - _lastRequest < 3000) return;
            _lastRequest = Environment.TickCount64; _reading = true;
        }
        _ = Task.Run(() =>
        {
            lock (_hardwareGate)
            {
                try
                {
                    if (!_disposed)
                    {
                        if (Interlocked.Exchange(ref _restartRequested, 0) != 0)
                        {
                            try { _computer?.Close(); } catch { }
                            _computer = null;
                        }
                        Volatile.Write(ref _current, Read());
                    }
                }
                finally { lock (_gate) _reading = false; }
            }
        });
    }
    internal void RequestRefresh()
    {
        if (_disposed) return;
        Interlocked.Exchange(ref _restartRequested, 1);
        Volatile.Write(ref _current, new(TemperatureState.Reading, DateTimeOffset.UtcNow, []));
        lock (_gate) _lastRequest = long.MinValue;
        Request();
    }
    private TemperatureSnapshot Read()
    {
        try
        {
            // The library caches installation state once. Read the live state after local setup.
            if (!HardwareSensorSetup.DriverInstalled) return new(TemperatureState.DriverMissing, DateTimeOffset.UtcNow, []);
            if (!HardwareSensorSetup.IsElevated)
                return _access is { IsConnected: true } access ? access.Current : new(TemperatureState.AccessRequired, DateTimeOffset.UtcNow, []);
            if (_computer == null) { _computer = new Computer { IsCpuEnabled = true }; _computer.Open(); }
            var cpus = new List<CpuReading>();
            foreach (var hardware in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
            {
                var sensors = new List<TemperatureSensor>();
                ReadHardware(hardware, sensors);
                if (CpuTemperatures.Primary(sensors) is { } primary) cpus.Add(new(hardware.Name, primary, sensors.ToArray()));
            }
            return new(cpus.Count > 0 ? TemperatureState.Available : TemperatureState.Unavailable, DateTimeOffset.UtcNow, cpus.ToArray());
        }
        catch (Exception ex)
        {
            try { _computer?.Close(); } catch { /* Release partially opened hardware on a failed read. */ }
            _computer = null; return new(TemperatureState.Error, DateTimeOffset.UtcNow, [], ex.GetType().Name + ": " + ex.Message);
        }
    }
    private static void ReadHardware(IHardware hardware, List<TemperatureSensor> sensors)
    {
        hardware.Update();
        sensors.AddRange(hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0 and <= 150)
            .Select(s => new TemperatureSensor(s.Name, s.Value!.Value)).Where(s => float.IsFinite(s.Value)));
        foreach (var child in hardware.SubHardware) ReadHardware(child, sensors);
    }
    internal static TemperatureSnapshot Probe()
    {
        var monitor = new CpuTemperatureMonitor();
        try { return monitor.Read(); } finally { try { monitor._computer?.Close(); } catch { } }
    }
    public void Dispose()
    {
        // Close on the worker queue to avoid blocking the UI on a hardware call during shutdown.
        _disposed = true;
        _ = Task.Run(() => { lock (_hardwareGate) { try { _computer?.Close(); } catch { } _computer = null; } });
    }
}
