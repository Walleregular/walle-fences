using System.Windows.Interop;

namespace WalleFences.App;

internal sealed partial class FenceWindow
{
    private readonly RevealMotion _revealMotion = new();
    private DisplayArea? _motionDisplay;
    private bool _motionSubscribed, _motionClosing, _motionKeepFloating, _preserveRevealBounds;
    private long _motionStartedAt;
    private uint _layoutNativeDpi;
    private bool _dpiRefreshQueued;
    private int _motionDuration;
    private double _motionOpacityFrom, _motionOpacityTo;
    internal bool IsRevealAnimating => _revealMotion.IsRunning;
    private bool KeepFloatingDuringReveal => IsRevealAnimating && _motionClosing && _motionKeepFloating;
    private bool RevealAnimationsEnabled => SystemParameters.ClientAreaAnimation;

    private LayoutRect RevealBounds(DisplayArea display, bool rolled)
    {
        if (IsEdgeDock) return EdgeLayout.Bounds(Fence, display, !rolled);
        var height = rolled ? CollapsedLogicalHeight : Fence.Height;
        var y = _hoverExpanded && Fence.ContentCollapsed ? Math.Clamp(Fence.Y, display.Y, Math.Max(display.Y, display.Bottom - height * display.Scale)) : Fence.Y;
        return new(Fence.X, y, Fence.Width * display.Scale, height * display.Scale);
    }

    private DisplayArea PrepareRevealLayout(bool fullViewport = false)
    {
        var display = DisplayLayout.Select(Fence, AppController.Displays());
        var nativeDpi = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle);
        var dpiScale = nativeDpi > 0 ? nativeDpi / 96d : VisualTreeHelper.GetDpi(this).DpiScaleX;
        _layoutNativeDpi = nativeDpi;
        var ratio = display.Scale / AppController.GlobalScale / dpiScale;
        if (Math.Abs(ratio - _renderRatio) > .001) { _surface.LayoutTransform = Math.Abs(ratio - 1) < .001 ? Transform.Identity : new ScaleTransform(ratio, ratio); _renderRatio = ratio; }
        var expanded = IsEdgeDock ? EdgeLayout.Bounds(Fence, display, true) : new LayoutRect(Fence.X, Fence.Y, Fence.Width * display.Scale, Fence.Height * display.Scale);
        // Keep the inner layout at its final size. Only the native viewport reveals it.
        _chrome.Width = expanded.Width / display.Scale;
        _chrome.Height = !fullViewport && !IsEdgeDock && VisuallyCollapsed ? CollapsedLogicalHeight : expanded.Height / display.Scale;
        _chrome.HorizontalAlignment = Fence.Edge == DockEdge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _chrome.VerticalAlignment = Fence.Edge == DockEdge.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        var onlyWidgets = Fence.ActiveTab.Widgets.Count > 0 && Fence.ActiveTab.Items.Count == 0 && Fence.ActiveTab.PortalPath == null;
        _widgetScroll.MaxHeight = WidgetHeightLimit(onlyWidgets);
        return display;
    }

    private LayoutRect CurrentRevealBounds()
    {
        Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var actual);
        var scale = AppController.GlobalScale;
        return new(actual.Left * scale, actual.Top * scale, (actual.Right - actual.Left) * scale, (actual.Bottom - actual.Top) * scale);
    }

    private void ApplyTransientBounds(LayoutRect bounds, DisplayArea display)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0 || _closed) return;
        var pixels = RevealPixelBounds(bounds); var width = pixels.Right - pixels.Left; var height = pixels.Bottom - pixels.Top;
        var point = new Native.Point { X = pixels.Left, Y = pixels.Top };
        void PositionNativeBounds()
        {
            Native.GetWindowRect(hwnd, out var actual);
            if (actual.Left == point.X && actual.Top == point.Y && actual.Right - actual.Left == width && actual.Bottom - actual.Top == height) return;
            var client = point; var parent = Native.ChildParent(hwnd); if (parent != 0) Native.ScreenToClient(parent, ref client);
            Native.SetWindowPos(hwnd, Native.HwndTop, client.X, client.Y, width, height, Native.SwpNoActivate | Native.SwpNoZOrder);
        }
        // Size and position must move together before WPF resizes a right/bottom viewport.
        // Resizing at the old left edge can temporarily put it onto a neighbour with another DPI.
        PositionNativeBounds();
        if (Native.GetDpiForWindow(hwnd) != _layoutNativeDpi) PrepareRevealLayout(IsRevealAnimating);
        Width = bounds.Width / display.Scale * _renderRatio; Height = bounds.Height / display.Scale * _renderRatio;
        PositionNativeBounds();
    }

    private Native.Rect RevealPixelBounds(LayoutRect bounds)
    {
        var scale = AppController.GlobalScale;
        var width = Math.Max(1, (int)Math.Round(bounds.Width / scale)); var height = Math.Max(1, (int)Math.Round(bounds.Height / scale));
        // Rounding from the anchored edge keeps right/bottom strips still at every DPI.
        var x = Fence.Edge == DockEdge.Right ? (int)Math.Round(bounds.Right / scale) - width : (int)Math.Round(bounds.X / scale);
        var y = Fence.Edge == DockEdge.Bottom ? (int)Math.Round(bounds.Bottom / scale) - height : (int)Math.Round(bounds.Y / scale);
        if (IsEdgeDock)
        {
            // Touching strips and panels round their shared boundary once, including
            // collision-resolved custom spans, without pixel gaps or overlap.
            if (Fence.Edge is DockEdge.Top or DockEdge.Bottom)
            {
                x = (int)Math.Round(bounds.X / scale); width = Math.Max(1, (int)Math.Round(bounds.Right / scale) - x);
            }
            else
            {
                y = (int)Math.Round(bounds.Y / scale); height = Math.Max(1, (int)Math.Round(bounds.Bottom / scale) - y);
            }
        }
        return new Native.Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
    }

    private void ApplyRevealVisibility(bool rolled)
    {
        _edgeHandle.Visibility = IsEdgeDock && rolled ? Visibility.Visible : Visibility.Collapsed;
        _chrome.Visibility = IsEdgeDock && rolled ? Visibility.Collapsed : Visibility.Visible;
        _chrome.IsHitTestVisible = !IsEdgeDock || !rolled;
        _content.Visibility = rolled ? Visibility.Collapsed : Visibility.Visible;
        _content.IsHitTestVisible = !rolled; _content.Opacity = rolled ? 0 : 1;
    }

    private void TransitionReveal(bool rolled, bool animate)
    {
        if (!animate || !RevealAnimationsEnabled || !IsLoaded || !IsVisible)
        {
            // Reparent once before measuring the final native DPI. A second MoveTo here
            // made a global Peek prepare and resize every window twice on the UI thread.
            StopRevealAnimation(); ApplyLayer(); MoveTo(Fence.X, Fence.Y); return;
        }
        var display = PrepareRevealLayout(true); var target = RevealBounds(display, rolled);
        if (IsRevealAnimating && _revealMotion.Target == target) return;
        var now = Environment.TickCount64;
        _motionKeepFloating = ((long)Native.GetWindowLong(new WindowInteropHelper(this).Handle, Native.GwlExStyle) & 8) != 0;
        _motionClosing = rolled;
        var opacity = _content.Visibility == Visibility.Visible ? _content.Opacity : 0;
        if (!rolled) ApplyLayer();
        // Reparenting can change native DPI; measure again before the first frame.
        display = PrepareRevealLayout(true); target = RevealBounds(display, rolled);
        _motionDuration = rolled ? RevealMotion.ClosingDurationMs : RevealMotion.OpeningDurationMs;
        var actual = CurrentRevealBounds(); var pixels = RevealPixelBounds(target); var actualPixels = RevealPixelBounds(actual);
        if (pixels.Left == actualPixels.Left && pixels.Top == actualPixels.Top && pixels.Right == actualPixels.Right && pixels.Bottom == actualPixels.Bottom && Math.Abs(opacity - (rolled ? 0 : 1)) < .000001)
        {
            StopRevealAnimation(); ApplyRevealVisibility(rolled); ApplyTransientBounds(target, display); ApplyLayer(); ApplyMonitorBounds(); return;
        }
        if (!_revealMotion.Start(actual, target, now, _motionDuration))
        {
            StopRevealAnimation();
            ApplyRevealVisibility(rolled); ApplyTransientBounds(target, display); ApplyLayer(); ApplyMonitorBounds(); return;
        }
        _motionDisplay = display; _motionStartedAt = now; _motionOpacityFrom = opacity; _motionOpacityTo = rolled ? 0 : 1;
        _chrome.Visibility = Visibility.Visible; _edgeHandle.Visibility = Visibility.Collapsed;
        _chrome.IsHitTestVisible = !IsEdgeDock || !rolled;
        _content.Visibility = Visibility.Visible; _content.IsHitTestVisible = !rolled; _content.Opacity = opacity;
        if (!_motionSubscribed) { CompositionTarget.Rendering += RevealFrame; _motionSubscribed = true; }
        RenderReveal(now);
    }

    private void RevealFrame(object? sender, EventArgs args)
    {
        if (_closed || !IsVisible) { StopRevealAnimation(); return; }
        if (!RevealAnimationsEnabled) { SetRollHeight(false); return; }
        RenderReveal(Environment.TickCount64);
    }

    private void RenderReveal(long now)
    {
        if (_motionDisplay == null || !IsRevealAnimating) return;
        var bounds = _revealMotion.Sample(now);
        var progress = Math.Clamp((now - _motionStartedAt) / (double)_motionDuration, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        _content.Opacity = _motionOpacityFrom + (_motionOpacityTo - _motionOpacityFrom) * eased;
        ApplyTransientBounds(bounds, _motionDisplay);
        if (!IsRevealAnimating)
        {
            StopRevealAnimation(); ApplyRevealVisibility(VisuallyCollapsed); ApplyLayer(); ApplyMonitorBounds();
        }
    }

    private void StopRevealAnimation()
    {
        if (_motionSubscribed) { CompositionTarget.Rendering -= RevealFrame; _motionSubscribed = false; }
        _revealMotion.Reset(_revealMotion.Target); _motionDisplay = null;
    }

}
