using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WalleFences.App;

internal static class HardwareSensorHost
{
    private const string InstallerSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

    internal static int Run(string[] args)
    {
        // This branch deliberately executes before profile creation, application resources and the normal single-instance mutex.
        if (!TryArguments(args, out var pipeName, out var token, out var parentId) || !HardwareSensorSetup.IsElevated) return 2;
        try { return RunAsync(pipeName, token, parentId).GetAwaiter().GetResult(); }
        catch { return 1; } // No elevated UI, file logging, user-specified paths or recovery commands.
    }

    private static bool TryArguments(string[] args, out string pipe, out string token, out int parent)
    {
        pipe = ""; token = ""; parent = 0;
        if (args.Length != 7 || args[0] != "--hardware-sensor-host" || args[1] != "--sensor-pipe"
            || args[3] != "--sensor-token" || args[5] != "--sensor-parent") return false;
        pipe = args[2]; token = args[4];
        const string prefix = "WalleFences-Sensors-";
        return pipe.Length == prefix.Length + 48 && pipe.StartsWith(prefix, StringComparison.Ordinal)
            && pipe[prefix.Length..].All(Uri.IsHexDigit) && token.Length == 64 && token.All(Uri.IsHexDigit)
            && int.TryParse(args[6], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out parent)
            && parent > 0 && parent != Environment.ProcessId;
    }

    private static async Task<int> RunAsync(string pipeName, string token, int parentId)
    {
        if (!HardwareSensorIdentity.IsSameApplicationProcess(parentId)) return 2;
        using var parent = Process.GetProcessById(parentId);
        using var stop = new CancellationTokenSource();
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using (var handshake = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            await pipe.ConnectAsync(handshake.Token).ConfigureAwait(false);
            if (!HardwareSensorIdentity.PipeServerIs(pipe, parentId) || !HardwareSensorIdentity.IsSameApplicationProcess(parentId)) return 2;
            var hello = await HardwareSensorWire.ReadAsync(pipe, handshake.Token).ConfigureAwait(false);
            if (hello.Kind != "hello" || hello.ProcessId != parentId || !HardwareSensorWire.SameToken(token, hello.Token)) return 2;
            await HardwareSensorWire.WriteAsync(pipe, new("hello", token, Environment.ProcessId), handshake.Token).ConfigureAwait(false);
        }

        // The client sends no commands after authentication. EOF, unexpected input or parent exit ends the reader.
        var disconnected = WatchDisconnectAsync(pipe, stop);
        var parentExited = WatchParentAsync(parent, stop);
        CpuTemperatureMonitor? monitor = null;
        try
        {
            await SendStatus(pipe, HardwareSensorStatus.SettingUp, stop.Token).ConfigureAwait(false);
            if (!HardwareSensorSetup.DriverInstalled)
            {
                var status = await InstallLocalDriverAsync(stop.Token).ConfigureAwait(false);
                // Complete an already started installer even if the UI has closed; do not start any reader afterward.
                if (stop.IsCancellationRequested) return 0;
                if (status != null) { await SendStatus(pipe, status, stop.Token).ConfigureAwait(false); return 1; }
            }
            if (!HardwareSensorSetup.DriverInstalled)
            {
                await SendStatus(pipe, HardwareSensorStatus.DriverUnavailable, stop.Token).ConfigureAwait(false);
                return 1;
            }

            await SendStatus(pipe, HardwareSensorStatus.Ready, stop.Token).ConfigureAwait(false);
            monitor = new CpuTemperatureMonitor();
            var lastSample = DateTimeOffset.MinValue;
            while (!stop.IsCancellationRequested)
            {
                monitor.Request();
                var sample = monitor.Current;
                if (sample.State != TemperatureState.Reading && sample.At != lastSample)
                {
                    lastSample = sample.At;
                    await HardwareSensorWire.WriteAsync(pipe, new("sample", Snapshot: HardwareSensorWire.BoundedSnapshot(sample)), stop.Token).ConfigureAwait(false);
                }
                await Task.Delay(500, stop.Token).ConfigureAwait(false);
            }
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception ex) when (ex is IOException or JsonException) { return 0; }
        catch
        {
            try { if (!stop.IsCancellationRequested) await SendStatus(pipe, HardwareSensorStatus.Unavailable, stop.Token).ConfigureAwait(false); } catch { }
            return 1;
        }
        finally
        {
            monitor?.Dispose();
            stop.Cancel();
            pipe.Dispose();
            try { await Task.WhenAll(disconnected, parentExited).ConfigureAwait(false); } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
            // Closing this dedicated process also releases all OS hardware handles, including an in-flight reader call.
        }
    }

    private static async Task<string?> InstallLocalDriverAsync(CancellationToken parentLifetime)
    {
        parentLifetime.ThrowIfCancellationRequested();
        // The supplied official installer is AMD64. ARM64 needs the Windows 11 x64-emulation layer.
        var architecture = RuntimeInformation.OSArchitecture;
        if (architecture != Architecture.X64 && architecture != Architecture.Arm64
            || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
            || architecture == Architecture.Arm64 && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return HardwareSensorStatus.Unavailable;
        var installerPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Hardware", "PawnIO", "PawnIO_setup.exe");
        if (!File.Exists(installerPath)) return HardwareSensorStatus.Missing;
        try
        {
            // Hold a read-only handle through execution so the verified file cannot be replaced between hashing and launch.
            using var installerFile = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(installerFile, parentLifetime).ConfigureAwait(false));
            if (!string.Equals(hash, InstallerSha256, StringComparison.Ordinal)) return HardwareSensorStatus.Damaged;
            parentLifetime.ThrowIfCancellationRequested();
            using var installer = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "-install -silent",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(installerPath)!
            });
            if (installer == null) return HardwareSensorStatus.InstallFailed;
            // Never abort a system driver installation midway, even when the UI disconnects or its startup timer expires.
            await installer.WaitForExitAsync().ConfigureAwait(false);
            return installer.ExitCode switch { 0 => null, 3010 => HardwareSensorStatus.RebootRequired, _ => HardwareSensorStatus.InstallFailed };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return HardwareSensorStatus.InstallFailed; }
    }

    private static Task SendStatus(Stream pipe, string key, CancellationToken cancellation) => HardwareSensorWire.WriteAsync(pipe, new("status", StatusKey: key), cancellation);

    private static async Task WatchDisconnectAsync(Stream pipe, CancellationTokenSource stop)
    {
        try { await pipe.ReadAsync(new byte[1], stop.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { stop.Cancel(); }
    }

    private static async Task WatchParentAsync(Process parent, CancellationTokenSource stop)
    {
        try { await parent.WaitForExitAsync(stop.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception) { }
        finally { stop.Cancel(); }
    }
}

internal sealed record HardwareSensorMessage(string Kind, string? Token = null, int ProcessId = 0, string? StatusKey = null, TemperatureSnapshot? Snapshot = null);

internal static class HardwareSensorWire
{
    private const int MaximumBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 16 };

    internal static bool SameToken(string expected, string? actual)
    {
        if (actual == null || expected.Length != 64 || actual.Length != 64) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));
    }

    internal static async Task WriteAsync(Stream stream, HardwareSensorMessage message, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Sensor message too large.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }

    internal static async Task<HardwareSensorMessage> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumBytes) throw new InvalidDataException("Invalid sensor message length.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
        return JsonSerializer.Deserialize<HardwareSensorMessage>(bytes, Options) ?? throw new InvalidDataException("Empty sensor message.");
    }

    internal static TemperatureSnapshot BoundedSnapshot(TemperatureSnapshot sample)
    {
        static string Name(string? value) => string.IsNullOrEmpty(value) ? "CPU" : value.Length <= 128 ? value : value[..128];
        static TemperatureSensor Sensor(TemperatureSensor value) => new(Name(value.Name), value.Value);
        var cpus = sample.Cpus.Take(8).Select(cpu => new CpuReading(Name(cpu.Name), Sensor(cpu.Primary), cpu.Sensors.Take(128).Select(Sensor).ToArray())).ToArray();
        return sample with { Cpus = cpus, Detail = sample.Detail is { Length: > 512 } detail ? detail[..512] : sample.Detail };
    }

    internal static bool IsValidSnapshot(TemperatureSnapshot? sample)
    {
        static bool ValidSensor(TemperatureSensor? sensor) => sensor != null && sensor.Name is { Length: <= 128 }
            && float.IsFinite(sensor.Value) && sensor.Value is > 0 and <= 150;
        if (sample == null || !Enum.IsDefined(sample.State) || sample.Cpus == null || sample.Cpus.Length > 8
            || sample.Detail is { Length: > 512 } || sample.At > DateTimeOffset.UtcNow.AddMinutes(1)) return false;
        return sample.Cpus.All(cpu => cpu != null && cpu.Name is { Length: <= 128 } && ValidSensor(cpu.Primary)
            && cpu.Sensors != null && cpu.Sensors.Length <= 128 && cpu.Sensors.All(ValidSensor));
    }
}

internal static class HardwareSensorIdentity
{
    internal static bool IsApplicationExecutable(string? path) => !string.IsNullOrEmpty(path)
        && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path.GetFileNameWithoutExtension(path), typeof(Program).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase);

    internal static bool IsSameApplicationProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var current = Process.GetCurrentProcess();
            if (process.SessionId != current.SessionId) return false;
            var handle = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION works across the same user's UAC boundary.
            if (handle == 0) return false;
            try
            {
                var path = new StringBuilder(32768); var length = path.Capacity;
                return QueryFullProcessImageName(handle, 0, path, ref length)
                    && string.Equals(Path.GetFullPath(path.ToString()), Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseHandle(handle); }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { return false; }
    }

    internal static bool PipeClientIs(NamedPipeServerStream pipe, int expected) => GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var actual) && actual == (uint)expected;
    internal static bool PipeServerIs(NamedPipeClientStream pipe, int expected) => GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var actual) && actual == (uint)expected;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref int length);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
