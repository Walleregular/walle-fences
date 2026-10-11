using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace WalleFences.App;

internal sealed partial class FenceWindow
{
    private readonly Border _tabDropMarker = new() { Width = 2, CornerRadius = new CornerRadius(1), IsHitTestVisible = false };
    private TabSelectionMotion? _tabDropMotion;
    private readonly Dictionary<string, TabReorderMotion> _tabReorderMotions = [];
    private TabContentMotionRequest? _pendingTabContentMotion;
    private sealed record TabContentMotionRequest(string TabId, double OffsetX);
    private sealed record TabPositionSnapshot(Dictionary<string, Point> Positions, Rect Selection);


    private void InitializeTabDragMotion()
    {
        _tabDropMarker.Background = _accent;
        AutomationProperties.SetAutomationId(_tabDropMarker, "tab-drop-marker-" + Fence.Id);
        var overlay = new Canvas { IsHitTestVisible = false };
        overlay.Children.Add(_tabDropMarker); _tabSurface.Children.Add(overlay);
        _tabDropMotion = new(_tabDropMarker);
        DragLeave += (_, args) =>
        {
            var pointer = args.GetPosition(this);
            if (pointer.X < 0 || pointer.Y < 0 || pointer.X > ActualWidth || pointer.Y > ActualHeight) CancelTabDropPreview();
        };
    }

    private void RequestTabContentMotion(FenceTab next, FenceTab previous, bool animate)
    {
        _pendingTabContentMotion = null;
        if (!animate || next == previous || !SystemParameters.ClientAreaAnimation) { _tabContentMotion.Reset(); return; }
        var direction = Math.Sign(Fence.Tabs.IndexOf(next) - Fence.Tabs.IndexOf(previous));
        _pendingTabContentMotion = new(next.Id, direction * 8);
    }

    private bool TryTabInsertion(TabDrag moving, string? targetTabId, bool after, out Fence? source, out int index)
    {
        source = _app.Workspace.Fences.FirstOrDefault(fence => fence.Id == moving.FenceId);
        index = 0;
        if (_closed || Fence.ContentLocked || source == null || source.ContentLocked || !source.Tabs.Any(tab => tab.Id == moving.TabId) || !_app.Workspace.Fences.Contains(Fence)) return false;
        var target = targetTabId == null ? null : Fence.Tabs.FirstOrDefault(tab => tab.Id == targetTabId);
        if (targetTabId != null && target == null) return false;
        index = target == null ? Fence.Tabs.Count : Fence.Tabs.IndexOf(target) + (after ? 1 : 0);
        if (source == Fence && source.Tabs.FindIndex(tab => tab.Id == moving.TabId) < index) index--;
        return true;
    }

    private bool PreviewTabDrop(TabDrag moving, string? targetTabId, bool after)
    {
        if (!TryTabInsertion(moving, targetTabId, after, out var source, out var index)) { CancelTabDropPreview(); return false; }
        if (!HasVisibleTabStrip || source == Fence && source.Tabs.FindIndex(tab => tab.Id == moving.TabId) == index) { CancelTabDropPreview(); return true; }
        _tabs.UpdateLayout();
        var target = targetTabId == null ? _tabViews.GetValueOrDefault(Fence.Tabs.Last().Id) : _tabViews.GetValueOrDefault(targetTabId);
        if (target == null || target.Group.ActualWidth <= 0) { CancelTabDropPreview(); return true; }
        var bounds = new Rect(target.Group.TranslatePoint(new Point(), _tabSurface), target.Group.RenderSize);
        var x = targetTabId == null || after ? bounds.Right + 1 : bounds.Left - 2;
        _tabDropMotion!.MoveTo(new Rect(x, bounds.Top + 2, 2, Math.Max(2, bounds.Height - 4)), true);
        return true;
    }

    private void CancelTabDropPreview() => _tabDropMotion?.Hide();

    private bool CanDetachTabAt(Native.Point pointer)
    {
        if (_app.Windows.Any(window => window.IsVisible && window.ContainsPointer(pointer))) return false;
        if (_app.Manager is { IsVisible: true } manager && Native.GetWindowRect(new WindowInteropHelper(manager).Handle, out var bounds)
            && pointer.X >= bounds.Left && pointer.X < bounds.Right && pointer.Y >= bounds.Top && pointer.Y < bounds.Bottom) return false;
        return true;
    }


    private void AutoScrollTabDrag(Point pointer)
    {
        if (_tabStrip.ScrollableWidth <= 0 || pointer.Y < 0 || pointer.Y > _tabStrip.ActualHeight) return;
        if (pointer.X < 20) _tabStrip.ScrollToHorizontalOffset(_tabStrip.HorizontalOffset - 8);
        else if (pointer.X > _tabStrip.ActualWidth - 20) _tabStrip.ScrollToHorizontalOffset(_tabStrip.HorizontalOffset + 8);
    }

    private void DropTab(TabDrag moving, string? targetTabId, bool after, bool animate = true)
    {
        CancelTabDropPreview();
        if (!TryTabInsertion(moving, targetTabId, after, out var source, out var index)) return;
        MoveTabToIndex(moving, source!, index, animate);
    }

    private void MoveTabToIndex(TabDrag moving, Fence source, int index, bool animate)
    {
        WorkspaceActions.RequireEditable(source); WorkspaceActions.RequireEditable(Fence);
        if (source == Fence && source.Tabs.FindIndex(tab => tab.Id == moving.TabId) == index) return;
        var windows = _app.Windows.Where(window => window.Fence == source || window.Fence == Fence).ToArray();
        var positions = windows.ToDictionary(window => window.Fence.Id, window => window.CaptureTabPositions());
        var activeTabs = windows.ToDictionary(window => window.Fence.Id, window => window.Fence.ActiveTabId);
        foreach (var window in windows) { window.CancelTabDropPreview(); window.StopTabReorderMotion(); }
        _app.Mutate(() =>
        {
            WorkspaceActions.MoveTab(_app.Workspace, moving.FenceId, moving.TabId, Fence.Id, index);
            // Only this tab membership operation can reuse an HWND after its cached signature changes.
            // All other style, sizing and metadata edits retain the normal CanReuse checks.
            foreach (var window in windows)
            {
                window._visualSignature = VisualSignature(window.Fence);
                if (activeTabs[window.Fence.Id] != window.Fence.ActiveTabId) { window._search.Text = ""; window._tabContentMotion.Reset(); }
                var tabs = window.Fence.Tabs.Select(tab => tab.Id).ToHashSet();
                foreach (var id in window._portalLocations.Keys.Where(id => !tabs.Contains(id)).ToArray()) window._portalLocations.Remove(id);
                foreach (var id in window._history.Keys.Where(id => !tabs.Contains(id)).ToArray()) window._history.Remove(id);
            }
        });
        foreach (var window in _app.Windows.Where(window => positions.ContainsKey(window.Fence.Id)))
            window.AnimateTabPositions(positions[window.Fence.Id], animate);
    }

    private TabPositionSnapshot CaptureTabPositions()
    {
        _tabs.UpdateLayout();
        return new(_tabViews.Where(pair => pair.Value.Group.ActualWidth > 0).ToDictionary(pair => pair.Key, pair => pair.Value.Group.TranslatePoint(new Point(), _tabSurface)), _tabMotion.Bounds);
    }

    private void AnimateTabPositions(TabPositionSnapshot previous, bool animate)
    {
        if (_closed || !IsLoaded || !HasVisibleTabStrip || !animate || !SystemParameters.ClientAreaAnimation) return;
        _tabs.UpdateLayout();
        var selection = ActiveTabBounds;
        foreach (var (id, view) in _tabViews)
        {
            var final = view.Group.TranslatePoint(new Point(), _tabSurface);
            var offset = previous.Positions.TryGetValue(id, out var old) ? old - final : new Vector(8, 0);
            if (offset.Length < .5) continue;
            var motion = new TabReorderMotion(view.Group); _tabReorderMotions.Add(id, motion);
            motion.Start(offset, !previous.Positions.ContainsKey(id));
        }
        if (!previous.Selection.IsEmpty && !selection.IsEmpty)
        {
            _tabMotion.MoveTo(previous.Selection, false);
            _tabMotion.MoveTo(selection, true);
        }
    }

    private void StopTabReorderMotion()
    {
        foreach (var motion in _tabReorderMotions.Values) motion.Dispose();
        _tabReorderMotions.Clear();
    }

    private void DisposeTabDragMotion() { CancelTabDropPreview(); StopTabReorderMotion(); _pendingTabContentMotion = null; }

    private void StartTabDragFeedback(Border group)
    {
        group.BeginAnimation(OpacityProperty, null);
        if (SystemParameters.ClientAreaAnimation)
            group.BeginAnimation(OpacityProperty, new DoubleAnimation(1, .55, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else group.Opacity = .55;
    }

    private static void StopTabDragFeedback(Border group) { group.BeginAnimation(OpacityProperty, null); group.Opacity = 1; }

    private void RefreshTabMenu(TabVisual view)
    {
        view.Group.ContextMenu = TabMenu(view.Tab);
    }

    private MenuItem TabMoveMenu(string title, FenceTab tab, int direction, string icon)
    {
        var item = TabMotionMenu(title, smooth => MoveTabToIndex(new(Fence.Id, tab.Id), Fence, Math.Clamp(Fence.Tabs.IndexOf(tab) + direction, 0, Fence.Tabs.Count - 1), smooth), icon);
        var destination = Fence.Tabs.IndexOf(tab) + direction;
        item.IsEnabled = !Fence.ContentLocked && destination >= 0 && destination < Fence.Tabs.Count;
        return item;
    }

    private static MenuItem TabMotionMenu(string title, Action<bool> action, string? icon = null)
    {
        var keyboard = false;
        var item = Menu(title, () =>
        {
            var animate = !keyboard; keyboard = false;
            action(animate);
        }, icon);
        item.PreviewKeyDown += (_, args) => { if (args.Key is Key.Enter or Key.Space) keyboard = true; };
        item.PreviewMouseLeftButtonDown += (_, _) => keyboard = false;
        return item;
    }


    private sealed class TabReorderMotion : IDisposable
    {
        private readonly Border _surface;
        private readonly Transform _original;
        private readonly TranslateTransform _offset = new();
        private readonly List<AnimationClock> _clocks = [];
        private int _generation;

        internal TabReorderMotion(Border surface)
        {
            _surface = surface; _original = surface.RenderTransform;
            var transforms = new TransformGroup(); transforms.Children.Add(_original); transforms.Children.Add(_offset); surface.RenderTransform = transforms;
            surface.Unloaded += OnUnloaded;
        }
        internal void Start(Vector from, bool entering)
        {
            _offset.X = from.X; _offset.Y = from.Y;
            var generation = _generation;
            var horizontal = Animate(_offset, TranslateTransform.XProperty, from.X, 0);
            Animate(_offset, TranslateTransform.YProperty, from.Y, 0);
            if (entering) { _surface.Opacity = .7; Animate(_surface, OpacityProperty, .7, 1); }
            horizontal.Completed += (_, _) => { if (_generation == generation) Reset(); };
        }
        private AnimationClock Animate(DependencyObject target, DependencyProperty property, double from, double to)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd };
            var clock = (AnimationClock)animation.CreateClock(true); _clocks.Add(clock);
            if (target is Animatable transform) transform.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
            else ((UIElement)target).ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
            clock.Controller!.Begin(); clock.Controller.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
            return clock;
        }
        private void Reset()
        {
            _generation++;
            foreach (var clock in _clocks) clock.Controller?.Remove(); _clocks.Clear();
            _offset.ApplyAnimationClock(TranslateTransform.XProperty, null); _offset.ApplyAnimationClock(TranslateTransform.YProperty, null);
            _surface.ApplyAnimationClock(OpacityProperty, null); _offset.X = _offset.Y = 0; _surface.Opacity = 1;
        }
        private void OnUnloaded(object sender, RoutedEventArgs args) => Reset();
        public void Dispose() { Reset(); _surface.Unloaded -= OnUnloaded; _surface.RenderTransform = _original; }
    }
}
