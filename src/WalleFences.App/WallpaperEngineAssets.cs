using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace WalleFences.App;

// Read only the selected local assets of an already running Wallpaper Engine.
// Video previews are decoded from their files; no screen or foreign window pixels are read.
internal static class WallpaperEngineAssets
{
    private sealed record Asset(string File, string? Preview);
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, WallpaperImage> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".m4v" };
    private const int MaxDimension = 1280, MaxHeight = 800, MaxFrameBytes = 8 * 1024 * 1024;

    // Called by the serialized background wallpaper reader, never by a UI render callback.
    internal static WallpaperSnapshot? TryRead(WallpaperSnapshot metadata)
    {
        try
        {
            var configPath = FindRunningConfig(); if (configPath == null) return null;
            using var config = ReadJson(configPath, 2 * 1024 * 1024); if (config == null) return null;
            var general = default(JsonElement);
            foreach (var version in config.RootElement.EnumerateObject().Take(8))
                if (version.Value.ValueKind == JsonValueKind.Object && version.Value.TryGetProperty("general", out general)) break;
            if (general.ValueKind != JsonValueKind.Object || !general.TryGetProperty("wallpaperconfig", out var wallpaper) || !wallpaper.TryGetProperty("selectedwallpapers", out var selected) || selected.ValueKind != JsonValueKind.Object) return null;
            var map = default(JsonElement);
            if (general.TryGetProperty("user", out var user)) _ = user.TryGetProperty("monitormap", out map);
            var result = new List<WallpaperMonitor>(); var supplied = false;
            foreach (var monitor in metadata.Monitors.Take(64))
            {
                if (monitor.Image != null) { result.Add(monitor); continue; }
                var physicalId = monitor.Id.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase) ? Native.DisplayIdentity(monitor.Id).Id : monitor.Id;
                var slot = FindMonitorSlot(map, physicalId);
                if (slot == null || !selected.TryGetProperty(slot, out var selection) || selection.ValueKind != JsonValueKind.Object || !selection.TryGetProperty("file", out var active) || active.ValueKind != JsonValueKind.String)
                { result.Add(monitor); continue; }
                var assetFile = LocalFile(active.GetString());
                if (assetFile == null) { result.Add(monitor); continue; }
                var asset = new Asset(assetFile, FindPreview(assetFile)); var image = ReadThumbnail(asset);
                if (image == null) { result.Add(monitor); continue; }
                supplied = true; result.Add(monitor with { Path = asset.File, Image = image, Position = WallpaperPosition.Stretch });
            }
            if (!supplied) return null;
            return metadata with { Source = "WallpaperEngine", Monitors = result, Loading = false, Error = result.All(m => m.Image != null) ? null : "PartialWallpaperAssets" };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { return null; }
    }
    private static string? FindRunningConfig()
    {
        foreach (var name in new[] { "wallpaper64", "wallpaper32" })
        {
            foreach (var process in Process.GetProcessesByName(name).Take(8))
            {
                using (process)
                {
                    try
                    {
                        var executable = process.MainModule?.FileName; var directory = executable == null ? null : Path.GetDirectoryName(executable);
                        if (directory != null && LocalFile(Path.Combine(directory, "config.json")) is { } config) return config;
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
                }
            }
        }
        return null;
    }
    private static JsonDocument? ReadJson(string path, int maxBytes)
    {
        var file = new FileInfo(path); if (!file.Exists || file.Length <= 0 || file.Length > maxBytes) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream(); var chunk = new byte[8192]; int count;
        while ((count = stream.Read(chunk, 0, chunk.Length)) > 0) { if (buffer.Length + count > maxBytes) return null; buffer.Write(chunk, 0, count); }
        return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }
    private static string? FindMonitorSlot(JsonElement map, string physicalId)
    {
        if (map.ValueKind != JsonValueKind.Object) return null;
        var id = physicalId.Replace('/', '\\');
        foreach (var item in map.EnumerateObject().Take(64))
            if (item.Name.Replace('/', '\\').Equals(id, StringComparison.OrdinalIgnoreCase) && item.Value.ValueKind == JsonValueKind.Object && item.Value.TryGetProperty("location", out var location) && location.TryGetInt32(out var index) && index is >= 0 and < 64) return "Monitor" + index;
        return null;
    }
    private static string? LocalFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        var full = Path.GetFullPath(path); return File.Exists(full) ? full : null;
    }
    private static string? FindPreview(string activeFile)
    {
        var directory = Path.GetDirectoryName(activeFile); if (directory == null) return null;
        var projectPath = LocalFile(Path.Combine(directory, "project.json"));
        if (projectPath != null)
        {
            using var project = ReadJson(projectPath, 128 * 1024);
            if (project != null && project.RootElement.TryGetProperty("preview", out var preview) && preview.ValueKind == JsonValueKind.String)
            {
                var name = preview.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var candidate = Path.GetFullPath(Path.Combine(directory, name));
                    if (candidate.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && LocalFile(candidate) is { } found) return found;
                }
            }
        }
        return new[] { "preview.jpg", "preview.png", "preview.gif" }.Select(name => LocalFile(Path.Combine(directory, name))).FirstOrDefault(path => path != null);
    }
    private static WallpaperImage? ReadThumbnail(Asset asset)
    {
        var active = new FileInfo(asset.File); if (!active.Exists || active.Length <= 0 || active.Length > 4L * 1024 * 1024 * 1024) return null;
        var ffmpeg = VideoExtensions.Contains(active.Extension) ? FindFfmpeg() : null;
        var preview = asset.Preview == null ? null : new FileInfo(asset.Preview);
        var key = active.FullName + "|" + active.LastWriteTimeUtc.Ticks + "|" + active.Length + "|" + ffmpeg + "|" + preview?.LastWriteTimeUtc.Ticks;
        lock (CacheGate) if (Cache.TryGetValue(key, out var cached)) return cached;
        var image = VideoExtensions.Contains(active.Extension) ? ffmpeg == null ? null : DecodeVideo(active.FullName, ffmpeg) : DecodeImage(active.FullName, active.FullName);
        image ??= asset.Preview == null ? null : DecodeImage(asset.Preview, asset.File);
        if (image != null) lock (CacheGate) { if (Cache.Count >= 16) Cache.Clear(); Cache[key] = image; }
        return image;
    }
    private static string? FindFfmpeg()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Take(128))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            try { if (LocalFile(Path.Combine(directory.Trim('"'), "ffmpeg.exe")) is { } file) return file; }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException) { }
        }
        return null;
    }
    private static WallpaperImage? DecodeVideo(string file, string ffmpeg)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = ffmpeg, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden } };
        foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-ss", "1", "-i", file, "-map", "0:v:0", "-frames:v", "1", "-vf", "scale=1280:800:force_original_aspect_ratio=decrease", "-an", "-threads", "1", "-f", "image2pipe", "-vcodec", "png", "pipe:1" }) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) return null;
            var output = Task.Run(() => ReadFrame(process.StandardOutput.BaseStream));
            var error = Task.Run(() => { var buffer = new char[1024]; while (process.StandardError.Read(buffer, 0, buffer.Length) > 0) { } });
            if (!process.WaitForExit(6000)) { process.Kill(entireProcessTree: true); _ = process.WaitForExit(1000); return null; }
            if (!output.Wait(1000) || output.IsFaulted || process.ExitCode != 0) return null;
            _ = error.Wait(250);
            using var stream = new MemoryStream(output.Result, false); return DecodeBitmap(stream, file);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or AggregateException or System.ComponentModel.Win32Exception or NotSupportedException or FileFormatException or ArgumentException or System.Runtime.InteropServices.COMException) { return null; }
        finally { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
    }
    private static byte[] ReadFrame(Stream source)
    {
        using var result = new MemoryStream(); var buffer = new byte[65536]; int count;
        while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
        { if (result.Length + count > MaxFrameBytes) throw new IOException("Wallpaper thumbnail exceeds its bound."); result.Write(buffer, 0, count); }
        return result.ToArray();
    }
    private static WallpaperImage? DecodeImage(string path, string identity)
    {
        try
        {
            var file = new FileInfo(path); if (!file.Exists || file.Length <= 0 || file.Length > 32 * 1024 * 1024) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return DecodeBitmap(stream, identity);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException or InvalidOperationException or System.Runtime.InteropServices.COMException) { return null; }
    }
    private static WallpaperImage? DecodeBitmap(Stream stream, string identity)
    {
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0]; var width = frame.PixelWidth; var height = frame.PixelHeight;
        if (width <= 0 || height <= 0) return null;
        stream.Position = 0; var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream;
        if (width / (double)MaxDimension >= height / (double)MaxHeight) bitmap.DecodePixelWidth = Math.Min(width, MaxDimension); else bitmap.DecodePixelHeight = Math.Min(height, MaxHeight);
        bitmap.EndInit(); bitmap.Freeze(); return new(identity, bitmap, width, height);
    }
}
