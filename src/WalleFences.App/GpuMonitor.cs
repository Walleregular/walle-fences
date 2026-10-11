using LibreHardwareMonitor.Hardware;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WalleFences.App;

internal sealed record GpuAdapterSnapshot(string Id, string Name, double? LoadPercent, double? TemperatureC, long? UsedMemoryBytes, long? TotalMemoryBytes);
internal sealed record GpuSnapshot(DateTimeOffset At, GpuAdapterSnapshot[] Adapters, string? Detail = null);

// GPU APIs are read independently of the CPU/PawnIO reader. UI timers only request cached snapshots.
internal sealed class GpuMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly object _hardwareGate = new();
    private Computer? _computer;
    private GpuSnapshot _current = new(DateTimeOffset.MinValue, []);
    private long _lastRequest = long.MinValue;
    private long _openedAt;
    private bool _reading;
    private volatile bool _disposed;

    internal GpuSnapshot Current => Volatile.Read(ref _current);

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
                    if (!_disposed) Volatile.Write(ref _current, Read());
                }
                finally { lock (_gate) _reading = false; }
            }
        });
    }

    private GpuSnapshot Read()
    {
        var adapters = new List<GpuAdapterSnapshot>();
        var failures = new List<string>();
        try
        {
            if (_computer == null)
            {
                _computer = new Computer { IsGpuEnabled = true };
                _computer.Open(); _openedAt = Environment.TickCount64;
            }
            foreach (var hardware in _computer.Hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
            {
                try
                {
                    var sensors = new List<ISensor>();
                    ReadHardware(hardware, sensors);
                    var load = ReadValue(sensors, SensorType.Load, "GPU Core", "GPU Utilization", "GPU Total");
                    // D3D engines describe independent workloads. Summing them can exceed 100%.
                    load ??= sensors.Where(s => s.SensorType == SensorType.Load &&
                        (s.Name.Equals("D3D 3D", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("D3D 3D ", StringComparison.OrdinalIgnoreCase)))
                        .Select(Value).Where(v => v is >= 0 and <= 100).Max();
                    if (load is < 0 or > 100) load = null;
                    var temperature = ReadValue(sensors, SensorType.Temperature, "GPU Core", "GPU");
                    if (temperature is <= 0 or > 150) temperature = null;
                    var used = ReadMemory(sensors, "GPU Memory Used", "GPU VRAM Used", "D3D Dedicated Memory Used");
                    var total = ReadMemory(sensors, "GPU Memory Total", "GPU VRAM Total", "D3D Dedicated Memory Total");
                    if (total is <= 0) total = null;
                    adapters.Add(new(hardware.Identifier.ToString(), CleanName(hardware.Name), load, temperature, used, total));
                }
                catch (Exception ex)
                {
                    // A sleeping or unsupported adapter must not remove readings from other GPUs.
                    adapters.Add(new(hardware.Identifier.ToString(), CleanName(hardware.Name), null, null, null, null));
                    failures.Add(hardware.Name + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            if (adapters.Count == 0 && Environment.TickCount64 - _openedAt > 30000) CloseComputer();
        }
        catch (Exception ex)
        {
            CloseComputer();
            failures.Add(ex.GetType().Name + ": " + ex.Message);
        }
        // LHM's Intel enumeration depends on its CPU reader. Windows names remain available
        // without that reader or a sensor driver; none of these fallback entries invent readings.
        try { AddWindowsAdapters(adapters); }
        catch (Exception ex) { failures.Add(ex.GetType().Name + ": " + ex.Message); }
        return new(DateTimeOffset.UtcNow, adapters.OrderByDescending(a => a.TotalMemoryBytes ?? -1).ThenBy(a => a.Id, StringComparer.Ordinal).ToArray(),
            failures.Count > 0 ? string.Join("\n", failures) : null);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        internal uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceString;
        internal uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceKey;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint number, ref DisplayDevice display, uint flags);

    private static void AddWindowsAdapters(List<GpuAdapterSnapshot> adapters)
    {
        if (!OperatingSystem.IsWindows()) return;
        var seenDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const uint mirroringDriver = 0x00000008, remoteDisplay = 0x04000000;
        for (uint index = 0; index < 128; index++)
        {
            var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>(), DeviceName = "", DeviceString = "", DeviceId = "", DeviceKey = "" };
            // A null first argument asks for adapters, rather than their attached monitors.
            if (!EnumDisplayDevices(null, index, ref device, 0)) break;
            if ((device.StateFlags & (mirroringDriver | remoteDisplay)) != 0) continue;
            var deviceId = device.DeviceId.Trim(); var name = CleanName(device.DeviceString);
            if (!deviceId.StartsWith("PCI\\VEN_", StringComparison.OrdinalIgnoreCase) || name.Length == 0 || !seenDevices.Add(deviceId)) continue;
            if (name.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase) || name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) || name.Contains("Mirroring", StringComparison.OrdinalIgnoreCase)) continue;
            // Vendor APIs and Windows can spell the same model with minor branding differences.
            if (adapters.Any(adapter => adapter.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || name.Contains(adapter.Name, StringComparison.OrdinalIgnoreCase) && adapter.Name.Length >= 12
                || adapter.Name.Contains(name, StringComparison.OrdinalIgnoreCase) && name.Length >= 12)) continue;
            adapters.Add(new("windows-gpu/" + deviceId.ToUpperInvariant(), name, null, null, null, null));
        }
    }

    private static void ReadHardware(IHardware hardware, List<ISensor> sensors)
    {
        hardware.Update(); sensors.AddRange(hardware.Sensors);
        foreach (var child in hardware.SubHardware) ReadHardware(child, sensors);
    }

    private static double? Value(ISensor sensor) => sensor.Value is { } value && float.IsFinite(value) ? value : null;

    private static double? ReadValue(IEnumerable<ISensor> sensors, SensorType type, params string[] names)
    {
        foreach (var name in names)
        {
            var value = sensors.Where(s => s.SensorType == type && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(Value).FirstOrDefault(v => v != null);
            if (value != null) return value;
        }
        return null;
    }

    private static long? ReadMemory(IEnumerable<ISensor> sensors, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var sensor in sensors.Where(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                var scale = sensor.SensorType switch { SensorType.SmallData => 1048576d, SensorType.Data => 1073741824d, _ => 0d };
                if (scale == 0 || Value(sensor) is not { } value || value < 0) continue;
                var bytes = value * scale;
                if (double.IsFinite(bytes) && bytes < long.MaxValue) return (long)Math.Round(bytes);
            }
        }
        return null;
    }

    internal static string CleanName(string name) => string.Join(" ", name.Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
        .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase).Replace("®", "").Replace("™", "")
        .Split([' ', '\t', '\r', '\n', '\0'], StringSplitOptions.RemoveEmptyEntries));

    private void CloseComputer()
    {
        try { _computer?.Close(); } catch { /* A failed vendor API must still release the reader reference. */ }
        _computer = null;
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        // Some vendor API calls block. Closing is serialized with reads away from the WPF thread.
        _ = Task.Run(() => { lock (_hardwareGate) CloseComputer(); });
    }
}
