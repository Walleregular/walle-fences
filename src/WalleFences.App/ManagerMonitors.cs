using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Shapes;

namespace WalleFences.App;

internal sealed record DesktopGroupPreview(string FenceId, Rect PhysicalBounds, bool ContentVisible, Visual? LiveVisual, IReadOnlyList<FileEntry> Items);
internal sealed record DesktopPreviewGeometry(string FenceId, Rect PhysicalBounds, bool Visible, bool ContentVisible, string TabId, Visual? LiveVisual, int ItemsCount);

internal sealed partial class ManagerWindow
{
    private readonly DispatcherTimer _desktopPreviewTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
    private DesktopPreviewGeometry[] _desktopPreviewGeometry = [];
    private void InitializeDesktopPreviewSync()
    {
        _desktopPreviewTimer.Tick += (_, _) => RefreshDesktopPreviewGeometry();
        IsVisibleChanged += (_, _) => UpdateDesktopPreviewTimer();
        StateChanged += (_, _) => UpdateDesktopPreviewTimer();
        Closed += (_, _) => { _desktopPreviewTimer.Stop(); _desktopPreviewGeometry = []; };
        UpdateDesktopPreviewTimer();
    }
    private void UpdateDesktopPreviewTimer()
    {
        if (IsVisible && WindowState != WindowState.Minimized && CurrentSection is "groups" or "monitors") _desktopPreviewTimer.Start(); else _desktopPreviewTimer.Stop();
    }
    private DesktopPreviewGeometry[] DesktopPreviewGeometry()
    {
        var displays = AppController.Displays();
        return _app.Workspace.Fences.Where(f => f.PageId == _app.Workspace.ActivePageId).Select(f =>
        {
            var window = _app.Windows.FirstOrDefault(w => w.Fence.Id == f.Id);
            var visible = (!_app.Hidden || _app.IsPeek || f.KeepVisibleOnHide) && window is not { IsVisible: false };
            return new DesktopPreviewGeometry(f.Id, ProjectionBounds(f, DisplayLayout.Select(f, displays), window), visible, window?.IsContentVisible ?? !f.ContentCollapsed, f.ActiveTab.Id, window?.Content as Visual, window?.PreviewEntries.Count ?? 0);
        }).ToArray();
    }
    internal void RefreshDesktopPreviewGeometry()
    {
        if (!IsVisible || WindowState == WindowState.Minimized || CurrentSection is not ("groups" or "monitors") || _desktopMonitors == null) return;
        var geometry = DesktopPreviewGeometry();
        if (_desktopPreviewGeometry.SequenceEqual(geometry)) return;
        // Only geometry/state reads occur on the timer: no COM, file access or decoding.
        _desktopPreviewGeometry = geometry; _desktopMonitors.Child = MonitorPreviews(false);
    }
    private static Rect ProjectionBounds(Fence fence, DisplayArea display, FenceWindow? window)
    {
        if (window != null)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != 0 && Native.GetWindowRect(handle, out var rect) && rect.Right > rect.Left && rect.Bottom > rect.Top) return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        var expanded = window?.IsContentVisible ?? !fence.ContentCollapsed;
        var bounds = fence.Edge == DockEdge.None ? new LayoutRect(fence.X, fence.Y, fence.Width * display.Scale, (expanded ? fence.Height : FenceLayout.CollapsedHeight(fence)) * display.Scale) : EdgeLayout.Bounds(fence, display, expanded);
        return new(bounds.X / AppController.GlobalScale, bounds.Y / AppController.GlobalScale, bounds.Width / AppController.GlobalScale, bounds.Height / AppController.GlobalScale);
    }
    private UIElement MonitorPreviews(bool refreshWallpaper = true)
    {
        if (refreshWallpaper) _ = DesktopWallpaper.RefreshAsync();
        var displays = AppController.Displays(); var snapshot = DesktopWallpaper.Current;
        if (!displays.Any(d => d.Id == _selectedMonitorId)) _selectedMonitorId = displays.FirstOrDefault(d => d.Primary)?.Id ?? displays.FirstOrDefault()?.Id;
        var previews = new UniformGrid { Columns = Math.Min(2, Math.Max(1, displays.Length)) };
        foreach (var (display, index) in displays.Select((display, index) => (display, index)))
        {
            var selected = display.Id == _selectedMonitorId;
            var tile = new StackPanel { Margin = new Thickness(3, 0, 8, 0) };
            var preview = Ui.Button("", () =>
            {
                _selectedMonitorId = display.Id;
                if (CurrentSection == "groups") RefreshDesktopVisuals(); else Refresh();
            }, id: "monitor-preview-" + display.Id);
            preview.Padding = new Thickness(8); preview.Margin = new Thickness(0); preview.Background = Ui.Surface; preview.BorderThickness = new Thickness(selected ? 1.5 : 1); preview.BorderBrush = selected ? Ui.Accent : Ui.Line; preview.HorizontalContentAlignment = HorizontalAlignment.Stretch; preview.ToolTip = display.Name; AutomationProperties.SetName(preview, L.Format("Экран {0} · {1:0}%", index + 1, display.Scale / AppController.GlobalScale * 100));
            preview.Content = new Viewbox { Child = MonitorCanvas(display, snapshot), Stretch = Stretch.Uniform, MaxHeight = CurrentSection == "groups" ? 140 : 170, HorizontalAlignment = HorizontalAlignment.Stretch }; tile.Children.Add(preview);
            var caption = Ui.Text(L.Format("Экран {0} · {1:0}%", index + 1, display.Scale / AppController.GlobalScale * 100), 10, Ui.Muted); caption.HorizontalAlignment = HorizontalAlignment.Center; caption.Margin = new Thickness(0, 5, 0, 9); tile.Children.Add(caption); previews.Children.Add(tile);
        }
        _desktopPreviewGeometry = DesktopPreviewGeometry(); return previews;
    }
    private Canvas MonitorCanvas(DisplayArea display, WallpaperSnapshot snapshot)
    {
        const double width = 270;
        var monitor = DesktopWallpaper.SelectMonitor(snapshot, display, AppController.GlobalScale);
        var full = monitor.Bounds; var height = width * full.Height / Math.Max(1, full.Width);
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };
        var background = WallpaperBackground(snapshot, monitor, width, height); AutomationProperties.SetAutomationId(background, "monitor-wallpaper-" + display.Id); background.Tag = monitor; canvas.Children.Add(background);
        var sx = width / Math.Max(1, full.Width); var sy = height / Math.Max(1, full.Height);
        var displays = AppController.Displays();
        foreach (var fence in _app.Workspace.Fences.Where(f => f.PageId == _app.Workspace.ActivePageId))
        {
            var window = _app.Windows.FirstOrDefault(w => w.Fence.Id == fence.Id);
            if (_app.Hidden && !_app.IsPeek && !fence.KeepVisibleOnHide || window is { IsVisible: false }) continue;
            var expanded = window?.IsContentVisible ?? !fence.ContentCollapsed;
            var assignedDisplay = DisplayLayout.Select(fence, displays);
            var physical = ProjectionBounds(fence, assignedDisplay, window);
            var intersection = Rect.Intersect(physical, full);
            if (intersection.IsEmpty || intersection.Width <= 0 || intersection.Height <= 0) continue;
            var visual = window?.Content as Visual;
            var projection = new Border { Width = physical.Width * sx, Height = physical.Height * sy, Background = visual != null ? new VisualBrush(visual) { Stretch = Stretch.Fill } : Ui.Brush(fence.Color, fence.Opacity), BorderBrush = fence.Id == _selectedFenceId ? Ui.FocusAccent : Brushes.Transparent, BorderThickness = new Thickness(fence.Id == _selectedFenceId ? .6 : 0), Cursor = Cursors.Hand, Tag = new DesktopGroupPreview(fence.Id, physical, expanded, visual, window?.PreviewEntries ?? []) };
            AutomationProperties.SetAutomationId(projection, "monitor-projection-" + fence.Id + (assignedDisplay.Id == display.Id ? "" : "-on-" + display.Id)); AutomationProperties.SetName(projection, fence.Name); projection.ToolTip = fence.Name;
            projection.MouseLeftButtonDown += (_, e) => { SelectGroup(fence.Id); e.Handled = true; };
            Canvas.SetLeft(projection, (physical.X - full.X) * sx); Canvas.SetTop(projection, (physical.Y - full.Y) * sy); canvas.Children.Add(projection);
        }
        return canvas;
    }
    private static Border WallpaperBackground(WallpaperSnapshot snapshot, WallpaperMonitor monitor, double width, double height)
    {
        var background = new Border { Width = width, Height = height, Background = new SolidColorBrush(snapshot.Background), ClipToBounds = true };
        if (monitor.Image is { } image)
        {
            if (snapshot.Source == "WallpaperEngine" && monitor.Position == WallpaperPosition.Stretch) background.ToolTip = L.Text("Предпросмотр выбранных обоев Wallpaper Engine");
            var sx = width / monitor.Bounds.Width; var sy = height / monitor.Bounds.Height;
            var position = monitor.Position ?? snapshot.Position;
            if (position == WallpaperPosition.Tile)
            {
                background.Background = new ImageBrush(image.Source) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect((-monitor.Bounds.X % image.PixelWidth) * sx, (-monitor.Bounds.Y % image.PixelHeight) * sy, image.PixelWidth * sx, image.PixelHeight * sy), Stretch = Stretch.Fill };
            }
            else
            {
                var area = position == WallpaperPosition.Span ? snapshot.VirtualBounds : monitor.Bounds;
                var rectangle = DesktopWallpaper.ImageRectangle(position, image.PixelWidth, image.PixelHeight, area);
                var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };
                var picture = new Image { Source = image.Source, Width = rectangle.Width * sx, Height = rectangle.Height * sy, Stretch = Stretch.Fill };
                AutomationProperties.SetAutomationId(picture, "wallpaper-image-" + monitor.Id); Canvas.SetLeft(picture, (rectangle.X - monitor.Bounds.X) * sx); Canvas.SetTop(picture, (rectangle.Y - monitor.Bounds.Y) * sy); canvas.Children.Add(picture); background.Child = canvas;
            }
        }
        else if (snapshot.Loading || snapshot.Error != null || monitor.Path != null)
        {
            var hint = Ui.Text(L.Text(snapshot.Loading ? "Загрузка фона…" : "Windows не передаёт изображение фона"), 9, Ui.Muted); hint.TextAlignment = TextAlignment.Center; hint.Margin = new Thickness(12); background.Child = hint; background.ToolTip = L.Text("Динамические обои могут не предоставлять изображение через Windows.");
        }
        return background;
    }
    private void WallpaperChanged()
    {
        if (CurrentSection == "groups" && _desktopMonitors != null) _desktopMonitors.Child = MonitorPreviews();
        else if (CurrentSection == "monitors") Refresh();
    }
    private void Monitors()
    {
        Intro(L.Text("Размещайте группы на подключённых экранах."));
        _desktopMonitors = new Border { Child = MonitorPreviews() }; _desktopRows = null;
        _page.Children.Add(_desktopMonitors);
        _page.Children.Add(Ui.Button(L.Text("Обновить рабочие столы"), () => { DesktopWallpaper.Invalidate(); Refresh(); }, id: "refresh-desktop-preview"));
        var displays = AppController.Displays(); var selected = displays.FirstOrDefault(d => d.Id == _selectedMonitorId) ?? displays[0];
        var name = Ui.Text(selected.Name, 17, Ui.Ink, FontWeights.SemiBold); name.Margin = new Thickness(0, 8, 0, 3); _page.Children.Add(name);
        _page.Children.Add(Ui.Text(L.Format("Рабочая область: {0:0} × {1:0} · масштаб {2:0}%", selected.Width / AppController.GlobalScale, selected.Height / AppController.GlobalScale, selected.Scale / AppController.GlobalScale * 100), 11, Ui.Muted));
        var select = new ComboBox { ItemsSource = _app.Workspace.Fences.Where(f => f.PageId == _app.Workspace.ActivePageId).ToList(), DisplayMemberPath = nameof(Fence.Name), ItemTemplate = null, Margin = new Thickness(0, 14, 0, 10), MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(select, "monitor-group-selector"); AutomationProperties.SetName(select, L.Text("Группа"));
        if (SelectedFence != null) select.SelectedItem = SelectedFence; else if (select.Items.Count > 0) select.SelectedIndex = 0;
        select.SelectionChanged += (_, _) => { if (select.SelectedItem is Fence fence) _selectedFenceId = fence.Id; }; _page.Children.Add(select);
        var move = Ui.Button(L.Text("Перенести выбранную группу на этот экран"), () => { if (select.SelectedItem is Fence fence) _app.MoveToMonitor(fence, selected); }, true, "monitor-move-selected"); move.IsEnabled = select.Items.Count > 0; _page.Children.Add(move);
        var prefs = _app.Workspace.Preferences;
        var restore = Ui.Toggle(L.Text("Возвращать группы на прежний монитор после подключения"), prefs.RestoreMonitorPositions, value => _app.Mutate(() => _app.Workspace.Preferences.RestoreMonitorPositions = value), "monitor-restore-positions"); restore.Margin = new Thickness(0, 16, 0, 12); _page.Children.Add(restore);
        _page.Children.Add(Ui.Button(L.Text("Настройки экранов Windows…"), Native.OpenDisplaySettings, id: "monitors-display-settings"));
        var disconnected = _app.Workspace.Fences.Where(f => f.Placement != null && !displays.Any(d => d.Id.Equals(f.Placement.MonitorId, StringComparison.OrdinalIgnoreCase))).ToList();
        if (disconnected.Count > 0)
        {
            var hint = Ui.Text(L.Text("Некоторые группы временно показаны на основном экране. Их сохранённые мониторы сейчас отключены."), 12, Ui.Muted); hint.Margin = new Thickness(0, 15, 0, 0); _page.Children.Add(hint);
            foreach (var fence in disconnected) { var group = Ellipsis(fence.Name, 11); group.Margin = new Thickness(0, 6, 0, 0); _page.Children.Add(group); }
        }
    }
}
