using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WalleFences.App;

internal sealed record ProcessMetric(int Id, string Name, double? CpuPercent, long? MemoryBytes);
internal sealed record ProcessesSnapshot(DateTimeOffset At, ProcessMetric[] Processes, bool Sampling, string? Error = null);
internal sealed record NetworkMetric(string Id, string Name, double? ReceiveBytesPerSecond, double? SendBytesPerSecond, long SpeedBitsPerSecond);
internal sealed record NetworkSnapshot(DateTimeOffset At, NetworkMetric[] Interfaces, string? Error = null);
internal sealed record BatterySnapshot(DateTimeOffset At, bool Present, double? ChargePercent, bool? IsCharging, bool? OnAcPower, TimeSpan? Remaining, string? Error = null);

// One application-wide reader. Windows calls stay on worker threads; widgets only consume snapshots.
internal sealed class SystemMetricsMonitor : IDisposable
{
    private sealed class RequestGate
    {
        internal bool Reading;
        internal long? LastRequest;
    }

    private sealed record ProcessSample(string Name, long? StartedUtcTicks, long CpuTicks, long Timestamp);
    private sealed record NetworkSample(long ReceivedBytes, long SentBytes, long Speed, long Timestamp);

    private readonly object _publicationGate = new();
    private readonly RequestGate _processGate = new(), _networkGate = new(), _batteryGate = new();
    private Dictionary<int, ProcessSample> _processSamples = new();
    private Dictionary<string, NetworkSample> _networkSamples = new(StringComparer.Ordinal);
    private bool _processSampled;
    private int _disposed;
    private ProcessesSnapshot _processes = new(DateTimeOffset.MinValue, [], true);
    private NetworkSnapshot _network = new(DateTimeOffset.MinValue, []);
    // An unread battery is unknown; it becomes absent only when Windows explicitly reports no battery.
    private BatterySnapshot _battery = new(DateTimeOffset.MinValue, true, null, null, null, null);

    internal ProcessesSnapshot CurrentProcesses => Volatile.Read(ref _processes);
    internal NetworkSnapshot CurrentNetwork => Volatile.Read(ref _network);
    internal BatterySnapshot CurrentBattery => Volatile.Read(ref _battery);

    internal void RequestProcesses() => Request(_processGate, TimeSpan.FromMilliseconds(2500), ReadProcesses);
    internal void RequestNetwork() => Request(_networkGate, TimeSpan.FromSeconds(1), ReadNetwork);
    internal void RequestBattery() => Request(_batteryGate, TimeSpan.FromSeconds(10), ReadBattery);

    private void Request(RequestGate gate, TimeSpan interval, Action read)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (gate)
        {
            if (Volatile.Read(ref _disposed) != 0 || gate.Reading) return;
            var now = Stopwatch.GetTimestamp();
            if (gate.LastRequest is { } previous && Stopwatch.GetElapsedTime(previous, now) < interval) return;
            gate.LastRequest = now;
            gate.Reading = true;
        }
        _ = Task.Run(() =>
        {
            try
            {
                if (Volatile.Read(ref _disposed) == 0) read();
            }
            finally
            {
                lock (gate) gate.Reading = false;
            }
        });
    }

    private void ReadProcesses()
    {
        Process[]? handles = null;
        try
        {
            handles = Process.GetProcesses();
            var metrics = new List<ProcessMetric>(handles.Length);
            var nextSamples = new Dictionary<int, ProcessSample>(handles.Length);
            var logicalProcessors = Math.Max(1, Environment.ProcessorCount);
            foreach (var process in handles)
            {
                if (Volatile.Read(ref _disposed) != 0) break;
                var id = TryRead(() => process.Id);
                // PID 0 measures idle time, not a process consuming CPU.
                if (id is null or 0) continue;
                string name;
                try { name = process.ProcessName; }
                catch { name = "PID " + id.Value; }
                var started = TryRead(() => process.StartTime.ToUniversalTime().Ticks);
                var cpuTicks = TryRead(() => process.TotalProcessorTime.Ticks);
                var sampledAt = Stopwatch.GetTimestamp();
                var memory = TryRead(() => process.WorkingSet64);
                if (memory < 0) memory = null;
                double? percent = null;
                if (cpuTicks is >= 0)
                {
                    if (_processSamples.TryGetValue(id.Value, out var previous)
                        && previous.Name == name && previous.StartedUtcTicks == started)
                    {
                        var elapsed = Stopwatch.GetElapsedTime(previous.Timestamp, sampledAt).TotalSeconds;
                        var delta = cpuTicks.Value - previous.CpuTicks;
                        if (elapsed > 0 && delta >= 0)
                            percent = Math.Clamp(delta / (double)TimeSpan.TicksPerSecond / elapsed / logicalProcessors * 100, 0, 100);
                    }
                    nextSamples[id.Value] = new(name, started, cpuTicks.Value, sampledAt);
                }
                // Access to RAM and CPU is independent. An exited or protected process may expose neither.
                metrics.Add(new(id.Value, name, percent, memory));
            }
            var sampling = !_processSampled;
            _processSamples = nextSamples;
            _processSampled = true;
            Publish(ref _processes, new(DateTimeOffset.UtcNow, metrics.ToArray(), sampling));
        }
        catch (Exception ex)
        {
            _processSamples = new();
            _processSampled = false;
            Publish(ref _processes, new(DateTimeOffset.UtcNow, [], true, ex.GetType().Name));
        }
        finally
        {
            if (handles != null)
                foreach (var process in handles)
                    try { process.Dispose(); } catch { /* One invalid handle must not retain the others. */ }
        }
    }

    private void ReadNetwork()
    {
        try
        {
            var metrics = new List<NetworkMetric>();
            var nextSamples = new Dictionary<string, NetworkSample>(StringComparer.Ordinal);
            string? adapterError = null;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (Volatile.Read(ref _disposed) != 0) break;
                try
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var id = adapter.Id;
                    var statistics = adapter.GetIPStatistics();
                    var sampledAt = Stopwatch.GetTimestamp();
                    var received = statistics.BytesReceived;
                    var sent = statistics.BytesSent;
                    // Speed is non-nullable in the snapshot contract; zero means Windows did not expose it.
                    var speed = Math.Max(0, TryRead(() => adapter.Speed) ?? 0);
                    double? receiveRate = null, sendRate = null;
                    if (received >= 0 && sent >= 0)
                    {
                        if (_networkSamples.TryGetValue(id, out var previous) && previous.Speed == speed
                            && received >= previous.ReceivedBytes && sent >= previous.SentBytes)
                        {
                            var elapsed = Stopwatch.GetElapsedTime(previous.Timestamp, sampledAt).TotalSeconds;
                            if (elapsed > 0)
                            {
                                receiveRate = (received - previous.ReceivedBytes) / elapsed;
                                sendRate = (sent - previous.SentBytes) / elapsed;
                            }
                        }
                        nextSamples[id] = new(received, sent, speed, sampledAt);
                    }
                    metrics.Add(new(id, adapter.Name, receiveRate, sendRate, speed));
                }
                catch (Exception ex)
                {
                    // An adapter can disappear during enumeration. Other adapters remain usable.
                    adapterError = ex.GetType().Name;
                }
            }
            // Only currently active interfaces keep baselines. A reconnect or reset needs a fresh sample.
            _networkSamples = nextSamples;
            Publish(ref _network, new(DateTimeOffset.UtcNow, metrics.ToArray(), metrics.Count == 0 ? adapterError : null));
        }
        catch (Exception ex)
        {
            _networkSamples = new(StringComparer.Ordinal);
            Publish(ref _network, new(DateTimeOffset.UtcNow, [], ex.GetType().Name));
        }
    }

    private void ReadBattery()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
            {
                Publish(ref _battery, new(DateTimeOffset.UtcNow, true, null, null, null, null, "Win32 " + Marshal.GetLastWin32Error()));
                return;
            }
            bool? acPower = status.ACLineStatus switch { 0 => false, 1 => true, _ => null };
            var flagsKnown = status.BatteryFlag != 255;
            var present = !flagsKnown || (status.BatteryFlag & 128) == 0;
            double? charge = present && status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null;
            bool? charging = present && flagsKnown ? (status.BatteryFlag & 8) != 0 : null;
            TimeSpan? remaining = present && status.BatteryLifeTime != uint.MaxValue
                ? TimeSpan.FromSeconds(status.BatteryLifeTime) : null;
            Publish(ref _battery, new(DateTimeOffset.UtcNow, present, charge, charging, acPower, remaining));
        }
        catch (Exception ex)
        {
            Publish(ref _battery, new(DateTimeOffset.UtcNow, true, null, null, null, null, ex.GetType().Name));
        }
    }

    private void Publish<T>(ref T cache, T snapshot) where T : class
    {
        // Dispose and publication share only this tiny critical section; no OS call can block shutdown.
        lock (_publicationGate)
            if (_disposed == 0) Volatile.Write(ref cache, snapshot);
    }

    private static T? TryRead<T>(Func<T> read) where T : struct
    {
        try { return read(); }
        catch { return null; }
    }

    public void Dispose()
    {
        lock (_publicationGate) Volatile.Write(ref _disposed, 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        internal byte ACLineStatus;
        internal byte BatteryFlag;
        internal byte BatteryLifePercent;
        internal byte SystemStatusFlag;
        internal uint BatteryLifeTime;
        internal uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
