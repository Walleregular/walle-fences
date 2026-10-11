using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Path = System.Windows.Shapes.Path;

namespace WalleFences.App;

// The shade has a real hole in its geometry. The highlighted control remains the
// original, interactive control rather than a screenshot or another copy of it.
internal sealed class CoachMarkOverlay : Grid
{
    private readonly Path _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(172, 0, 0, 0)), Stretch = Stretch.None };
    private readonly Canvas _ornaments = new() { IsHitTestVisible = false };
    private readonly Path _outline = new() { Stroke = Ui.FocusAccent, StrokeThickness = 2, Stretch = Stretch.None };
    private readonly Path _connector = new() { Stroke = Ui.FocusAccent, StrokeThickness = 1.5, Opacity = .8, Stretch = Stretch.None };
    private readonly Border _badge;
    private readonly TextBlock _badgeText = Ui.Text("", 12, Ui.OnAccent, FontWeights.SemiBold);
    private readonly Canvas _cardLayer = new();
    private readonly Border _card;
    private readonly TextBlock _title = Ui.Text("", 15, Ui.Ink, FontWeights.SemiBold);
    private readonly TextBlock _description = Ui.Text("", 12);
    private readonly TextBlock _progress = Ui.Text("", 11, Ui.Muted);
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _skip;
    private readonly Button _close;
    private readonly TranslateTransform _cardTranslation = new();
    private readonly DispatcherTimer _positionTimer;
    private FrameworkElement? _target;
    private Window? _host;
    private IInputElement? _previousFocus;
    private Rect? _lastTargetBounds;
    private Size _lastViewport;
    private bool _positionDirty;
    private int _stepRevision;

    internal event Action? BackRequested;
    internal event Action? NextRequested;
    internal event Action? SkipRequested;
    internal event Action? CloseRequested;
    internal bool IsShowing { get; private set; }

    internal CoachMarkOverlay()
    {
        Visibility = Visibility.Collapsed;
        Background = null;
        Focusable = false;
        ClipToBounds = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Panel.SetZIndex(this, int.MaxValue);
        AutomationProperties.SetAutomationId(this, "Onboarding.CoachOverlay");

        _shade.PreviewMouseDown += (_, e) => e.Handled = true;
        _shade.PreviewMouseWheel += (_, e) => e.Handled = true;
        _badge = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
            Background = Ui.Accent, Child = _badgeText
        };
        _badgeText.HorizontalAlignment = HorizontalAlignment.Center;
        _badgeText.TextWrapping = TextWrapping.NoWrap;
        _ornaments.Children.Add(_connector);
        _ornaments.Children.Add(_outline);
        _ornaments.Children.Add(_badge);

        _close = Ui.IconButton("close", () => CloseRequested?.Invoke(), L.Text("Закрыть обучение"), "Onboarding.Close");
        _close.Width = _close.Height = 28;
        _close.Padding = new Thickness(4);
        _close.Margin = new Thickness(8, -4, -4, 0);
        _back = Ui.Button(L.Text("Назад"), () => BackRequested?.Invoke(), id: "Onboarding.Back");
        _next = Ui.Button(L.Text("Далее"), () => NextRequested?.Invoke(), true, "Onboarding.Next");
        _skip = Ui.Button(L.Text("Пропустить"), () => SkipRequested?.Invoke(), id: "Onboarding.Skip");
        _back.Margin = new Thickness(0);
        _next.Margin = new Thickness(8, 0, 0, 0);
        _back.Padding = _next.Padding = new Thickness(12, 6, 12, 6);
        _back.MinHeight = _next.MinHeight = 32;
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        _next.HorizontalAlignment = HorizontalAlignment.Right;
        _skip.Margin = new Thickness(0, 12, 0, 0);
        _skip.Padding = new Thickness(0, 2, 0, 2);
        _skip.MinHeight = 24;
        _skip.HorizontalAlignment = HorizontalAlignment.Left;
        _skip.Background = Brushes.Transparent;
        _skip.BorderThickness = new Thickness(0);
        _skip.Foreground = Ui.Muted;

        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(_progress);
        Grid.SetColumn(_close, 1);
        heading.Children.Add(_close);
        var navigation = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        navigation.ColumnDefinitions.Add(new ColumnDefinition());
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigation.Children.Add(_back);
        Grid.SetColumn(_next, 1);
        navigation.Children.Add(_next);
        _title.Margin = new Thickness(0, 8, 0, 8);
        _description.LineHeight = 19;
        var content = new StackPanel();
        content.Children.Add(heading);
        content.Children.Add(_title);
        content.Children.Add(_description);
        content.Children.Add(navigation);
        content.Children.Add(_skip);
        _card = new Border
        {
            Width = 320, Background = Ui.Surface, BorderBrush = Ui.Line,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18), RenderTransform = _cardTranslation,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 4, Opacity = .24 },
            Child = new ScrollViewer
            {
                Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
                PanningMode = PanningMode.VerticalOnly
            }
        };
        KeyboardNavigation.SetTabNavigation(_card, KeyboardNavigationMode.Continue);
        AutomationProperties.SetAutomationId(_card, "Onboarding.CoachCard");
        AutomationProperties.SetLiveSetting(_title, AutomationLiveSetting.Polite);
        _cardLayer.Children.Add(_card);
        Children.Add(_shade);
        Children.Add(_ornaments);
        Children.Add(_cardLayer);
        _positionTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(40) };
        _positionTimer.Tick += (_, _) => { _positionTimer.Stop(); PositionCard(); };
        Loaded += (_, _) => { if (IsShowing) { AttachHost(); QueuePosition(); } };
        Unloaded += (_, _) => HideGuide();
    }

    internal void ShowStep(FrameworkElement? target, string title, string description, string progress, bool canGoBack, string nextLabel, bool animate)
    {
        if (!IsShowing)
        {
            _previousFocus = Keyboard.FocusedElement;
            IsShowing = true;
            LayoutUpdated += OnLayoutUpdated;
            SizeChanged += OnSizeChanged;
        }
        var revision = ++_stepRevision;
        _target = target;
        _title.Text = title;
        _description.Text = description;
        _progress.Text = progress;
        _badgeText.Text = new string(progress.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        _back.IsEnabled = canGoBack;
        _next.Content = nextLabel;
        _back.Content = L.Text("Назад");
        _skip.Content = L.Text("Пропустить");
        _card.Background = Ui.Surface;
        _card.BorderBrush = Ui.Line;
        _outline.Stroke = _connector.Stroke = Ui.FocusAccent;
        _badge.Background = Ui.Accent;
        _badgeText.Foreground = Ui.OnAccent;
        _title.Foreground = _description.Foreground = Ui.Ink;
        _progress.Foreground = _skip.Foreground = Ui.Muted;
        _back.Foreground = Ui.Ink;
        _next.Background = _next.BorderBrush = Ui.Accent;
        _next.Foreground = Ui.OnAccent;
        _close.Content = Brand.Icon("close", Ui.Ink);
        _close.ToolTip = L.Text("Закрыть обучение");
        AutomationProperties.SetName(_back, L.Text("Назад"));
        AutomationProperties.SetName(_skip, L.Text("Пропустить"));
        AutomationProperties.SetName(_close, L.Text("Закрыть обучение"));
        AutomationProperties.SetName(_card, title);
        AutomationProperties.SetHelpText(_card, description);
        AutomationProperties.SetName(_title, $"{progress}. {title}. {description}");
        AutomationProperties.SetName(_next, nextLabel);
        AutomationProperties.SetName(_badge, progress);
        Visibility = Visibility.Visible;
        AttachHost();
        _positionDirty = true;
        if (target is { IsLoaded: true, IsVisible: true }) target.BringIntoView();
        PositionCard();
        QueuePosition();
        BeginEntrance(animate);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!IsShowing || revision != _stepRevision) return;
            PositionCard();
            if (!IsWithin(Keyboard.FocusedElement as DependencyObject, _card)
                && !IsWithin(Keyboard.FocusedElement as DependencyObject, _target)) _next.Focus();
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.CreatePeerForElement(_title)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }));
    }

    internal void HideGuide()
    {
        if (!IsShowing) return;
        var restoreFocus = IsWithin(Keyboard.FocusedElement as DependencyObject, _card);
        IsShowing = false;
        ++_stepRevision;
        _positionTimer.Stop();
        LayoutUpdated -= OnLayoutUpdated;
        SizeChanged -= OnSizeChanged;
        if (_host != null)
        {
            _host.PreviewKeyDown -= OnHostKeyDown;
            _host.Closed -= OnHostClosed;
            _host = null;
        }
        _card.BeginAnimation(OpacityProperty, null);
        _cardTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        _card.Opacity = 1;
        _cardTranslation.Y = 0;
        Visibility = Visibility.Collapsed;
        _target = null;
        _lastTargetBounds = null;
        _shade.Data = _outline.Data = _connector.Data = null;
        var focus = _previousFocus;
        _previousFocus = null;
        if (restoreFocus && focus is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } element
            && PresentationSource.FromVisual(element) != null) element.Focus();
    }

    private void AttachHost()
    {
        var host = Window.GetWindow(this);
        if (host == _host) return;
        if (_host != null)
        {
            _host.PreviewKeyDown -= OnHostKeyDown;
            _host.Closed -= OnHostClosed;
        }
        _host = host;
        if (_host != null)
        {
            _host.PreviewKeyDown += OnHostKeyDown;
            _host.Closed += OnHostClosed;
        }
    }

    private void OnHostClosed(object? sender, EventArgs e) => HideGuide();
    private void OnLayoutUpdated(object? sender, EventArgs e) => QueuePosition();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => QueuePosition();
    private void QueuePosition() { if (IsShowing && !_positionTimer.IsEnabled) _positionTimer.Start(); }

    private void OnHostKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsShowing) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            SkipRequested?.Invoke();
            return;
        }
        if (e.Key != Key.Tab || (Keyboard.Modifiers & ~ModifierKeys.Shift) != ModifierKeys.None) return;
        // Keyboard navigation covers the real target as well as the guide. It
        // never activates a dimmed control elsewhere in the settings window.
        var controls = FocusableChildren(_target).Concat(FocusableChildren(_card)).Distinct().ToList();
        if (controls.Count == 0) return;
        var current = controls.FindIndex(control => control == Keyboard.FocusedElement);
        if (current < 0) current = controls.FindLastIndex(control => control.IsKeyboardFocusWithin);
        var backwards = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var next = current < 0 ? (backwards ? controls.Count - 1 : 0)
            : (current + (backwards ? -1 : 1) + controls.Count) % controls.Count;
        e.Handled = true;
        controls[next].Focus();
    }

    private static IEnumerable<UIElement> FocusableChildren(DependencyObject? root)
    {
        if (root is not Visual visual) yield break;
        if (root is UIElement element)
        {
            if (!element.IsVisible || !element.IsEnabled) yield break;
            if (element.Focusable && KeyboardNavigation.GetIsTabStop(element)) yield return element;
        }
        var children = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(visual))
            .Select(index => VisualTreeHelper.GetChild(visual, index)).OrderBy(KeyboardNavigation.GetTabIndex);
        foreach (var child in children)
            foreach (var control in FocusableChildren(child)) yield return control;
    }

    private static bool IsWithin(DependencyObject? child, DependencyObject? parent)
    {
        if (parent == null) return false;
        while (child != null)
        {
            if (child == parent) return true;
            child = child is Visual ? VisualTreeHelper.GetParent(child) : LogicalTreeHelper.GetParent(child);
        }
        return false;
    }

    private Rect? VisibleTargetBounds()
    {
        if (_target is not { IsLoaded: true, IsVisible: true } target
            || target.RenderSize.Width < 1 || target.RenderSize.Height < 1
            || PresentationSource.FromVisual(target) == null) return null;
        try
        {
            var bounds = target.TransformToVisual(this).TransformBounds(new Rect(target.RenderSize));
            for (var ancestor = VisualTreeHelper.GetParent(target); ancestor is Visual; ancestor = VisualTreeHelper.GetParent(ancestor))
            {
                if (ancestor is not FrameworkElement element) continue;
                if (element.ClipToBounds || element is ScrollContentPresenter)
                    bounds.Intersect(element.TransformToVisual(this).TransformBounds(new Rect(element.RenderSize)));
                if (element.Clip != null)
                    bounds.Intersect(element.TransformToVisual(this).TransformBounds(element.Clip.Bounds));
            }
            bounds.Intersect(new Rect(0, 0, ActualWidth, ActualHeight));
            return bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1 ? null : bounds;
        }
        catch (InvalidOperationException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private void PositionCard()
    {
        if (!IsShowing || ActualWidth < 1 || ActualHeight < 1) return;
        var viewport = new Size(ActualWidth, ActualHeight);
        var target = VisibleTargetBounds();
        if (!_positionDirty && viewport == _lastViewport && SameBounds(target, _lastTargetBounds)) return;
        _positionDirty = false;
        _lastViewport = viewport;
        _lastTargetBounds = target;
        const double inset = 14, gap = 18;
        _card.Width = Math.Min(320, Math.Max(1, viewport.Width - inset * 2));
        _card.MaxHeight = Math.Max(1, viewport.Height - inset * 2);
        _card.Measure(new Size(_card.Width, _card.MaxHeight));
        var size = new Size(_card.Width, Math.Min(_card.DesiredSize.Height, _card.MaxHeight));
        Rect Constrain(double x, double y) => new(
            Math.Max(0, Math.Min(Math.Max(inset, x), viewport.Width - size.Width - inset)),
            Math.Max(0, Math.Min(Math.Max(inset, y), viewport.Height - size.Height - inset)), size.Width, size.Height);
        Rect placement;
        if (target is Rect anchor)
        {
            var candidates = new[]
            {
                Constrain(anchor.Right + gap, anchor.Top + (anchor.Height - size.Height) / 2),
                Constrain(anchor.Left + (anchor.Width - size.Width) / 2, anchor.Bottom + gap),
                Constrain(anchor.Left - size.Width - gap, anchor.Top + (anchor.Height - size.Height) / 2),
                Constrain(anchor.Left + (anchor.Width - size.Width) / 2, anchor.Top - size.Height - gap)
            };
            placement = candidates.Select((rect, index) =>
            {
                var intersection = Rect.Intersect(rect, anchor);
                var overlap = intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
                return (rect, score: overlap * 1000 + index);
            }).OrderBy(candidate => candidate.score).First().rect;
        }
        else placement = Constrain((viewport.Width - size.Width) / 2, (viewport.Height - size.Height) / 2);
        Canvas.SetLeft(_card, placement.Left);
        Canvas.SetTop(_card, placement.Top);

        var shade = new GeometryGroup { FillRule = FillRule.EvenOdd };
        shade.Children.Add(new RectangleGeometry(new Rect(viewport)));
        _outline.Visibility = _badge.Visibility = _connector.Visibility = target == null ? Visibility.Collapsed : Visibility.Visible;
        if (target is Rect highlight)
        {
            highlight.Inflate(5, 5);
            highlight.Intersect(new Rect(viewport));
            shade.Children.Add(new RectangleGeometry(highlight));
            _outline.Data = new RectangleGeometry(highlight, 8, 8);
            PositionBadge(highlight, placement, viewport);
            DrawConnector(highlight, placement);
        }
        else _outline.Data = _connector.Data = null;
        _shade.Data = shade;
    }

    private static bool SameBounds(Rect? a, Rect? b) => a == null || b == null ? a == b
        : Math.Abs(a.Value.X - b.Value.X) < .5 && Math.Abs(a.Value.Y - b.Value.Y) < .5
            && Math.Abs(a.Value.Width - b.Value.Width) < .5 && Math.Abs(a.Value.Height - b.Value.Height) < .5;

    private void PositionBadge(Rect target, Rect card, Size viewport)
    {
        var candidates = new[]
        {
            new Rect(target.Left, target.Top - 36, 28, 28),
            new Rect(target.Right + 8, target.Top, 28, 28),
            new Rect(target.Left - 36, target.Top, 28, 28),
            new Rect(target.Left, target.Bottom + 8, 28, 28)
        };
        var placement = candidates.FirstOrDefault(rect => new Rect(viewport).Contains(rect) && !rect.IntersectsWith(card));
        if (placement == default || string.IsNullOrEmpty(_badgeText.Text)) { _badge.Visibility = Visibility.Collapsed; return; }
        Canvas.SetLeft(_badge, placement.Left);
        Canvas.SetTop(_badge, placement.Top);
    }

    private void DrawConnector(Rect target, Rect card)
    {
        if (target.IntersectsWith(card)) { _connector.Visibility = Visibility.Collapsed; return; }
        double Along(double value, double minimum, double maximum) => Math.Clamp(value, minimum, Math.Max(minimum, maximum));
        Point start, end;
        if (card.Left >= target.Right)
        {
            start = new(card.Left, Along(target.Top + target.Height / 2, card.Top + 16, card.Bottom - 16));
            end = new(target.Right, Along(start.Y, target.Top, target.Bottom));
        }
        else if (card.Right <= target.Left)
        {
            start = new(card.Right, Along(target.Top + target.Height / 2, card.Top + 16, card.Bottom - 16));
            end = new(target.Left, Along(start.Y, target.Top, target.Bottom));
        }
        else if (card.Top >= target.Bottom)
        {
            start = new(Along(target.Left + target.Width / 2, card.Left + 16, card.Right - 16), card.Top);
            end = new(Along(start.X, target.Left, target.Right), target.Bottom);
        }
        else
        {
            start = new(Along(target.Left + target.Width / 2, card.Left + 16, card.Right - 16), card.Bottom);
            end = new(Along(start.X, target.Left, target.Right), target.Top);
        }
        var direction = start - end;
        if (direction.Length < 8) { _connector.Visibility = Visibility.Collapsed; return; }
        direction.Normalize();
        var perpendicular = new Vector(-direction.Y, direction.X);
        var arrow = new GeometryGroup();
        arrow.Children.Add(new LineGeometry(start, end));
        arrow.Children.Add(new LineGeometry(end, end + direction * 6 + perpendicular * 3));
        arrow.Children.Add(new LineGeometry(end, end + direction * 6 - perpendicular * 3));
        _connector.Data = arrow;
    }

    private void BeginEntrance(bool animate)
    {
        _card.BeginAnimation(OpacityProperty, null);
        _cardTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        _card.Opacity = 1;
        _cardTranslation.Y = 0;
        if (!animate || !SystemParameters.ClientAreaAnimation) return;
        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
        _cardTranslation.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(5, 0, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
    }
}
