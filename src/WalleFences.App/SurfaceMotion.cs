using System.Windows.Media.Animation;

namespace WalleFences.App;

// Translates an existing surface without changing its measured size or text rasterization.
internal sealed class SurfaceMotion : IDisposable
{
    internal const int DurationMilliseconds = 180;
    private readonly FrameworkElement _surface;
    private readonly TranslateTransform _offset = new();
    private readonly Transform _originalTransform;
    private readonly double _restOpacity;
    private readonly List<AnimationClock> _clocks = [];
    private int _generation;
    private bool _disposed;
    internal bool IsAnimating { get; private set; }

    internal SurfaceMotion(FrameworkElement surface)
    {
        _surface = surface;
        _restOpacity = surface.Opacity;
        _originalTransform = surface.RenderTransform;
        var transforms = new TransformGroup();
        transforms.Children.Add(_originalTransform);
        transforms.Children.Add(_offset);
        surface.RenderTransform = transforms;
        surface.Unloaded += OnUnloaded;
    }

    internal void Enter(bool animate = true, double offsetX = 0, double offsetY = 6)
    {
        if (_disposed) return;
        var continuing = IsAnimating;
        var x = continuing ? _offset.X : offsetX;
        var y = continuing ? _offset.Y : offsetY;
        var opacity = continuing ? _surface.Opacity : _restOpacity * .82;
        Reset();
        if (!animate || !SystemParameters.ClientAreaAnimation || !_surface.IsLoaded || !_surface.IsVisible) return;
        if (!double.IsFinite(x + y + opacity)) return;
        _offset.X = x; _offset.Y = y; _surface.Opacity = opacity;
        IsAnimating = true;
        var generation = _generation;
        Animate(_offset, TranslateTransform.XProperty, x, 0);
        Animate(_offset, TranslateTransform.YProperty, y, 0);
        Animate(_surface, UIElement.OpacityProperty, opacity, _restOpacity,
            () => { if (_generation == generation) Reset(); });
    }

    internal void Reset()
    {
        _generation++; IsAnimating = false;
        foreach (var clock in _clocks) clock.Controller?.Remove();
        _clocks.Clear();
        _offset.ApplyAnimationClock(TranslateTransform.XProperty, null);
        _offset.ApplyAnimationClock(TranslateTransform.YProperty, null);
        _surface.ApplyAnimationClock(UIElement.OpacityProperty, null);
        _offset.X = _offset.Y = 0;
        _surface.Opacity = _restOpacity;
    }

    private void Animate(IAnimatable target, DependencyProperty property, double from, double to, Action? completed = null)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(DurationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd
        };
        var clock = (AnimationClock)animation.CreateClock(true);
        if (completed != null) clock.Completed += (_, _) => completed();
        _clocks.Add(clock);
        target.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
        clock.Controller!.Begin();
        clock.Controller.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Reset();

    public void Dispose()
    {
        if (_disposed) return;
        Reset(); _disposed = true;
        _surface.Unloaded -= OnUnloaded;
        _surface.RenderTransform = _originalTransform;
    }
}
