using Microsoft.Win32;
using System.ComponentModel;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace WalleFences.App;

// The desktop UI retains its normal token. Only this explicitly enabled, narrowly scoped reader is elevated.
internal sealed class HardwareSensorSetup : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private NamedPipeServerStream? _pipe;
    private Process? _host;
    private int _busy;
    private int _connected;
    private volatile bool _disposed;
    private string _statusKey = "";
    private TemperatureSnapshot _current = new(TemperatureState.AccessRequired, DateTimeOffset.UtcNow, []);

    internal event Action? Changed;
    internal bool IsBusy => Volatile.Read(ref _busy) != 0;
    internal bool IsConnected => Volatile.Read(ref _connected) != 0;
    internal string StatusKey => Volatile.Read(ref _statusKey);
    internal TemperatureSnapshot Current => Volatile.Read(ref _current);

    internal static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return false; }
        }
    }

    // LHM 0.9.6 caches PawnIo.Version in a static constructor; installation in an existing process needs this live check.
    internal static bool DriverInstalled
    {
        get
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                    if (Version.TryParse(key?.GetValue("DisplayVersion") as string, out _)) return true;
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            }
            return false;
        }
    }

    internal async Task EnableAsync(nint ownerHandle)
    {
        if (_disposed || IsConnected || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        NotifyChanged();
        NamedPipeServerStream? pipe = null;
        Process? host = null;
        var handedOff = false;
        try
        {
            SetStatus(HardwareSensorStatus.SettingUp, TemperatureState.Reading);
            var executable = Environment.ProcessPath;
            if (!HardwareSensorIdentity.IsApplicationExecutable(executable))
                throw new HardwareSensorFailure(HardwareSensorStatus.Unavailable);

            var pipeName = "WalleFences-Sensors-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
            lock (_gate)
            {
                if (_disposed) return;
                _pipe = pipe;
            }

            // Process.Start may wait for UAC. Keep that wait off the WPF dispatcher.
            host = await Task.Run(() => Process.Start(new ProcessStartInfo
            {
                FileName = executable!,
                Arguments = $"--hardware-sensor-host --sensor-pipe {pipeName} --sensor-token {token} --sensor-parent {Environment.ProcessId}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                ErrorDialog = false,
                ErrorDialogParentHandle = ownerHandle
            }), _lifetime.Token).ConfigureAwait(false);
            if (host == null) throw new HardwareSensorFailure(HardwareSensorStatus.InstallFailed);
            lock (_gate)
            {
                if (_disposed) return;
                _host = host;
            }

            using var startup = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            startup.CancelAfter(TimeSpan.FromSeconds(30));
            await pipe.WaitForConnectionAsync(startup.Token).ConfigureAwait(false);
            // The launch result supplies the expected child PID. Do not inspect an elevated child's module from the normal UI.
            if (!HardwareSensorIdentity.PipeClientIs(pipe, host.Id))
                throw new InvalidDataException("Unexpected sensor reader.");

            await HardwareSensorWire.WriteAsync(pipe, new("hello", token, Environment.ProcessId), startup.Token).ConfigureAwait(false);
            var hello = await HardwareSensorWire.ReadAsync(pipe, startup.Token).ConfigureAwait(false);
            if (hello.Kind != "hello" || hello.ProcessId != host.Id || !HardwareSensorWire.SameToken(token, hello.Token))
                throw new InvalidDataException("Sensor reader handshake failed.");

            Volatile.Write(ref _connected, 1);
            NotifyChanged();
            var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = ReceiveAsync(pipe, host, first);
            handedOff = true;
            // Installation is bounded at the UI boundary. Disconnecting never terminates an installer in progress.
            using var ready = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            ready.CancelAfter(TimeSpan.FromMinutes(3));
            await first.Task.WaitAsync(ready.Token).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            SetStatus(HardwareSensorStatus.Cancelled, TemperatureState.AccessRequired);
        }
        catch (HardwareSensorFailure ex) { SetStatus(ex.StatusKey, TemperatureState.AccessRequired); }
        catch (OperationCanceledException)
        {
            if (!_disposed) SetStatus(HardwareSensorStatus.InstallFailed, TemperatureState.AccessRequired);
            Disconnect(pipe);
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException or System.Text.Json.JsonException)
        {
            if (!_disposed) SetStatus(HardwareSensorStatus.InstallFailed, TemperatureState.AccessRequired);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            NotifyChanged();
            if (!handedOff)
            {
                Disconnect(pipe);
                host?.Dispose();
                lock (_gate) { if (ReferenceEquals(_host, host)) _host = null; }
            }
        }
    }

    private async Task ReceiveAsync(NamedPipeServerStream pipe, Process host, TaskCompletionSource<bool> first)
    {
        var terminal = false;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var message = await HardwareSensorWire.ReadAsync(pipe, _lifetime.Token).ConfigureAwait(false);
                if (message.Kind == "status" && HardwareSensorStatus.IsAllowed(message.StatusKey))
                {
                    var key = message.StatusKey!;
                    SetStatus(key, key == HardwareSensorStatus.SettingUp || key == HardwareSensorStatus.Ready
                        ? TemperatureState.Reading : TemperatureState.AccessRequired);
                    if (key != HardwareSensorStatus.SettingUp && key != HardwareSensorStatus.Ready)
                    {
                        terminal = true;
                        first.TrySetResult(false);
                        return;
                    }
                }
                else if (message.Kind == "sample" && HardwareSensorWire.IsValidSnapshot(message.Snapshot))
                {
                    if (message.Snapshot!.State is TemperatureState.DriverMissing or TemperatureState.AccessRequired)
                    {
                        SetStatus(HardwareSensorStatus.DriverUnavailable, TemperatureState.AccessRequired);
                        terminal = true;
                        first.TrySetResult(false);
                        return; // A removed driver or lost access must allow a fresh explicit enable operation.
                    }
                    Volatile.Write(ref _current, message.Snapshot!);
                    var key = message.Snapshot!.State == TemperatureState.Available ? HardwareSensorStatus.Ready : HardwareSensorStatus.Unavailable;
                    var previous = Interlocked.Exchange(ref _statusKey, key);
                    if (!string.Equals(previous, key, StringComparison.Ordinal)) NotifyChanged();
                    first.TrySetResult(true);
                }
                else throw new InvalidDataException("Unexpected sensor message.");
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            if (!_disposed && !terminal) SetStatus(HardwareSensorStatus.Unavailable, TemperatureState.AccessRequired);
        }
        finally
        {
            first.TrySetResult(false);
            Disconnect(pipe);
            host.Dispose();
            lock (_gate) { if (ReferenceEquals(_host, host)) _host = null; }
        }
    }

    private void SetStatus(string key, TemperatureState state)
    {
        if (_disposed) return;
        var previous = Interlocked.Exchange(ref _statusKey, key);
        Volatile.Write(ref _current, new TemperatureSnapshot(state, DateTimeOffset.UtcNow, [], key));
        if (!string.Equals(previous, key, StringComparison.Ordinal)) NotifyChanged();
    }

    private void Disconnect(NamedPipeServerStream? pipe)
    {
        var changed = false;
        lock (_gate)
        {
            if (ReferenceEquals(_pipe, pipe))
            {
                _pipe = null;
                Volatile.Write(ref _connected, 0);
                var key = StatusKey;
                if (key is HardwareSensorStatus.SettingUp or HardwareSensorStatus.Ready) key = HardwareSensorStatus.Unavailable;
                Volatile.Write(ref _statusKey, key);
                Volatile.Write(ref _current, new TemperatureSnapshot(TemperatureState.AccessRequired, DateTimeOffset.UtcNow, [], key));
                changed = true;
            }
        }
        try { pipe?.Dispose(); } catch (IOException) { }
        if (changed) NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (_disposed) return;
        // Subscribers marshal state changes to their own dispatcher. No WPF dependency or worker-thread UI mutation here.
        try { Changed?.Invoke(); } catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        NamedPipeServerStream? pipe;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Changed = null;
            pipe = _pipe;
            _pipe = null;
        }
        _lifetime.Cancel();
        Volatile.Write(ref _connected, 0);
        Volatile.Write(ref _current, new TemperatureSnapshot(TemperatureState.AccessRequired, DateTimeOffset.UtcNow, []));
        try { pipe?.Dispose(); } catch (IOException) { }
        // EOF tells the elevated host to release its reader. Never kill its process while it could be installing the driver.
    }
}

internal static class HardwareSensorStatus
{
    internal const string SettingUp = "Настройка датчиков…";
    internal const string Ready = "Датчики готовы. Обновляем показания…";
    internal const string Cancelled = "Установка датчиков отменена.";
    internal const string RebootRequired = "Windows нужно перезагрузить, чтобы включить датчики.";
    internal const string InstallFailed = "Не удалось установить компонент датчиков. Попробуйте ещё раз.";
    internal const string Missing = "В комплекте программы отсутствует компонент датчиков.";
    internal const string Damaged = "Компонент датчиков повреждён. Используйте полный комплект программы.";
    internal const string Unavailable = "Аппаратные датчики недоступны на этой системе.";
    internal const string DriverUnavailable = "Компонент датчиков установлен, но пока недоступен. Перезапустите Windows и попробуйте снова.";
    internal static bool IsAllowed(string? key) => key is SettingUp or Ready or Cancelled or RebootRequired or InstallFailed or Missing or Damaged or Unavailable or DriverUnavailable;
}

internal sealed class HardwareSensorFailure : Exception
{
    internal string StatusKey { get; }
    internal HardwareSensorFailure(string key) : base(key) => StatusKey = key;
}
