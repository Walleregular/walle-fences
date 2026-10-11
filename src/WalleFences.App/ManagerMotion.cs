namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private readonly SurfaceMotion _pageMotion;

    // Direct navigation and keyboard actions remain immediate. Only pointer callbacks opt in.
    internal void Navigate(string id, bool animate = false)
    {
        var next = Names.ContainsKey(id) ? id : "groups";
        var transition = animate && next != CurrentSection && IsLoaded && IsVisible;
        CurrentSection = next;
        RefreshPage(preserveMotion: transition);
        _pageScroll.ScrollToTop();
        _pageMotion.Enter(transition, offsetY: 6);
    }

    private Button RouteButton(Button button, string section)
    {
        var navigate = TrackPointerNavigation(button, section);
        button.Click += (_, _) => Ui.Guard(navigate);
        return button;
    }

    private MenuItem RouteMenuItem(MenuItem item, string section)
    {
        var navigate = TrackPointerNavigation(item, section);
        item.Click += (_, _) => Ui.Guard(navigate);
        return item;
    }

    private Action TrackPointerNavigation(UIElement control, string section)
    {
        var pointer = false;
        var generation = 0;
        void Begin() { pointer = true; generation++; }
        void Clear() { pointer = false; generation++; }
        void ClearAfterInput()
        {
            var pending = generation;
            // Button releases capture before raising Click. Clear a cancelled press after that event.
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (generation == pending) Clear();
            }));
        }
        control.PreviewMouseLeftButtonDown += (_, _) => Begin();
        control.PreviewMouseLeftButtonUp += (_, _) => ClearAfterInput();
        control.PreviewTouchDown += (_, _) => Begin();
        control.PreviewTouchUp += (_, _) => ClearAfterInput();
        control.PreviewKeyDown += (_, _) => Clear();
        control.LostMouseCapture += (_, _) => ClearAfterInput();
        control.MouseLeave += (_, _) => { if (Mouse.LeftButton == MouseButtonState.Released) ClearAfterInput(); };
        return () =>
        {
            var animate = pointer;
            Clear();
            Navigate(section, animate);
        };
    }
}
