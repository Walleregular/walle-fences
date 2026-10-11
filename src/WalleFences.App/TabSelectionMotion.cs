using System.Windows.Media.Animation;

namespace WalleFences.App;

// A single selection surface keeps its position while the surrounding buttons stay alive.
internal sealed class TabSelectionMotion
{
    private readonly Border _selection;
    private readonly TranslateTransform _position = new();
    private Rect _target = Rect.Empty;
    private bool _placed;
    private int _generation;
    internal bool IsAnimating { get; private set; }
    internal Rect Bounds => _placed ? new(_position.X, _position.Y, _selection.Width, _selection.Height) : Rect.Empty;

    internal TabSelectionMotion(Border selection)
    {
        _selection = selection;
        _selection.RenderTransform = _position;
        _selection.IsHitTestVisible = false;
        _selection.Visibility = Visibility.Collapsed;
    }

    internal void MoveTo(Rect bounds, bool animate = true)
    {
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 || !double.IsFinite(bounds.X + bounds.Y + bounds.Width + bounds.Height)) { Hide(); return; }
        var smooth = animate && SystemParameters.ClientAreaAnimation && _placed;
        if (smooth && _target == bounds) return;
        var current = Bounds;
        Stop();
        _target = bounds; _placed = true;
        _selection.Visibility = Visibility.Visible;
        if (!smooth || current.IsEmpty || current == bounds) { SetBase(bounds); return; }
        IsAnimating = true;
        var generation = _generation;
        var width = Transition(current.Width, bounds.Width);
        width.Completed += (_, _) => { if (_generation == generation) { SetBase(_target); ClearClocks(); IsAnimating = false; } };
        _position.BeginAnimation(TranslateTransform.XProperty, Transition(current.X, bounds.X));
        _position.BeginAnimation(TranslateTransform.YProperty, Transition(current.Y, bounds.Y));
        _selection.BeginAnimation(FrameworkElement.HeightProperty, Transition(current.Height, bounds.Height));
        _selection.BeginAnimation(FrameworkElement.WidthProperty, width);
    }

    internal void Hide()
    {
        Stop(); _placed = false; _target = Rect.Empty;
        _selection.Visibility = Visibility.Collapsed;
    }

    private void Stop()
    {
        var current = Bounds;
        _generation++; IsAnimating = false;
        ClearClocks();
        if (!current.IsEmpty) SetBase(current);
    }

    private void SetBase(Rect bounds)
    {
        _position.X = bounds.X; _position.Y = bounds.Y;
        _selection.Width = bounds.Width; _selection.Height = bounds.Height;
    }

    private void ClearClocks()
    {
        _position.BeginAnimation(TranslateTransform.XProperty, null);
        _position.BeginAnimation(TranslateTransform.YProperty, null);
        _selection.BeginAnimation(FrameworkElement.WidthProperty, null);
        _selection.BeginAnimation(FrameworkElement.HeightProperty, null);
    }

    private static DoubleAnimation Transition(double from, double to) => new(from, to, TimeSpan.FromMilliseconds(160))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd
    };
}
