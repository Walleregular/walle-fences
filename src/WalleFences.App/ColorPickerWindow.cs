using System.Globalization;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Markup;
using System.Windows.Shapes;

namespace WalleFences.App;

internal sealed class ColorPickerWindow : Window
{
    private readonly Grid _plane = new() { Width = 240, Height = 240, Background = Brushes.Transparent, Focusable = true };
    private readonly Canvas _marker = new() { IsHitTestVisible = false };
    private readonly Grid _pointer = new() { Width = 14, Height = 14, IsHitTestVisible = false };
    private readonly GradientStop _pureHue = new(Colors.Red, 1);
    private readonly Border _planeOutline = new() { BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), IsHitTestVisible = false };
    private readonly TextBox _hex = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 16, Margin = new Thickness(0) };
    private readonly TextBlock _hexError = Ui.Text(L.Text("Введите цвет в формате #RRGGBB."), 10, Ui.Muted);
    private readonly Border _newSwatch = new() { Height = 46, CornerRadius = new CornerRadius(6), BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
    private readonly SolidColorBrush _draftBrush = new();
    private readonly Slider _hue, _saturation, _brightness, _red, _green, _blue;
    private readonly TextBlock _hueValue, _saturationValue, _brightnessValue, _redValue, _greenValue, _blueValue;
    private readonly Button _apply;
    private readonly Button _sample;
    private readonly TextBlock _sampleError = Ui.Text(L.Text("Не удалось получить цвет с экрана."), 10, Ui.Muted);
    private CancellationTokenSource? _samplingCancellation;
    private bool _closed;
    private bool _syncing, _validHex = true;
    private double _h, _s, _v;
    private Color _color;
    internal Color OriginalColor { get; }
    internal Color DraftColor => _color;
    internal string? Result { get; private set; }

    internal static string? Pick(Window? owner, string title, string initial)
    {
        var dialog = new ColorPickerWindow(title, initial);
        if (owner != null) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    internal static bool TryHex(string? input, out string normalized, out Color color)
    {
        normalized = ""; color = default;
        var value = input?.Trim();
        if (value?.StartsWith('#') == true) value = value[1..];
        if (value == null || value.Length != 6 || !value.All(Uri.IsHexDigit) || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = Color.FromRgb((byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255));
        normalized = "#" + value.ToUpperInvariant();
        return true;
    }

    internal ColorPickerWindow(string title, string initial)
    {
        OriginalColor = TryHex(initial, out _, out var initialColor) ? initialColor : Ui.Accent.Color;
        _color = Color.FromRgb(OriginalColor.R, OriginalColor.G, OriginalColor.B);
        (_h, _s, _v) = ToHsv(_color, 0);
        Title = title; Icon = Brand.WindowIcon; Width = 440; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height; ShowInTaskbar = false;
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 64); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Language = XmlLanguage.GetLanguage(L.Culture.Name); FontFamily = Ui.AppFont;
        SetResourceReference(BackgroundProperty, "Paper"); SetResourceReference(ForegroundProperty, "Ink");
        AutomationProperties.SetAutomationId(this, "color-picker"); Ui.WindowFrame(this);

        var shell = new DockPanel();
        var header = new DockPanel { LastChildFill = true };
        var close = Ui.IconButton("close", Close, L.Text("Закрыть"), "picker-close"); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(Ui.Text(title, 13, Ui.Ink, FontWeights.SemiBold));
        var titlebar = new Border { Child = header, Height = 40, Padding = new Thickness(16, 0, 6, 0), Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        titlebar.MouseLeftButtonDown += (_, e) => { if (!close.IsMouseOver) { DragMove(); e.Handled = true; } };
        DockPanel.SetDock(titlebar, Dock.Top); shell.Children.Add(titlebar);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 12, 14, 16) };
        var cancel = Ui.Button(L.Text("Отмена"), Close, id: "picker-cancel"); cancel.IsCancel = true; actions.Children.Add(cancel);
        _apply = Ui.Button(L.Text("Применить цвет"), Apply, true, "picker-apply"); _apply.IsDefault = true; actions.Children.Add(_apply);
        DockPanel.SetDock(actions, Dock.Bottom); shell.Children.Add(actions);

        var body = new StackPanel { Margin = new Thickness(20, 18, 20, 2) };
        var top = new Grid(); top.ColumnDefinitions.Add(new() { Width = new GridLength(240) }); top.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); top.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        BuildPlane(); top.Children.Add(_plane);
        var comparison = new StackPanel(); Grid.SetColumn(comparison, 2); top.Children.Add(comparison);
        comparison.Children.Add(Caption("Исходный цвет"));
        var originalSwatch = new Border { Height = 46, Background = new SolidColorBrush(OriginalColor), CornerRadius = new CornerRadius(6), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetAutomationId(originalSwatch, "picker-original"); originalSwatch.ToolTip = Hex(OriginalColor); comparison.Children.Add(originalSwatch);
        comparison.Children.Add(Caption("Новый цвет")); _newSwatch.Background = _draftBrush; _newSwatch.Margin = new Thickness(0, 0, 0, 12); AutomationProperties.SetAutomationId(_newSwatch, "picker-preview"); comparison.Children.Add(_newSwatch);
        var hexLabel = Ui.Text("HEX", 11, Ui.Muted); hexLabel.Margin = new Thickness(0, 0, 0, 5); comparison.Children.Add(hexLabel);
        AutomationProperties.SetAutomationId(_hex, "picker-hex"); AutomationProperties.SetName(_hex, "HEX · #RRGGBB"); _hex.ToolTip = L.Text("Введите цвет в формате #RRGGBB."); comparison.Children.Add(_hex);
        _hexError.Visibility = Visibility.Collapsed; _hexError.Margin = new Thickness(0, 5, 0, 0); AutomationProperties.SetAutomationId(_hexError, "picker-hex-error"); comparison.Children.Add(_hexError);
        _sample = Ui.Button(L.Text("Пипетка с экрана"), SampleScreen, id: "picker-eyedropper");
        _sample.Margin = new Thickness(0, 12, 0, 0); _sample.Padding = new Thickness(8, 7, 8, 7);
        var sampleContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        sampleContent.Children.Add(Brand.Icon("eyedropper", Ui.Ink, 15));
        var sampleCaption = Ui.Text(L.Text("Пипетка"), 10.5, Ui.Ink); sampleCaption.Margin = new Thickness(6, 0, 0, 0); sampleContent.Children.Add(sampleCaption);
        _sample.Content = sampleContent; _sample.ToolTip = L.Text("Выберите цвет на экране · Esc или ПКМ — отменить"); comparison.Children.Add(_sample);
        _sampleError.Visibility = Visibility.Collapsed; _sampleError.Margin = new Thickness(0, 6, 0, 0); comparison.Children.Add(_sampleError);
        body.Children.Add(top);

        var hueHeader = ValueHeader("Цветовой тон", out _hueValue); hueHeader.Margin = new Thickness(0, 15, 0, 1); body.Children.Add(hueHeader);
        _hue = Slider("Цветовой тон", "picker-hue", 360); _hue.IsSnapToTickEnabled = false;
        _hue.Template = (ControlTemplate)XamlReader.Parse(HueTemplate); body.Children.Add(_hue);
        _saturation = SliderRow(body, "Насыщенность", "picker-saturation", 100, out _saturationValue);
        _brightness = SliderRow(body, "Яркость", "picker-brightness", 100, out _brightnessValue);

        var rgb = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        _red = SliderRow(rgb, "Красный", "picker-red", 255, out _redValue);
        _green = SliderRow(rgb, "Зелёный", "picker-green", 255, out _greenValue);
        _blue = SliderRow(rgb, "Синий", "picker-blue", 255, out _blueValue);
        var precise = new Expander { Header = Ui.Text("RGB · " + L.Text("Точные значения"), 12, Ui.Muted), Content = rgb, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetAutomationId(precise, "picker-rgb"); body.Children.Add(precise);

        _hex.TextChanged += (_, _) => HexChanged();
        _hex.LostKeyboardFocus += (_, _) => { if (_validHex) { _syncing = true; _hex.Text = Hex(_color); _syncing = false; } };
        _hue.ValueChanged += (_, _) => { if (!_syncing) SetHsv(_hue.Value, _s, _v); };
        _saturation.ValueChanged += (_, _) => { if (!_syncing) SetHsv(_h, _saturation.Value / 100, _v); };
        _brightness.ValueChanged += (_, _) => { if (!_syncing) SetHsv(_h, _s, _brightness.Value / 100); };
        foreach (var slider in new[] { _red, _green, _blue }) slider.ValueChanged += (_, _) => { if (!_syncing) SetRgb(); };
        Content = new Border { Child = shell, Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
        shell.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Loaded += (_, _) => { _hex.Focus(); _hex.SelectAll(); };
        Closed += (_, _) => { _closed = true; _samplingCancellation?.Cancel(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        SyncControls();
    }

    private static TextBlock Caption(string key)
    {
        var label = Ui.Text(L.Text(key), 11, Ui.Muted); label.Margin = new Thickness(0, 0, 0, 5); return label;
    }
    private static Grid ValueHeader(string key, out TextBlock value)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(58) });
        row.Children.Add(Ui.Text(L.Text(key), 11, Ui.Muted)); value = Ui.Text("", 11, Ui.Ink); value.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(value, 1); row.Children.Add(value); return row;
    }
    private static Slider Slider(string label, string id, double maximum)
    {
        var slider = new Slider { Minimum = 0, Maximum = maximum, TickFrequency = 1, SmallChange = 1, LargeChange = maximum == 255 ? 16 : 10, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Margin = new Thickness(0) };
        AutomationProperties.SetAutomationId(slider, id); AutomationProperties.SetName(slider, L.Text(label)); return slider;
    }
    private static Slider SliderRow(Panel parent, string label, string id, double maximum, out TextBlock value)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 0) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(104) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(50) });
        row.Children.Add(Ui.Text(L.Text(label), 11, Ui.Muted)); var slider = Slider(label, id, maximum); Grid.SetColumn(slider, 1); row.Children.Add(slider);
        value = Ui.Text("", 11, Ui.Ink); value.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(value, 2); row.Children.Add(value); parent.Children.Add(row); return slider;
    }
    private void BuildPlane()
    {
        var horizontal = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) }; horizontal.GradientStops.Add(new(Colors.White, 0)); horizontal.GradientStops.Add(_pureHue);
        var vertical = new LinearGradientBrush { StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1) }; vertical.GradientStops.Add(new(Colors.Transparent, 0)); vertical.GradientStops.Add(new(Colors.Black, 1));
        var colors = new Grid { Background = horizontal, Clip = new RectangleGeometry(new Rect(0, 0, 240, 240), 6, 6), IsHitTestVisible = false }; colors.Children.Add(new Border { Background = vertical }); _plane.Children.Add(colors); _plane.Children.Add(_planeOutline);
        _pointer.Children.Add(new Ellipse { Stroke = Brushes.Black, StrokeThickness = 3 }); _pointer.Children.Add(new Ellipse { Stroke = Brushes.White, StrokeThickness = 1.5 }); _marker.Children.Add(_pointer); _plane.Children.Add(_marker);
        AutomationProperties.SetAutomationId(_plane, "picker-plane"); AutomationProperties.SetName(_plane, L.Text("Насыщенность") + " / " + L.Text("Яркость"));
        _plane.MouseLeftButtonDown += (_, e) => { _plane.Focus(); _plane.CaptureMouse(); SelectPlane(e.GetPosition(_plane)); e.Handled = true; };
        _plane.MouseMove += (_, e) => { if (_plane.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) SelectPlane(e.GetPosition(_plane)); };
        _plane.MouseLeftButtonUp += (_, e) => { if (_plane.IsMouseCaptured) { SelectPlane(e.GetPosition(_plane)); _plane.ReleaseMouseCapture(); e.Handled = true; } };
        _plane.GotKeyboardFocus += (_, _) => _planeOutline.BorderBrush = Ui.FocusAccent;
        _plane.LostKeyboardFocus += (_, _) => _planeOutline.BorderBrush = Ui.Line;
        _plane.KeyDown += (_, e) =>
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .1 : .01;
            switch (e.Key)
            {
                case Key.Left: SetHsv(_h, _s - step, _v); break;
                case Key.Right: SetHsv(_h, _s + step, _v); break;
                case Key.Up: SetHsv(_h, _s, _v + step); break;
                case Key.Down: SetHsv(_h, _s, _v - step); break;
                case Key.Home: SetHsv(_h, 0, 1); break;
                case Key.End: SetHsv(_h, 1, 0); break;
                default: return;
            }
            e.Handled = true;
        };
    }
    private void SelectPlane(Point point) => SetHsv(_h, point.X / _plane.Width, 1 - point.Y / _plane.Height);
    private void SetHsv(double hue, double saturation, double brightness)
    {
        _h = Math.Clamp(hue, 0, 360); _s = Math.Clamp(saturation, 0, 1); _v = Math.Clamp(brightness, 0, 1); _color = FromHsv(_h, _s, _v); SyncControls();
    }
    private void SetRgb()
    {
        _color = Color.FromRgb((byte)Math.Round(_red.Value), (byte)Math.Round(_green.Value), (byte)Math.Round(_blue.Value)); (_h, _s, _v) = ToHsv(_color, _h); SyncControls();
    }
    private void HexChanged()
    {
        if (_syncing) return;
        if (!TryHex(_hex.Text, out _, out var color))
        {
            _validHex = false; _apply.IsEnabled = false; _hexError.Visibility = Visibility.Visible; return;
        }
        _color = color; (_h, _s, _v) = ToHsv(color, _h); SyncControls(false);
    }
    private void SyncControls(bool rewriteHex = true)
    {
        _syncing = true;
        try
        {
            _hue.Value = _h; _saturation.Value = _s * 100; _brightness.Value = _v * 100; _red.Value = _color.R; _green.Value = _color.G; _blue.Value = _color.B;
            _hueValue.Text = Math.Round(_h).ToString("0", L.Culture) + "°"; _saturationValue.Text = Math.Round(_s * 100).ToString("0", L.Culture) + "%"; _brightnessValue.Text = Math.Round(_v * 100).ToString("0", L.Culture) + "%";
            _redValue.Text = _color.R.ToString(L.Culture); _greenValue.Text = _color.G.ToString(L.Culture); _blueValue.Text = _color.B.ToString(L.Culture);
            _pureHue.Color = FromHsv(_h, 1, 1); _draftBrush.Color = _color; _newSwatch.ToolTip = Hex(_color);
            Canvas.SetLeft(_pointer, _s * _plane.Width - 7); Canvas.SetTop(_pointer, (1 - _v) * _plane.Height - 7);
            if (rewriteHex) _hex.Text = Hex(_color);
            _validHex = true; _hexError.Visibility = Visibility.Collapsed; _apply.IsEnabled = true;
        }
        finally { _syncing = false; }
    }
    private void Apply()
    {
        if (!TryHex(_hex.Text, out var normalized, out _)) { _hex.Focus(); return; }
        Result = normalized; DialogResult = true;
    }
    private async void SampleScreen()
    {
        if (_samplingCancellation != null || _closed) return;
        var cancellation = new CancellationTokenSource(); _samplingCancellation = cancellation;
        _sample.IsEnabled = false; _sampleError.Visibility = Visibility.Collapsed;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        try
        {
            // Hide the HWND, rather than the WPF modal window: Window.Hide would end ShowDialog.
            Native.ShowWindow(handle, 0);
            var sampled = await ScreenColorSampler.PickAsync(cancellation.Token);
            if (!_closed && sampled is { } color)
            {
                _color = color; (_h, _s, _v) = ToHsv(color, _h); SyncControls();
            }
        }
        catch (Exception)
        {
            if (!_closed) _sampleError.Visibility = Visibility.Visible;
        }
        finally
        {
            _samplingCancellation = null; cancellation.Dispose();
            if (!_closed)
            {
                Native.ShowWindow(handle, 4); Activate(); _sample.IsEnabled = true; _hex.Focus(); _hex.SelectAll();
            }
        }
    }
    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    private static (double Hue, double Saturation, double Brightness) ToHsv(Color color, double priorHue)
    {
        var r = color.R / 255.0; var g = color.G / 255.0; var b = color.B / 255.0;
        var maximum = Math.Max(r, Math.Max(g, b)); var minimum = Math.Min(r, Math.Min(g, b)); var delta = maximum - minimum;
        var hue = priorHue;
        if (delta > 0)
        {
            hue = maximum == r ? 60 * ((g - b) / delta % 6) : maximum == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
            if (hue < 0) hue += 360;
        }
        return (hue, maximum == 0 ? 0 : delta / maximum, maximum);
    }
    private static Color FromHsv(double hue, double saturation, double brightness)
    {
        var sector = (hue % 360) / 60; var c = brightness * saturation; var x = c * (1 - Math.Abs(sector % 2 - 1)); var m = brightness - c;
        var (r, g, b) = (int)sector switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
        static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value * 255, MidpointRounding.AwayFromZero), 0, 255);
        return Color.FromRgb(Channel(r + m), Channel(g + m), Channel(b + m));
    }

    private const string HueTemplate = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type Slider}">
          <Grid>
            <Border Height="10" CornerRadius="5" Margin="9,0" VerticalAlignment="Center">
              <Border.Background><LinearGradientBrush StartPoint="0,.5" EndPoint="1,.5">
                <GradientStop Color="#FF0000" Offset="0"/><GradientStop Color="#FFFF00" Offset=".1666667"/><GradientStop Color="#00FF00" Offset=".3333333"/>
                <GradientStop Color="#00FFFF" Offset=".5"/><GradientStop Color="#0000FF" Offset=".6666667"/><GradientStop Color="#FF00FF" Offset=".8333333"/><GradientStop Color="#FF0000" Offset="1"/>
              </LinearGradientBrush></Border.Background>
            </Border>
            <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" IsDirectionReversed="{TemplateBinding IsDirectionReversed}">
              <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
              <Track.Thumb><Thumb Width="18" Height="22"><Thumb.Template><ControlTemplate TargetType="Thumb"><Grid Background="Transparent"><Ellipse Width="14" Height="14" Stroke="Black" StrokeThickness="3"/><Ellipse Width="14" Height="14" Stroke="White" StrokeThickness="1.5"/></Grid></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
        """;
}
