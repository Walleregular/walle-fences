using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace WalleFences.App;

internal sealed partial class FenceWindow
{
    [Flags]
    private enum ResizeSide
    {
        None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8,
        TopLeft = Left | Top, TopRight = Right | Top, BottomLeft = Left | Bottom, BottomRight = Right | Bottom
    }

    private sealed record ResizeHandle(Thumb Thumb, ResizeSide Side);
    private sealed record ResizeOrigin(DisplayArea Display, LayoutRect Bounds, Native.Point Pointer,
        double X, double Y, double Width, double Height, double EdgeLength, double EdgeOffset, OpeningSize OpeningSize,
        EdgeSpanMode EdgeSpanMode, int EdgeBlockCount, int EdgeBlockIndex);

    private readonly List<ResizeHandle> _resizeHandles = [];
    private readonly TextBlock _resizeSize = Ui.Text("", 11, weight: FontWeights.SemiBold);
    private readonly Border _resizeSizeBadge = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private Thumb? _resizeThumb;
    private ResizeOrigin? _resizeOrigin;
    private EdgeCollisionGesture? _resizeEdgeGesture;
    private bool _resizeChanged;
    private bool IsResizing => _resizeOrigin != null;

    private Thumb ResizeGrip()
    {
        var grip = CreateResizeHandle(ResizeSide.Right | ResizeSide.Bottom, true);
        grip.Width = grip.Height = 17;
        grip.HorizontalAlignment = HorizontalAlignment.Right;
        grip.VerticalAlignment = VerticalAlignment.Center;
        grip.Opacity = .55;
        var template = new ControlTemplate(typeof(Thumb));
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var icon = new FrameworkElementFactory(typeof(Image));
        icon.SetValue(Image.SourceProperty, Brand.Icon("resize", _ink, 16).Source);
        icon.SetValue(FrameworkElement.WidthProperty, 16d);
        icon.SetValue(FrameworkElement.HeightProperty, 16d);
        icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.SetValue(UIElement.IsHitTestVisibleProperty, false);
        surface.AppendChild(icon); template.VisualTree = surface; grip.Template = template;
        return grip;
    }

    private void InitializeResizeHandles()
    {
        var overlay = new Grid();
        foreach (var side in new[] { ResizeSide.Left, ResizeSide.Top, ResizeSide.Right, ResizeSide.Bottom,
            ResizeSide.Left | ResizeSide.Top, ResizeSide.Right | ResizeSide.Top,
            ResizeSide.Left | ResizeSide.Bottom, ResizeSide.Right | ResizeSide.Bottom })
        {
            var corner = HasHorizontalSide(side) && HasVerticalSide(side);
            var thumb = CreateResizeHandle(side, corner);
            thumb.Margin = corner ? new Thickness(0) : HasHorizontalSide(side) ? new Thickness(0, 12, 0, 12) : new Thickness(12, 0, 12, 0);
            thumb.Width = corner ? 12 : HasHorizontalSide(side) ? 5 : double.NaN;
            thumb.Height = corner ? 12 : HasVerticalSide(side) ? 5 : double.NaN;
            thumb.HorizontalAlignment = side.HasFlag(ResizeSide.Left) ? HorizontalAlignment.Left : side.HasFlag(ResizeSide.Right) ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            thumb.VerticalAlignment = side.HasFlag(ResizeSide.Top) ? VerticalAlignment.Top : side.HasFlag(ResizeSide.Bottom) ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
            overlay.Children.Add(thumb);
        }
        _surface.Children.Add(overlay);
        _resizeSize.Foreground = _ink;
        _resizeSizeBadge.Child = _resizeSize;
        _resizeSizeBadge.Padding = new Thickness(10, 5, 10, 5);
        _resizeSizeBadge.Margin = new Thickness(0, 0, 0, 24);
        _resizeSizeBadge.HorizontalAlignment = HorizontalAlignment.Center;
        _resizeSizeBadge.VerticalAlignment = VerticalAlignment.Bottom;
        _resizeSizeBadge.CornerRadius = new CornerRadius(6);
        _resizeSizeBadge.Background = Ui.Brush(Fence.Color, .98);
        _resizeSizeBadge.BorderBrush = Ui.Brush(Fence.Accent, .65);
        _resizeSizeBadge.BorderThickness = new Thickness(1);
        _surface.Children.Add(_resizeSizeBadge);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !IsResizing) return;
            CancelResize(); e.Handled = true;
        };
        RefreshResizeHandles();
    }

    private Thumb CreateResizeHandle(ResizeSide side, bool corner)
    {
        var thumb = new Thumb { Focusable = false, Cursor = ResizeCursor(side),
            ToolTip = L.Text("Потяните край или угол, чтобы изменить размер") };
        AutomationProperties.SetName(thumb, L.Text("Изменить размер"));
        AutomationProperties.SetAutomationId(thumb, $"resize-{side.ToString().Replace(", ", "-").ToLowerInvariant()}-{Fence.Id}");
        var template = new ControlTemplate(typeof(Thumb));
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var guide = new FrameworkElementFactory(typeof(Border)) { Name = "Guide" };
        guide.SetValue(UIElement.IsHitTestVisibleProperty, false);
        guide.SetValue(UIElement.OpacityProperty, corner ? .2d : 0d);
        guide.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        guide.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        if (corner)
        {
            guide.SetValue(FrameworkElement.WidthProperty, 7d); guide.SetValue(FrameworkElement.HeightProperty, 7d);
            guide.SetValue(Border.BorderBrushProperty, _accent);
            guide.SetValue(Border.BorderThicknessProperty, new Thickness(side.HasFlag(ResizeSide.Left) ? 1.5 : 0,
                side.HasFlag(ResizeSide.Top) ? 1.5 : 0, side.HasFlag(ResizeSide.Right) ? 1.5 : 0, side.HasFlag(ResizeSide.Bottom) ? 1.5 : 0));
        }
        else
        {
            guide.SetValue(FrameworkElement.WidthProperty, HasHorizontalSide(side) ? 1.5d : 38d);
            guide.SetValue(FrameworkElement.HeightProperty, HasHorizontalSide(side) ? 38d : 1.5d);
            guide.SetValue(Border.BackgroundProperty, _accent); guide.SetValue(Border.CornerRadiusProperty, new CornerRadius(1));
        }
        surface.AppendChild(guide); template.VisualTree = surface;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(OpacityProperty, .7, "Guide")); template.Triggers.Add(hover);
        var dragging = new Trigger { Property = Thumb.IsDraggingProperty, Value = true };
        dragging.Setters.Add(new Setter(OpacityProperty, 1d, "Guide")); template.Triggers.Add(dragging);
        thumb.Template = template;
        thumb.DragStarted += (_, _) => BeginResize(thumb, side);
        thumb.DragDelta += (_, _) => ResizeFromPointer(side);
        thumb.DragCompleted += (_, e) => CompleteResize(e.Canceled);
        _resizeHandles.Add(new(thumb, side));
        return thumb;
    }

    private static bool HasHorizontalSide(ResizeSide side) => (side & (ResizeSide.Left | ResizeSide.Right)) != 0;
    private static bool HasVerticalSide(ResizeSide side) => (side & (ResizeSide.Top | ResizeSide.Bottom)) != 0;
    private static Cursor ResizeCursor(ResizeSide side) => side switch
    {
        ResizeSide.Left or ResizeSide.Right => Cursors.SizeWE,
        ResizeSide.Top or ResizeSide.Bottom => Cursors.SizeNS,
        ResizeSide.TopLeft or ResizeSide.BottomRight => Cursors.SizeNWSE,
        _ => Cursors.SizeNESW
    };

    private bool CanResize(ResizeSide side)
    {
        if (_closed || Fence.Locked || Fence.Tabs.Count == 0 || VisuallyCollapsed || !IsEnabled) return false;
        var anchored = Fence.Edge switch { DockEdge.Left => ResizeSide.Left, DockEdge.Top => ResizeSide.Top,
            DockEdge.Right => ResizeSide.Right, DockEdge.Bottom => ResizeSide.Bottom, _ => ResizeSide.None };
        return (side & anchored) == 0;
    }

    private void RefreshResizeHandles()
    {
        foreach (var handle in _resizeHandles)
            handle.Thumb.Visibility = CanResize(handle.Side) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BeginResize(Thumb thumb, ResizeSide side)
    {
        if (!CanResize(side) || !Native.GetCursorPos(out var pointer)) { thumb.CancelDrag(); return; }
        // Freeze a partially opened viewport before measuring; no animation fights pointer movement.
        SetRollHeight(false);
        var display = DisplayLayout.Select(Fence, AppController.Displays());
        var bounds = RevealBounds(display, false);
        _resizeOrigin = new(display, bounds, pointer, Fence.X, Fence.Y, Fence.Width, Fence.Height, Fence.EdgeLength, Fence.EdgeOffset, Fence.OpeningSize,
            Fence.EdgeSpanMode, Fence.EdgeBlockCount, Fence.EdgeBlockIndex);
        _resizeEdgeGesture = _app.BeginEdgeCollisionGesture(Fence);
        _resizeThumb = thumb; _resizeChanged = false;
        ShowResizeDimensions();
    }

    private void ResizeFromPointer(ResizeSide side)
    {
        if (_resizeOrigin is not { } origin) return;
        if (!CanResize(side)) { CancelResize(); return; }
        if (!Native.GetCursorPos(out var pointer)) return;
        // Every pointer position starts from the gesture's original placement. Neighbours
        // may move in the preview, but a previous preview never becomes the next baseline.
        RestoreResizeOrigin(origin);
        _app.RestoreEdgeCollisionGesture(_resizeEdgeGesture, false);
        var display = origin.Display; var bounds = origin.Bounds;
        var dx = (pointer.X - origin.Pointer.X) * AppController.GlobalScale;
        var dy = (pointer.Y - origin.Pointer.Y) * AppController.GlobalScale;
        var verticalEdge = Fence.Edge is DockEdge.Left or DockEdge.Right;
        var horizontalEdge = Fence.Edge is DockEdge.Top or DockEdge.Bottom;
        var changesSpan = horizontalEdge && HasHorizontalSide(side) && Math.Abs(dx) > .01
            || verticalEdge && HasVerticalSide(side) && Math.Abs(dy) > .01;
        if (changesSpan) EdgeLayout.UseCustomSpan(Fence, display);
        var minWidth = Math.Min(200 * display.Scale, display.Width);
        var minHeight = Math.Min(130 * display.Scale, display.Height);
        var left = bounds.X; var top = bounds.Y; var right = bounds.Right; var bottom = bounds.Bottom;
        if (side.HasFlag(ResizeSide.Left)) left = Math.Clamp(bounds.X + dx, display.X, Math.Max(display.X, right - minWidth));
        if (side.HasFlag(ResizeSide.Right)) right = Math.Clamp(bounds.Right + dx, left + minWidth, Math.Max(left + minWidth, display.Right));
        if (side.HasFlag(ResizeSide.Top)) top = Math.Clamp(bounds.Y + dy, display.Y, Math.Max(display.Y, bottom - minHeight));
        if (side.HasFlag(ResizeSide.Bottom)) bottom = Math.Clamp(bounds.Bottom + dy, top + minHeight, Math.Max(top + minHeight, display.Bottom));
        // The model retains the user's free size while an automatic span owns the tangential axis.
        if (!horizontalEdge || Fence.EdgeSpanMode == EdgeSpanMode.Custom) Fence.Width = (right - left) / display.Scale;
        if (!verticalEdge || Fence.EdgeSpanMode == EdgeSpanMode.Custom) Fence.Height = (bottom - top) / display.Scale;
        Fence.X = left; Fence.Y = top;
        // Moving the opposite tangential edge also moves the strip; the screen attachment stays fixed.
        if (horizontalEdge && Fence.EdgeSpanMode == EdgeSpanMode.Custom && HasHorizontalSide(side))
            Fence.EdgeOffset = Math.Clamp((left - display.X) / display.Scale, 0, Math.Max(0, display.Width / display.Scale - Fence.EdgeLength));
        if (verticalEdge && Fence.EdgeSpanMode == EdgeSpanMode.Custom && HasVerticalSide(side))
            Fence.EdgeOffset = Math.Clamp((top - display.Y) / display.Scale, 0, Math.Max(0, display.Height / display.Scale - Fence.EdgeLength));
        Fence.OpeningSize = OpeningSize.Custom;
        DisplayLayout.Clamp(Fence, display);
        _app.PreviewEdgeCollisionGesture(Fence, _resizeEdgeGesture);
        ApplyMonitorBounds();
        _resizeChanged = Math.Abs(Fence.Width - origin.Width) > .01 || Math.Abs(Fence.Height - origin.Height) > .01
            || Math.Abs(Fence.X - origin.X) > .01 || Math.Abs(Fence.Y - origin.Y) > .01 || Math.Abs(Fence.EdgeOffset - origin.EdgeOffset) > .01
            || Math.Abs(Fence.EdgeLength - origin.EdgeLength) > .01
            || Fence.EdgeSpanMode != origin.EdgeSpanMode;
        ShowResizeDimensions();
    }

    private void ShowResizeDimensions()
    {
        var display = _resizeOrigin?.Display ?? DisplayLayout.Select(Fence, AppController.Displays());
        var bounds = RevealBounds(display, false);
        _resizeSize.Text = $"{bounds.Width / display.Scale:0} × {bounds.Height / display.Scale:0}";
        _resizeSizeBadge.Visibility = Visibility.Visible;
    }

    private void CompleteResize(bool canceled)
    {
        if (_resizeOrigin is not { } origin) return;
        var changed = _resizeChanged;
        var edgeGesture = _resizeEdgeGesture;
        _resizeOrigin = null; _resizeThumb = null; _resizeEdgeGesture = null; _resizeChanged = false; _resizeSizeBadge.Visibility = Visibility.Collapsed;
        if (canceled || _closed || Fence.Locked || Fence.Tabs.Count == 0)
        {
            RestoreResizeOrigin(origin);
            _app.RestoreEdgeCollisionGesture(edgeGesture);
        }
        else if (changed)
        {
            _app.RememberPlacement(Fence);
            // Preserve this HWND when the next file/status refresh observes the resized model.
            _visualSignature = VisualSignature(Fence);
        }
        else
        {
            Fence.OpeningSize = origin.OpeningSize;
            _app.RestoreEdgeCollisionGesture(edgeGesture);
        }
        if (!_closed && Fence.Tabs.Count > 0) SetRollHeight(false);
    }

    private void RestoreResizeOrigin(ResizeOrigin origin)
    {
        Fence.X = origin.X; Fence.Y = origin.Y; Fence.Width = origin.Width; Fence.Height = origin.Height;
        Fence.EdgeLength = origin.EdgeLength; Fence.EdgeOffset = origin.EdgeOffset; Fence.OpeningSize = origin.OpeningSize;
        Fence.EdgeSpanMode = origin.EdgeSpanMode; Fence.EdgeBlockCount = origin.EdgeBlockCount; Fence.EdgeBlockIndex = origin.EdgeBlockIndex;
    }

    private void CancelResize()
    {
        if (_resizeThumb is { IsDragging: true } thumb) thumb.CancelDrag();
        CompleteResize(true);
    }
}
