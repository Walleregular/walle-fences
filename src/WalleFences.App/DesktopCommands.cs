namespace WalleFences.App;

internal sealed partial class AppController
{
    private readonly Dictionary<int, Action> _hotkeyActions = [];
    private string? _hotkeySignature;
    private SearchWindow? _searchWindow;
    private bool _capturingHotkey;
    private readonly DispatcherTimer _focusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private List<(DesktopWidget Widget, string Title)> _focusWidgets = [];
    private void RefreshFocusWidgets()
    {
        _focusWidgets = Workspace.Fences.SelectMany(f => f.Tabs.SelectMany(t => t.Widgets.Where(w => w.Kind == WidgetKind.Focus).Select(w => (w, string.IsNullOrWhiteSpace(w.Title) ? f.Name : w.Title)))).ToList();
    }
    internal void TickFocusTimers(DateTimeOffset now)
    {
        foreach (var (widget, title) in _focusWidgets)
            if (widget.FocusEndsAt != null && FocusClock.Remaining(widget, now) == 0)
            { widget.FocusEndsAt = null; widget.RemainingSeconds = 0; ScheduleSave(); NotifyFocus(title); }
    }
    internal void SuspendHotkeys(bool capture)
    {
        _capturingHotkey = capture;
        if (_messages == null) return;
        if (capture) { foreach (var id in _hotkeyActions.Keys) Native.UnregisterHotKey(_messages.Handle, id); _hotkeyActions.Clear(); }
        else { _hotkeySignature = null; ConfigureHotkeys(); }
    }
    internal IReadOnlyList<string> HotkeyConflicts { get; private set; } = [];
    internal void ShowSearch()
    {
        if (_searchWindow == null) { _searchWindow = new(this); _searchWindow.Closed += (_, _) => _searchWindow = null; }
        _searchWindow.Show(); _searchWindow.Activate(); _searchWindow.FocusSearch();
    }
    internal void OpenGroup(Fence fence, FenceTab? tab = null)
    {
        if (Workspace.ActivePageId != fence.PageId) SwitchPage(fence.PageId);
        if (Hidden) ToggleHidden();
        var window = Windows.FirstOrDefault(w => w.Fence.Id == fence.Id);
        if (window == null) return;
        window.ActivateTab(tab ?? fence.ActiveTab, false);
        window.Activate(); window.FocusFiles();
    }
    private void ToggleGroupByHotkey(Fence fence)
    {
        var window = Windows.FirstOrDefault(w => w.Fence.Id == fence.Id);
        if (fence.RevealTrigger == RevealTrigger.Hotkey && Workspace.ActivePageId == fence.PageId && !Hidden && !IsPeek && window is { IsVisible: true, VisuallyCollapsed: false })
        {
            window.ToggleRoll();
            return;
        }
        OpenGroup(fence);
    }
    internal void NotifyFocus(string title)
    {
        SetStatus(L.Text("Таймер завершён") + " · " + title);
        _tray?.ShowBalloonTip(5000, "Walle Fences", L.Text("Таймер завершён") + " · " + title, System.Windows.Forms.ToolTipIcon.Info);
    }
    internal void CreateSidebar(DockEdge edge, DisplayArea display, string name)
    {
        var fence = new Fence { Name = name, PageId = Workspace.ActivePageId, IsSidebar = true, Edge = edge,
            EdgeLength = 120, EdgeThickness = 3, Layer = FenceLayer.AlwaysOnTop, DisplayMode = FenceDisplayMode.Compact,
            FileView = FileView.List, HoverExpand = true, Color = "#1D2021", Accent = Workspace.Preferences.InterfaceAccent,
            Opacity = .97, CloseDelayMs = 700, IconSize = 28, CornerRadius = 12 };
        OpeningSizes.Apply(fence, OpeningSize.Sidebar, display);
        var offset = 16.0;
        var occupied = Workspace.Fences.Where(f => f.PageId == fence.PageId && f.Edge == edge && DisplayLayout.Select(f, Displays()).Id == display.Id)
            .Select(f => EdgeLayout.Bounds(f, display, false)).OrderBy(r => edge is DockEdge.Left or DockEdge.Right ? r.Y : r.X);
        foreach (var strip in occupied)
        {
            var start = (edge is DockEdge.Left or DockEdge.Right ? strip.Y - display.Y : strip.X - display.X) / display.Scale;
            var length = (edge is DockEdge.Left or DockEdge.Right ? strip.Height : strip.Width) / display.Scale;
            if (offset + fence.EdgeLength + 8 <= start) break;
            offset = Math.Max(offset, start + length + 8);
        }
        var available = (edge is DockEdge.Left or DockEdge.Right ? display.Height : display.Width) / display.Scale;
        if (offset + fence.EdgeLength > available - 16) throw new InvalidOperationException(L.Text("На этом краю нет места для панели. Выберите другой край или монитор."));
        fence.EdgeOffset = offset;
        fence.Tabs[0].Name = L.Text("Быстрый доступ");
        Mutate(() => Workspace.Fences.Add(fence));
    }
}
