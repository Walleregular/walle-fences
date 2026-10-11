namespace WalleFences.Core;

public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double x, double y, double margin = 0) => x >= X - margin && x <= Right + margin && y >= Y - margin && y <= Bottom + margin;
    public double DistanceSquared(double x, double y)
    {
        var dx = Math.Max(X - x, Math.Max(0, x - Right)); var dy = Math.Max(Y - y, Math.Max(0, y - Bottom));
        return dx * dx + dy * dy;
    }
}

public static class EdgeLayout
{
    public static LayoutRect Bounds(Fence fence, DisplayArea display, bool expanded)
    {
        var scale = display.Scale;
        var vertical = fence.Edge is DockEdge.Left or DockEdge.Right;
        var available = vertical ? display.Height : display.Width;
        var length = Math.Min(fence.EdgeLength * scale, available);
        var offset = Math.Clamp(fence.EdgeOffset * scale, 0, Math.Max(0, available - length));
        var automaticSpan = fence.Edge != DockEdge.None && fence.EdgeSpanMode != EdgeSpanMode.Custom;
        if (automaticSpan)
        {
            var count = fence.EdgeSpanMode == EdgeSpanMode.Full ? 1 : Math.Clamp(fence.EdgeBlockCount, 1, 12);
            var index = fence.EdgeSpanMode == EdgeSpanMode.Full ? 0 : Math.Clamp(fence.EdgeBlockIndex, 0, count - 1);
            // Both neighbours share the same boundary; the final block reaches the edge exactly.
            offset = available * index / count;
            length = available * (index + 1) / count - offset;
        }
        if (EdgeCollisionLayout.TryGetResolvedSpan(fence, display, out var resolvedOffset, out var resolvedLength))
        {
            offset = resolvedOffset; length = resolvedLength;
        }
        var thickness = fence.EdgeThickness * scale;
        var strip = fence.Edge switch
        {
            DockEdge.Top => new LayoutRect(display.X + offset, display.Y, length, thickness),
            DockEdge.Bottom => new LayoutRect(display.X + offset, display.Bottom - thickness, length, thickness),
            DockEdge.Left => new LayoutRect(display.X, display.Y + offset, thickness, length),
            DockEdge.Right => new LayoutRect(display.Right - thickness, display.Y + offset, thickness, length),
            _ => new LayoutRect(fence.X, fence.Y, fence.Width * scale, fence.Height * scale)
        };
        if (!expanded) return strip;
        var width = Math.Min(fence.Width * scale, display.Width); var height = Math.Min(fence.Height * scale, display.Height);
        if (automaticSpan) { if (vertical) height = length; else width = length; }
        var x = fence.Edge == DockEdge.Right ? display.Right - width : Math.Clamp(strip.X, display.X, display.Right - width);
        var y = fence.Edge == DockEdge.Bottom ? display.Bottom - height : Math.Clamp(strip.Y, display.Y, display.Bottom - height);
        return new(x, y, width, height);
    }

    public static void UseCustomSpan(Fence fence, DisplayArea display)
    {
        if (fence.EdgeSpanMode == EdgeSpanMode.Custom)
        {
            // A crowded edge can temporarily compress a requested handle. Drag its visible
            // geometry, rather than jumping back to the larger requested interval first.
            if (fence.Edge != DockEdge.None && EdgeCollisionLayout.TryGetResolvedSpan(fence, display, out var offset, out var length))
            {
                fence.EdgeOffset = offset / display.Scale; fence.EdgeLength = length / display.Scale;
            }
            return;
        }
        if (fence.Edge != DockEdge.None)
        {
            var strip = Bounds(fence, display, false); var expanded = Bounds(fence, display, true);
            var vertical = fence.Edge is DockEdge.Left or DockEdge.Right;
            fence.EdgeLength = (vertical ? strip.Height : strip.Width) / display.Scale;
            fence.EdgeOffset = (vertical ? strip.Y - display.Y : strip.X - display.X) / display.Scale;
            if (vertical) fence.Height = Math.Max(130, expanded.Height / display.Scale);
            else fence.Width = Math.Max(200, expanded.Width / display.Scale);
            fence.X = expanded.X; fence.Y = expanded.Y;
        }
        fence.EdgeSpanMode = EdgeSpanMode.Custom;
    }
}

// Pointer intent is independent of transient WPF MouseEnter/MouseLeave events.
public sealed class HoverReveal
{
    public bool IsOpen { get; private set; }
    private long? _enterAt, _leaveAt;
    public void Reset(bool open = false) { IsOpen = open; _enterAt = _leaveAt = null; }
    public bool Tick(long now, bool near, bool inside, bool hold, int openDelay, int closeDelay, bool automatic = true)
    {
        var before = IsOpen;
        if (IsOpen)
        {
            if (inside || hold) _leaveAt = null;
            else { _leaveAt ??= now; if (now - _leaveAt >= closeDelay) Reset(); }
        }
        else if (near && automatic)
        {
            _enterAt ??= now;
            if (now - _enterAt >= openDelay) { IsOpen = true; _leaveAt = _enterAt = null; }
        }
        else _enterAt = null;
        return before != IsOpen;
    }
}
