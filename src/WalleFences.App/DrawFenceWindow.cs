using System.Windows.Shapes;
using Microsoft.Win32;

namespace WalleFences.App;

internal sealed class DrawFenceWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly Rectangle _selection = new() { Stroke = Ui.Brush("#9CE0BD"), StrokeThickness = 2, Fill = Ui.Brush("#9CE0BD", .14) };
    private Point? _start;
    private readonly AppController _app;
    private readonly List<DrawFenceWindow> _overlays;
    private readonly Rect _bounds;
    private readonly double _coordinateFactor;
    private bool _finished;
    private DrawFenceWindow(AppController app, Rect bounds, List<DrawFenceWindow> overlays)
    {
        _app = app; _overlays = overlays; _bounds = bounds; _coordinateFactor = Native.MonitorScale(bounds) * AppController.GlobalScale;
        Left = bounds.Left; Top = bounds.Top; Width = bounds.Width / _coordinateFactor; Height = bounds.Height / _coordinateFactor;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Ui.Brush("#091918", .35); ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross;
        var grid = new Grid(); grid.Children.Add(_canvas); _canvas.Children.Add(_selection);
        var hint = Ui.Text(L.Text("Нарисуйте группу мышью  ·  Esc — отменить"), 18, Brushes.White, FontWeights.SemiBold); hint.HorizontalAlignment = HorizontalAlignment.Center; hint.VerticalAlignment = VerticalAlignment.Top; hint.Margin = new Thickness(0, 35, 0, 0); hint.IsHitTestVisible = false; grid.Children.Add(hint); Content = grid;
        MouseLeftButtonDown += (_, e) => { _start = e.GetPosition(_canvas); CaptureMouse(); UpdateSelection(_start.Value); e.Handled = true; };
        MouseMove += (_, e) => { if (_start != null) UpdateSelection(e.GetPosition(_canvas)); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (_start == null || _finished) return;
            var end = e.GetPosition(_canvas); var start = _start.Value; var selection = new Rect(new Point(_bounds.X + Math.Min(start.X, end.X) * _coordinateFactor, _bounds.Y + Math.Min(start.Y, end.Y) * _coordinateFactor), new Size(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y)));
            ReleaseMouseCapture(); CloseOverlays();
            if (selection.Width < 80 || selection.Height < 60) return;
            var name = Ui.Ask(L.Text("Новая группа"), L.Text("Название"), L.Text("Новая группа")); if (name != null) Create(_app, selection, name);
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { ReleaseMouseCapture(); CloseOverlays(); } };
        Loaded += (_, _) => Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, Native.HwndTopmost, (int)Math.Round(_bounds.X / AppController.GlobalScale), (int)Math.Round(_bounds.Y / AppController.GlobalScale), (int)Math.Round(_bounds.Width / AppController.GlobalScale), (int)Math.Round(_bounds.Height / AppController.GlobalScale), Native.SwpNoActivate);
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        Closed += (_, _) => { _finished = true; SystemEvents.DisplaySettingsChanged -= DisplaysChanged; if (IsMouseCaptured) ReleaseMouseCapture(); };
    }
    private void UpdateSelection(Point point)
    {
        if (_start is not { } start) return;
        Canvas.SetLeft(_selection, Math.Min(start.X, point.X)); Canvas.SetTop(_selection, Math.Min(start.Y, point.Y)); _selection.Width = Math.Abs(point.X - start.X); _selection.Height = Math.Abs(point.Y - start.Y);
    }
    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (_finished) return; CloseOverlays(); _app.SetStatus(L.Text("Рисование отменено: конфигурация экранов изменилась")); });
    private void CloseOverlays() { foreach (var overlay in _overlays.ToArray()) if (!overlay._finished) { overlay._finished = true; overlay.Close(); } }
    internal static void Draw(AppController app)
    {
        if (app.IsPeek) app.Peek(); var overlays = new List<DrawFenceWindow>();
        foreach (var area in AppController.MonitorAreas()) overlays.Add(new(app, area, overlays));
        foreach (var overlay in overlays) overlay.Show(); overlays[0].Activate();
    }
    internal static void Create(AppController app, Rect rectangle, string name) => app.Mutate(() => app.Workspace.Fences.Add(new Fence { Name = name, PageId = app.Workspace.ActivePageId, X = rectangle.X, Y = rectangle.Y, Width = Math.Max(200, rectangle.Width), Height = Math.Max(130, rectangle.Height) }));
}
