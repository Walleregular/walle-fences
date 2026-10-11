using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace WalleFences.App;

internal enum WallpaperPosition { Center, Tile, Stretch, Fit, Fill, Span }
internal sealed record WallpaperImage(string Path, BitmapSource Source, int PixelWidth, int PixelHeight);
internal sealed record WallpaperMonitor(string Id, Rect Bounds, string? Path, WallpaperImage? Image, WallpaperPosition? Position = null);
internal sealed record WallpaperSnapshot(int Version, string Source, WallpaperPosition Position, Color Background, IReadOnlyList<WallpaperMonitor> Monitors, Rect VirtualBounds, bool Loading, string? Error);

// Read-only Windows background metadata. Never captures the screen or changes wallpaper settings.
internal static class DesktopWallpaper
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, WallpaperImage?> Images = new(StringComparer.OrdinalIgnoreCase);
    private static WallpaperSnapshot _current = Fallback(0, true, null);
    private static Task? _pending;
    private static DateTimeOffset _lastRead;
    private static int _version, _generation;
    internal static event Action? Changed;
    internal static WallpaperSnapshot Current { get { lock (Gate) return _current; } }
    static DesktopWallpaper()
    {
        SystemEvents.DisplaySettingsChanged += (_, _) => Invalidate();
        SystemEvents.UserPreferenceChanged += (_, e) => { if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color) Invalidate(); };
    }
    internal static void Invalidate()
    {
        lock (Gate) { _generation++; _lastRead = DateTimeOffset.MinValue; }
        _ = RefreshAsync();
    }
    internal static Task RefreshAsync(bool force = false)
    {
        lock (Gate)
        {
            if (force) { _generation++; _lastRead = DateTimeOffset.MinValue; }
            if (_pending is { IsCompleted: false }) return _pending;
            if (!force && !_current.Loading && DateTimeOffset.UtcNow - _lastRead < TimeSpan.FromSeconds(15)) return Task.CompletedTask;
            _pending = Task.Run(ReadAndPublish); return _pending;
        }
    }
    private static void ReadAndPublish()
    {
        int generation;
        lock (Gate) generation = _generation;
        WallpaperSnapshot snapshot;
        try { snapshot = ReadWindows(Interlocked.Increment(ref _version)); }
        catch (Exception ex) { snapshot = Fallback(Interlocked.Increment(ref _version), false, ex.GetType().Name); }
        if (snapshot.Monitors.Any(m => m.Image == null))
        {
            try { snapshot = WallpaperEngineAssets.TryRead(snapshot) ?? snapshot; }
            catch (Exception ex) { snapshot = snapshot with { Error = snapshot.Error ?? ex.GetType().Name }; }
        }
        lock (Gate) { _current = snapshot; _lastRead = DateTimeOffset.UtcNow; }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is { HasShutdownStarted: false }) dispatcher.BeginInvoke(() => Changed?.Invoke());
        lock (Gate)
        {
            if (generation == _generation) return;
            // An OS notification arrived while decoding. Serialize one follow-up read.
            _pending = null; _lastRead = DateTimeOffset.MinValue;
            _ = RefreshAsync();
        }
    }
    private static WallpaperSnapshot ReadWindows(int version)
    {
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), true)!);
            var desktop = (IDesktopWallpaper)instance!;
            Marshal.ThrowExceptionForHR(desktop.GetBackgroundColor(out var color));
            Marshal.ThrowExceptionForHR(desktop.GetPosition(out var position));
            Marshal.ThrowExceptionForHR(desktop.GetMonitorDevicePathCount(out var count));
            var monitors = new List<WallpaperMonitor>();
            for (uint index = 0; index < Math.Min(count, 64); index++)
            {
                if (desktop.GetMonitorDevicePathAt(index, out var id) < 0 || string.IsNullOrWhiteSpace(id)) continue;
                if (desktop.GetMonitorRect(id, out var rect) != 0 || rect.Right <= rect.Left || rect.Bottom <= rect.Top) continue;
                var bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                var path = desktop.GetWallpaper(id, out var wallpaper) >= 0 && !string.IsNullOrWhiteSpace(wallpaper) ? wallpaper : null;
                monitors.Add(new(id, bounds, path, ReadImage(path)));
            }
            if (monitors.Count == 0) throw new COMException("No attached wallpaper monitors.");
            var background = Color.FromRgb((byte)(color & 255), (byte)(color >> 8 & 255), (byte)(color >> 16 & 255));
            return new(version, "IDesktopWallpaper", position, background, monitors, Union(monitors.Select(m => m.Bounds)), false, monitors.All(m => m.Path == null) ? "NoImageProvided" : null);
        }
        finally { if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
    }
    private static WallpaperImage? ReadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var info = new FileInfo(path); if (!info.Exists || info.Length > 128 * 1024 * 1024) return null;
            var key = info.FullName + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length;
            if (Images.TryGetValue(key, out var cached)) return cached;
            int width, height;
            using (var stream = info.OpenRead())
            {
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0]; width = frame.PixelWidth; height = frame.PixelHeight;
            }
            if (width <= 0 || height <= 0) return null;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (width >= height) image.DecodePixelWidth = Math.Min(1280, width); else image.DecodePixelHeight = Math.Min(1280, height);
            image.UriSource = new Uri(info.FullName, UriKind.Absolute); image.EndInit(); image.Freeze();
            if (Images.Count >= 16) Images.Clear(); var result = new WallpaperImage(info.FullName, image, width, height); Images[key] = result; return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException or InvalidOperationException or COMException) { return null; }
    }
    private static WallpaperSnapshot Fallback(int version, bool loading, string? error)
    {
        // The OS desktop colour is real even when Explorer or the COM service is unavailable.
        var raw = GetSysColor(1); var color = Color.FromRgb((byte)(raw & 255), (byte)(raw >> 8 & 255), (byte)(raw >> 16 & 255));
        var monitors = Forms.Screen.AllScreens.Select(screen => new WallpaperMonitor(screen.DeviceName, new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height), null, null)).ToArray();
        return new(version, "SystemColor", WallpaperPosition.Fill, color, monitors, Union(monitors.Select(m => m.Bounds)), loading, error);
    }
    private static Rect Union(IEnumerable<Rect> rectangles)
    {
        var result = Rect.Empty; foreach (var rectangle in rectangles) result.Union(rectangle); return result.IsEmpty ? new Rect(0, 0, 1, 1) : result;
    }
    internal static WallpaperMonitor SelectMonitor(WallpaperSnapshot snapshot, DisplayArea display, double globalScale)
    {
        var exact = snapshot.Monitors.FirstOrDefault(m => m.Id.Equals(display.Id, StringComparison.OrdinalIgnoreCase)); if (exact != null) return exact;
        var work = new Rect(display.X / globalScale, display.Y / globalScale, display.Width / globalScale, display.Height / globalScale);
        double Overlap(Rect monitor) { var intersection = Rect.Intersect(monitor, work); return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height; }
        return snapshot.Monitors.OrderByDescending(m => Overlap(m.Bounds)).FirstOrDefault() ?? new(display.Id, work, null, null);
    }
    internal static Rect ImageRectangle(WallpaperPosition position, double imageWidth, double imageHeight, Rect area)
    {
        if (position == WallpaperPosition.Stretch) return area;
        var scale = position == WallpaperPosition.Center ? 1 : position == WallpaperPosition.Fit ? Math.Min(area.Width / imageWidth, area.Height / imageHeight) : Math.Max(area.Width / imageWidth, area.Height / imageHeight);
        var width = imageWidth * scale; var height = imageHeight * scale;
        return new(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }
    [DllImport("user32.dll")] private static extern uint GetSysColor(int index);
    [StructLayout(LayoutKind.Sequential)] private struct PhysicalRect { internal int Left, Top, Right, Bottom; }
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        [PreserveSig] int GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
        [PreserveSig] int GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRect([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out PhysicalRect rect);
        [PreserveSig] int SetBackgroundColor(uint color);
        [PreserveSig] int GetBackgroundColor(out uint color);
        [PreserveSig] int SetPosition(WallpaperPosition position);
        [PreserveSig] int GetPosition(out WallpaperPosition position);
    }
}
