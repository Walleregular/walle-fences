using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Automation;

namespace WalleFences.App;

internal static class Native
{
    internal const int GwlStyle = -16, GwlExStyle = -20;
    internal const long WsChild = 0x40000000L, WsPopup = 0x80000000L;
    internal static readonly nint HwndTop = 0, HwndBottom = 1, HwndTopmost = -1, HwndNotTopmost = -2;
    internal const uint SwpNoSize = 1, SwpNoMove = 2, SwpNoZOrder = 4, SwpNoActivate = 0x10, SwpFrameChanged = 0x20;
    internal delegate bool EnumWindowProc(nint hwnd, nint param);
    internal delegate nint HookProc(int code, nint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseData { public Point Point; public uint Mouse, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindow(string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowProc callback, nint param);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    internal static nint ChildParent(nint hwnd) => ((long)GetWindowLong(hwnd, GwlStyle) & WsChild) != 0 ? GetParent(hwnd) : 0;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLong(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int show);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    internal static bool ControlPressed() => (GetAsyncKeyState(0x11) & 0x8000) != 0;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice display, uint flags);
    internal static (string Id, string Name) DisplayIdentity(string device)
    {
        var display = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
        return EnumDisplayDevices(device, 0, ref display, 1) && !string.IsNullOrWhiteSpace(display.Id)
            ? (display.Id, string.IsNullOrWhiteSpace(display.Description) ? device : display.Description) : (device, device);
    }
    internal static double MonitorScale(System.Windows.Rect bounds)
    {
        var scale = AppController.GlobalScale;
        var center = new Point { X = (int)((bounds.X + bounds.Width / 2) / scale), Y = (int)((bounds.Y + bounds.Height / 2) / scale) };
        return GetDpiForMonitor(MonitorFromPoint(center, 2), 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, System.Text.StringBuilder buffer, int count);
    internal static bool TryGetForeignFullscreenBounds(nint foreground, out Rect bounds)
    {
        bounds = default;
        if (foreground == 0 || !IsWindowVisible(foreground) || IsIconic(foreground)) return false;
        GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId) return false;
        var name = new System.Text.StringBuilder(128); GetClassName(foreground, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        // DWM's visible frame omits the invisible resize border of maximized windows, which would
        // otherwise make an ordinary maximized application look like a fullscreen application.
        if (DwmGetWindowAttribute(foreground, 9, out var frame, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(foreground, out frame)) return false;
        if (frame.Right <= frame.Left || frame.Bottom <= frame.Top) return false;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var monitor = screen.Bounds;
            if (frame.Left <= monitor.Left + 2 && frame.Top <= monitor.Top + 2 && frame.Right >= monitor.Right - 2 && frame.Bottom >= monitor.Bottom - 2)
            { bounds = frame; return true; }
        }
        return false;
    }
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] internal static extern uint GetDoubleClickTime();
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc procedure, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wparam, nint lparam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ShellFileInfo
    {
        public nint Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource> Icons = new(StringComparer.OrdinalIgnoreCase);
    internal static ImageSource? Icon(string path)
    {
        var key = Directory.Exists(path) ? "::folder" : PathExtensions.IsShortcut(path) || Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? path : Path.GetExtension(path);
        if (Icons.TryGetValue(key, out var cached)) return cached;
        try
        {
            var flags = 0x100u | 0x0u; // SHGFI_ICON | SHGFI_LARGEICON
            if (!File.Exists(path) && !Directory.Exists(path)) flags |= 0x10;
            SHGetFileInfo(path, 0x80, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), flags);
            if (info.Icon == 0) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                if (Icons.Count > 1500) Icons.Clear();
                Icons[key] = source; return source;
            }
            finally { DestroyIcon(info.Icon); }
        }
        catch { return null; }
    }

    internal static nint DesktopHost()
    {
        nint host = 0;
        EnumWindows((window, _) => { if (FindWindowEx(window, 0, "SHELLDLL_DefView", null) != 0) { host = window; return false; } return true; }, 0);
        return host;
    }
    internal static nint DesktopIcons()
    {
        var host = DesktopHost();
        return host == 0 ? 0 : FindWindowEx(FindWindowEx(host, 0, "SHELLDLL_DefView", null), 0, "SysListView32", null);
    }
    internal static bool IsDesktopPoint(Point point)
    {
        var window = WindowFromPoint(point);
        var buffer = new System.Text.StringBuilder(128); GetClassName(window, buffer, buffer.Capacity);
        if (buffer.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView") return true;
        // Only empty list-view space; item clicks must keep normal Explorer behavior.
        if (buffer.ToString() != "SysListView32") return false;
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            return element.Current.ControlType is var type && type != ControlType.ListItem && type != ControlType.Image && type != ControlType.Text;
        }
        catch { return false; }
    }

    internal static void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var host = DesktopHost();
        var style = (long)GetWindowLong(hwnd, GwlStyle);
        if (host != 0 && ChildParent(hwnd) == host && ((long)GetWindowLong(hwnd, GwlExStyle) & 8) == 0) { if (window is FenceWindow stableFence) stableFence.ApplyMonitorBounds(); return; }
        if (host != 0)
        {
            SetWindowLong(hwnd, GwlStyle, (nint)((style & ~WsPopup) | WsChild));
            SetParent(hwnd, host);
            if (ChildParent(hwnd) != host)
            {
                SetParent(hwnd, 0); SetWindowLong(hwnd, GwlStyle, (nint)((style & ~WsChild) | WsPopup));
                SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
                if (window is FenceWindow failedFence) failedFence.ApplyMonitorBounds();
                return;
            }
            var position = window is FenceWindow fence ? new System.Windows.Point(fence.Fence.X, fence.Fence.Y) : new System.Windows.Point(window.Left, window.Top);
            var scale = AppController.GlobalScale;
            var point = new Point { X = (int)Math.Round(position.X / scale), Y = (int)Math.Round(position.Y / scale) };
            ScreenToClient(host, ref point);
            SetWindowPos(hwnd, HwndTop, point.X, point.Y, 0, 0, SwpNoSize | SwpNoActivate | SwpFrameChanged);
        }
        else
        {
            SetParent(hwnd, 0); SetWindowLong(hwnd, GwlStyle, (nint)((style & ~WsChild) | WsPopup));
            SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
        }
        if (window is FenceWindow attachedFence) attachedFence.ApplyMonitorBounds();
    }
    internal static void Detach(Window window, bool topmost)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        GetWindowRect(hwnd, out var bounds);
        var child = ChildParent(hwnd) != 0;
        var alreadyTop = ((long)GetWindowLong(hwnd, GwlExStyle) & 8) != 0;
        if (child) { SetParent(hwnd, 0); SetWindowLong(hwnd, GwlStyle, (nint)(((long)GetWindowLong(hwnd, GwlStyle) & ~WsChild) | WsPopup)); }
        if (child || alreadyTop != topmost) SetWindowPos(hwnd, topmost ? HwndTopmost : HwndNotTopmost, bounds.Left, bounds.Top, 0, 0, SwpNoSize | SwpNoActivate | (child ? SwpFrameChanged : 0));
        if (window is FenceWindow detachedFence) detachedFence.ApplyMonitorBounds();
    }

    internal static ProcessStartInfo LaunchInfo(string path, ReferenceLaunch? launch = null) => new(path)
    {
        UseShellExecute = true,
        Arguments = launch?.Arguments ?? "",
        WorkingDirectory = launch?.WorkingDirectory ?? "",
        Verb = launch?.RunAsAdministrator == true ? "runas" : ""
    };
    internal static bool IsWebAddress(string path) => Uri.TryCreate(path, UriKind.Absolute, out var address) && address.Scheme is "http" or "https";
    internal static event Action<string, ReferenceLaunch?>? OpenSucceeded;
    internal static void Open(string path, ReferenceLaunch? launch = null)
    {
        if (!IsWebAddress(path) && !File.Exists(path) && !Directory.Exists(path)) { Ui.Error(L.Text("Файл больше не существует: ") + path); return; }
        try { using var process = Process.Start(LaunchInfo(path, launch)); }
        catch (Exception ex) { Ui.Error(L.Text("Не удалось открыть файл. ") + ex.Message); return; }
        // The shell accepted this invocation. Observers manage their own persistence errors.
        OpenSucceeded?.Invoke(path, launch);
    }
    internal static void Reveal(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); }
        catch (Exception ex) { Ui.Error(ex.Message); }
    }
    internal static void OpenStartupSettings() => Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
    internal static void OpenDisplaySettings() => Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
}
