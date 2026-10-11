using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;

namespace WalleFences.App;

internal sealed class ScreenColorSampler : IDisposable
{
    private readonly TaskCompletionSource<Color?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<SamplerOverlay> _overlays = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly CancellationToken _cancellation;
    private CancellationTokenRegistration _registration;
    private bool _finished, _escapeReleased;

    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();

    private ScreenColorSampler(CancellationToken cancellation) => _cancellation = cancellation;

    internal static Task<Color?> PickAsync(CancellationToken cancellation)
    {
        var sampler = new ScreenColorSampler(cancellation);
        try { sampler.Start(); }
        catch (Exception error) { sampler.Finish(null, error); }
        return sampler._completion.Task;
    }

    private void Start()
    {
        if (_cancellation.IsCancellationRequested) { Finish(null); return; }
        // Capture each monitor before any overlay appears. The preview retains exact RGB values
        // and the same physical pixels even when adjacent monitors use different DPI scales.
        DwmFlush();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            _overlays.Add(new SamplerOverlay(this, screen.Bounds));
        if (_overlays.Count == 0) throw new InvalidOperationException("No display is available.");
        _escapeReleased = (GetAsyncKeyState(0x1B) & 0x8000) == 0;
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        _registration = _cancellation.Register(() => Application.Current.Dispatcher.BeginInvoke(() => Finish(null)));
        foreach (var overlay in _overlays) overlay.Show();
        _timer.Tick += TrackPointer; _timer.Start(); TrackPointer(this, EventArgs.Empty);
    }

    private void TrackPointer(object? sender, EventArgs e)
    {
        if (_finished) return;
        var escapeDown = (GetAsyncKeyState(0x1B) & 0x8000) != 0;
        if (!escapeDown) _escapeReleased = true;
        else if (_escapeReleased) { Finish(null); return; }
        if (!Native.GetCursorPos(out var point)) return;
        foreach (var overlay in _overlays) overlay.UpdatePreview(point);
    }

    private void Select()
    {
        if (_finished || !Native.GetCursorPos(out var point)) { Finish(null); return; }
        try
        {
            foreach (var overlay in _overlays) Native.ShowWindow(new WindowInteropHelper(overlay).Handle, 0);
            DwmFlush();
            var dc = GetDC(0);
            if (dc == 0) throw new ExternalException("The screen device context is unavailable.");
            uint pixel;
            try { pixel = GetPixel(dc, point.X, point.Y); }
            finally { ReleaseDC(0, dc); }
            if (pixel == uint.MaxValue) throw new ExternalException("The screen pixel is unavailable.");
            Finish(Color.FromRgb((byte)(pixel & 255), (byte)((pixel >> 8) & 255), (byte)((pixel >> 16) & 255)));
        }
        catch (Exception error) { Finish(null, error); }
    }

    private void DisplaysChanged(object? sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(() => Finish(null));

    private void Finish(Color? color, Exception? error = null)
    {
        if (_finished) return;
        Dispose();
        if (error != null) _completion.TrySetException(error);
        else _completion.TrySetResult(color);
    }

    public void Dispose()
    {
        if (_finished) return;
        _finished = true; _timer.Stop(); _timer.Tick -= TrackPointer;
        SystemEvents.DisplaySettingsChanged -= DisplaysChanged; _registration.Dispose();
        foreach (var overlay in _overlays)
        {
            if (overlay.IsMouseCaptured) overlay.ReleaseMouseCapture();
            overlay.Close(); overlay.DisposeSnapshot();
        }
        _overlays.Clear();
    }

    private sealed class SamplerOverlay : Window
    {
        private readonly ScreenColorSampler _sampler;
        private readonly DrawingRectangle _bounds;
        private readonly DrawingBitmap _snapshot;
        private readonly Image _image;
        private readonly Canvas _previewCanvas = new() { IsHitTestVisible = false };
        private readonly Border _preview;
        private readonly Border _swatch;
        private readonly TextBlock _hex;

        internal SamplerOverlay(ScreenColorSampler sampler, DrawingRectangle bounds)
        {
            _sampler = sampler; _bounds = bounds;
            _snapshot = new DrawingBitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                using (var graphics = System.Drawing.Graphics.FromImage(_snapshot))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, System.Drawing.CopyPixelOperation.SourceCopy);
                var bitmapHandle = _snapshot.GetHbitmap();
                BitmapSource source;
                try { source = Imaging.CreateBitmapSourceFromHBitmap(bitmapHandle, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze(); }
                finally { DeleteObject(bitmapHandle); }
                _image = new Image { Source = source, Stretch = Stretch.Fill, IsHitTestVisible = false };
            }
            catch { _snapshot.Dispose(); throw; }
            Left = bounds.Left; Top = bounds.Top; Width = bounds.Width; Height = bounds.Height;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
            Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true; Cursor = Cursors.Cross;
            Title = L.Text("Пипетка с экрана");
            AutomationProperties.SetName(this, Title);
            AutomationProperties.SetHelpText(this, L.Text("Выберите цвет на экране · Esc или ПКМ — отменить"));
            var grid = new Grid { ClipToBounds = true }; grid.Children.Add(_image);
            var instruction = Ui.Text(L.Text("Выберите цвет на экране · Esc или ПКМ — отменить"), 12, Ui.Ink, FontWeights.Medium);
            var hint = new Border { Child = instruction, Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(16, 24, 16, 0), CornerRadius = new CornerRadius(8), Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            grid.Children.Add(hint);
            var previewContent = new StackPanel { Orientation = Orientation.Horizontal };
            _swatch = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(4), BorderBrush = Ui.Line, BorderThickness = new Thickness(1) }; previewContent.Children.Add(_swatch);
            _hex = Ui.Text("", 12, Ui.Ink, FontWeights.SemiBold); _hex.Margin = new Thickness(8, 0, 0, 0); _hex.VerticalAlignment = VerticalAlignment.Center; previewContent.Children.Add(_hex);
            _preview = new Border { Child = previewContent, Padding = new Thickness(9), CornerRadius = new CornerRadius(7), Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
            _previewCanvas.Children.Add(_preview); grid.Children.Add(_previewCanvas); Content = grid;
            SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                Native.SetWindowLong(handle, Native.GwlExStyle, (nint)((long)Native.GetWindowLong(handle, Native.GwlExStyle) | 0x08000000 | 0x80));
                PositionPhysically();
            };
            Loaded += (_, _) => PositionPhysically();
            MouseLeftButtonDown += (_, e) => { CaptureMouse(); e.Handled = true; };
            MouseLeftButtonUp += (_, e) => { if (IsMouseCaptured) { ReleaseMouseCapture(); _sampler.Select(); } e.Handled = true; };
            MouseRightButtonDown += (_, e) => { CaptureMouse(); e.Handled = true; };
            MouseRightButtonUp += (_, e) => { if (IsMouseCaptured) ReleaseMouseCapture(); e.Handled = true; _sampler.Finish(null); };
            Closed += (_, _) => { if (!_sampler._finished) _sampler.Finish(null); };
        }

        private void PositionPhysically()
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(handle, Native.HwndTopmost, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, Native.SwpNoActivate);
            var scale = Math.Max(96u, Native.GetDpiForWindow(handle)) / 96.0;
            Width = _bounds.Width / scale; Height = _bounds.Height / scale;
            Native.SetWindowPos(handle, Native.HwndTopmost, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, Native.SwpNoActivate);
        }

        internal void UpdatePreview(Native.Point point)
        {
            if (!_bounds.Contains(point.X, point.Y)) { _preview.Visibility = Visibility.Collapsed; return; }
            var pixel = _snapshot.GetPixel(point.X - _bounds.Left, point.Y - _bounds.Top);
            var color = Color.FromRgb(pixel.R, pixel.G, pixel.B);
            _swatch.Background = new SolidColorBrush(color); _hex.Text = $"#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}";
            _preview.Visibility = Visibility.Visible; _preview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var local = PointFromScreen(new Point(point.X, point.Y));
            var x = local.X + 22; var y = local.Y + 22;
            if (x + _preview.DesiredSize.Width > ActualWidth - 8) x = local.X - _preview.DesiredSize.Width - 16;
            if (y + _preview.DesiredSize.Height > ActualHeight - 8) y = local.Y - _preview.DesiredSize.Height - 16;
            Canvas.SetLeft(_preview, Math.Max(8, x)); Canvas.SetTop(_preview, Math.Max(8, y));
        }

        internal void DisposeSnapshot() { _image.Source = null; _snapshot.Dispose(); }
    }
}
