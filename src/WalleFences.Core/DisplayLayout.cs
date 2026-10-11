namespace WalleFences.Core;

// Work-area coordinates use the existing layout coordinate system; Scale converts logical DIPs into it.
public sealed record DisplayArea(string Id, string Name, double X, double Y, double Width, double Height, double Scale, bool Primary = false)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

// The preferred placement is kept while a display is disconnected or its resolution is temporarily smaller.
public sealed record MonitorPlacement
{
    public string MonitorId { get; init; } = "";
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public double Width { get; init; } = 330;
    public double Height { get; init; } = 300;
}

public static class DisplayLayout
{
    public const double CollapsedHeight = 64;
    public static DisplayArea Select(Fence fence, IReadOnlyList<DisplayArea> displays)
    {
        if (displays.Count == 0) throw new ArgumentException("At least one display is required.", nameof(displays));
        if (fence.Edge != DockEdge.None && fence.Placement is { } placement && displays.FirstOrDefault(d => d.Id.Equals(placement.MonitorId, StringComparison.OrdinalIgnoreCase)) is { } home) return home;
        return displays.OrderByDescending(d => HeaderOverlap(fence, d))
            .ThenBy(d => Distance(fence.X, fence.Y, d)).ThenByDescending(d => d.Primary).First();
    }
    private static double HeaderOverlap(Fence fence, DisplayArea display)
    {
        var width = Math.Max(0, Math.Min(fence.X + fence.Width * display.Scale, display.Right) - Math.Max(fence.X, display.X));
        var height = Math.Max(0, Math.Min(fence.Y + FenceLayout.HeaderHeight(fence.DisplayMode) * display.Scale, display.Bottom) - Math.Max(fence.Y, display.Y));
        return width * height;
    }
    private static double Distance(double x, double y, DisplayArea display)
    {
        var dx = Math.Max(display.X - x, Math.Max(0, x - display.Right));
        var dy = Math.Max(display.Y - y, Math.Max(0, y - display.Bottom));
        return dx * dx + dy * dy;
    }
    public static void Clamp(Fence fence, DisplayArea display)
    {
        fence.Width = Math.Clamp(fence.Width, 200, Math.Max(200, display.Width / display.Scale));
        fence.Height = Math.Clamp(fence.Height, 130, Math.Max(130, display.Height / display.Scale));
        if (fence.Edge != DockEdge.None) { var bounds = EdgeLayout.Bounds(fence, display, true); fence.X = bounds.X; fence.Y = bounds.Y; return; }
        fence.X = Math.Clamp(fence.X, display.X, Math.Max(display.X, display.Right - fence.Width * display.Scale));
        var visibleHeight = fence.ContentCollapsed ? FenceLayout.CollapsedHeight(fence) : fence.Height;
        fence.Y = Math.Clamp(fence.Y, display.Y, Math.Max(display.Y, display.Bottom - visibleHeight * display.Scale));
    }
    public static void Capture(Fence fence, IReadOnlyList<DisplayArea> displays, DisplayArea? target = null)
    {
        var display = target ?? Select(fence, displays); Clamp(fence, display);
        fence.Placement = new() { MonitorId = display.Id, OffsetX = (fence.X - display.X) / display.Scale, OffsetY = (fence.Y - display.Y) / display.Scale, Width = fence.Width, Height = fence.Height };
    }
    public static void Reconcile(Fence fence, IReadOnlyList<DisplayArea> displays, bool restore = true)
    {
        if (fence.Placement == null) { Capture(fence, displays); return; }
        var placement = fence.Placement;
        var home = displays.FirstOrDefault(d => d.Id.Equals(placement.MonitorId, StringComparison.OrdinalIgnoreCase));
        if (home != null)
        {
            fence.X = home.X + placement.OffsetX * home.Scale; fence.Y = home.Y + placement.OffsetY * home.Scale;
            fence.Width = placement.Width; fence.Height = placement.Height; Clamp(fence, home);
        }
        else
        {
            // Only the visible projection changes. The preferred monitor and its placement remain saved.
            var fallback = displays.FirstOrDefault(d => d.Primary) ?? displays[0]; Clamp(fence, fallback);
            if (!restore) Capture(fence, displays, fallback);
        }
    }
    public static void MoveToDisplay(Fence fence, DisplayArea target, IReadOnlyList<DisplayArea> displays)
    {
        var source = Select(fence, displays);
        var x = (fence.X - source.X) / source.Scale; var y = (fence.Y - source.Y) / source.Scale;
        fence.X = target.X + Math.Max(0, x) * target.Scale; fence.Y = target.Y + Math.Max(0, y) * target.Scale;
        Capture(fence, displays, target);
    }
}
