using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.ComponentModel;
using System.Windows.Data;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Media.Animation;

namespace WalleFences.App;

internal static class Ui
{
    private sealed class ThemeColor(Color value) : INotifyPropertyChanged
    {
        public Color Value { get; private set; } = value;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Set(Color next) { if (Value == next) return; Value = next; PropertyChanged?.Invoke(this, new(nameof(Value))); }
    }
    private static readonly Dictionary<SolidColorBrush, ThemeColor> ThemeColors = [];
    private sealed class ComboTooltipConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            static object? Content(object? value) => value is ContentControl control ? control.Content : value;
            static string? Label(object? value, System.Globalization.CultureInfo culture) => value switch
            {
                string text => text,
                TextBlock text => text.Text,
                AccessText text => text.Text,
                char or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => System.Convert.ToString(value, culture),
                _ => null
            };
            try
            {
                var selected = Content(values[0]); var displayPath = values[2] as string;
                if (!string.IsNullOrWhiteSpace(displayPath))
                {
                    foreach (var part in displayPath.Split('.'))
                    {
                        if (selected == null) break;
                        var property = TypeDescriptor.GetProperties(selected).Find(part, false);
                        selected = property?.GetValue(selected);
                    }
                    return (object?)Label(selected, culture) ?? DependencyProperty.UnsetValue;
                }
                // SelectionBoxItem can remain empty or show the previous label until its template updates.
                return (object?)(Label(selected, culture) ?? Label(Content(values[1]), culture)) ?? DependencyProperty.UnsetValue;
            }
            catch (Exception) { return DependencyProperty.UnsetValue; }
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
    private static readonly ComboTooltipConverter ComboTooltip = new();
    // A binding prevents ResourceDictionary from freezing a brush shared by live themes.
    private static SolidColorBrush ThemedBrush(string hex)
    {
        var color = new ThemeColor((Color)ColorConverter.ConvertFromString(hex)); var brush = new SolidColorBrush(color.Value);
        BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(ThemeColor.Value)) { Source = color });
        ThemeColors.Add(brush, color); return brush;
    }
    internal static readonly SolidColorBrush Ink = ThemedBrush("#EBEEEB"), Muted = ThemedBrush("#8E9792"), Accent = ThemedBrush("#A0E9C0"), Line = ThemedBrush("#343938"), Paper = ThemedBrush("#151718"), Surface = ThemedBrush("#1D2021"), OnAccent = ThemedBrush("#14271E");
    internal static readonly SolidColorBrush Sidebar = ThemedBrush("#151718"), Raised = ThemedBrush("#25292A"), Hover = ThemedBrush("#303633"), AccentWash = ThemedBrush("#23352C"), FocusAccent = ThemedBrush("#A0E9C0");
    internal static SolidColorBrush Brush(string hex, double opacity = 1) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Opacity = opacity; return b; }
    internal static readonly FontFamily AppFont = new(new Uri("pack://application:,,,/"), "/WalleFences;component/Assets/Fonts/#Manrope");
    // The packaged family exposes real 400, 500, 600 and 700 faces, including Cyrillic.
    // Keep aliases for existing callers; FontWeight selects a face within one family.
    internal static readonly FontFamily MediumFont = AppFont;
    internal static readonly FontFamily SemiboldFont = AppFont;
    internal static void Theme(Application app)
    {
        app.Resources = new ResourceDictionary { Source = new Uri("pack://application:,,,/WalleFences;component/Assets/Theme.xaml") };
        // A style event setter makes WPF schedule Loaded for controls without their own handlers.
        var comboStyle = new Style(typeof(ComboBox), (Style)app.Resources[typeof(ComboBox)]);
        comboStyle.Setters.Add(new EventSetter(FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => EnsureComboTooltip((ComboBox)sender))));
        app.Resources[typeof(ComboBox)] = comboStyle;
        ApplyTheme(new Preferences());
    }
    internal static void EnsureComboTooltip(ComboBox combo)
    {
        // Never replace a local tooltip, binding, style setter or a custom control style.
        if (DependencyPropertyHelper.GetValueSource(combo, FrameworkElement.ToolTipProperty).BaseValueSource != BaseValueSource.Default || combo.ReadLocalValue(FrameworkElement.StyleProperty) != DependencyProperty.UnsetValue) return;
        var binding = new MultiBinding { Converter = ComboTooltip, Mode = BindingMode.OneWay };
        foreach (var property in new[] { nameof(ComboBox.SelectedItem), nameof(ComboBox.SelectionBoxItem), nameof(ComboBox.DisplayMemberPath) }) binding.Bindings.Add(new Binding(property) { Source = combo });
        BindingOperations.SetBinding(combo, FrameworkElement.ToolTipProperty, binding);
    }
    internal static void ApplyTheme(Preferences prefs)
    {
        var palette = prefs.Theme switch
        {
            InterfaceTheme.Paper => new[] { "#202724", "#616C65", "#D5DCD7", "#F3F5F2", "#FFFFFF", "#ECEFEC", "#E5EAE5", "#DDE5DE", "#DDECE1" },
            InterfaceTheme.Midnight => new[] { "#E7ECF5", "#969FB4", "#323848", "#11151E", "#191F2B", "#11151E", "#232B3A", "#2D3848", "#23372F" },
            _ => new[] { "#EBEEEB", "#8E9792", "#343938", "#151718", "#1D2021", "#151718", "#25292A", "#303633", "#23352C" }
        };
        var brushes = new[] { Ink, Muted, Line, Paper, Surface, Sidebar, Raised, Hover, AccentWash };
        for (var i = 0; i < brushes.Length; i++) ThemeColors[brushes[i]].Set((Color)ColorConverter.ConvertFromString(palette[i]));
        var accentColor = (Color)ColorConverter.ConvertFromString(prefs.InterfaceAccent); ThemeColors[Accent].Set(accentColor);
        byte Wash(byte basis, byte accent) => (byte)Math.Round(basis * .86 + accent * .14);
        ThemeColors[AccentWash].Set(Color.FromRgb(Wash(Surface.Color.R, accentColor.R), Wash(Surface.Color.G, accentColor.G), Wash(Surface.Color.B, accentColor.B)));
        double Linear(byte value) { var s = value / 255.0; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
        double Luminance(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        var luminance = Luminance(accentColor);
        ThemeColors[OnAccent].Set(luminance > .179 ? Colors.Black : Colors.White);
        var focus = accentColor;
        if (prefs.Theme == InterfaceTheme.Paper)
        {
            focus = Color.FromRgb(focus.R, focus.G, focus.B);
            var lightSurface = new[] { Paper, Surface, Raised }.Min(brush => Luminance(brush.Color));
            for (var i = 0; i < 40 && (lightSurface + .05) / (Luminance(focus) + .05) < 3.1; i++)
                focus = Color.FromRgb((byte)(focus.R * .92), (byte)(focus.G * .92), (byte)(focus.B * .92));
        }
        ThemeColors[FocusAccent].Set(focus);
        var resources = Application.Current.Resources;
        resources["Ink"] = Ink; resources["Muted"] = Muted; resources["Line"] = Line;
        resources["Paper"] = Paper; resources["Surface"] = Surface; resources["Accent"] = Accent;
        resources["OnAccent"] = OnAccent;
        resources["Sidebar"] = Sidebar; resources["Raised"] = Raised; resources["Hover"] = Hover; resources["AccentWash"] = AccentWash;
        resources["FocusAccent"] = FocusAccent;
    }
    internal static TextBlock Text(string text, double size = 13, Brush? color = null, FontWeight? weight = null) => new() { Text = text, FontFamily = AppFont, FontSize = size, Foreground = color ?? Ink, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    internal static Button Button(string label, Action action, bool primary = false, string? id = null)
    {
        var button = new Button { Content = label };
        if (primary) { button.Background = Accent; button.Foreground = OnAccent; button.BorderBrush = Accent; }
        if (id != null) AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => Guard(action); return button;
    }
    internal static Button Tiny(string label, Action action, string tooltip) { var b = Button(label, action); b.Padding = new Thickness(7, 3, 7, 3); b.Margin = new Thickness(2, 0, 2, 0); b.MinHeight = 26; b.ToolTip = tooltip; AutomationProperties.SetName(b, tooltip); return b; }
    internal static Button IconButton(string icon, Action action, string tooltip, string? id = null)
    {
        return IconButton(Brand.Icon(icon, Ink), action, tooltip, id);
    }
    internal static Button IconButton(UIElement content, Action action, string tooltip, string? id = null)
    {
        var button = Button("", action, id: id); button.SetResourceReference(FrameworkElement.StyleProperty, "IconButton");
        button.Content = content; button.ToolTip = tooltip; AutomationProperties.SetName(button, tooltip); return button;
    }
    internal static Button Segment(string label, Action action, bool selected = false, string? id = null)
    {
        var button = Button(label, action, id: id); button.SetResourceReference(FrameworkElement.StyleProperty, "SegmentButton"); button.Tag = selected ? "selected" : null; return button;
    }
    internal static CheckBox Toggle(string label, bool value, Action<bool>? changed = null, string? id = null)
    {
        var toggle = new CheckBox { Content = label, IsChecked = value }; toggle.SetResourceReference(FrameworkElement.StyleProperty, "SwitchCheckBox");
        AutomationProperties.SetName(toggle, label); if (id != null) AutomationProperties.SetAutomationId(toggle, id);
        toggle.Checked += (_, _) => { if (changed != null) Guard(() => changed(true)); }; toggle.Unchecked += (_, _) => { if (changed != null) Guard(() => changed(false)); }; return toggle;
    }
    internal static Border Card(UIElement child, Thickness? margin = null) => new() { Child = child, Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Margin = margin ?? new Thickness(0, 0, 0, 12) };
    // Keep the native resize and system menu while drawing our own compact title bar.
    internal static void WindowFrame(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(window.ResizeMode is ResizeMode.NoResize ? 0 : 6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(10), UseAeroCaptionButtons = false });
        void UpdateFrame()
        {
            var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
            var dark = Paper.Color.R + Paper.Color.G + Paper.Color.B < 384 ? 1 : 0; var corner = 2;
            _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)); _ = DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
        }
        EventHandler colorsChanged = (_, _) => UpdateFrame(); Paper.Changed += colorsChanged;
        window.SourceInitialized += (_, _) => UpdateFrame(); window.Closed += (_, _) => Paper.Changed -= colorsChanged;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    internal static void Label(Panel panel, string text) { var t = Text(text, 12, Muted, FontWeights.SemiBold); t.Margin = new Thickness(0, 0, 0, 6); panel.Children.Add(t); }
    internal static StackPanel Row(params UIElement[] children) { var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var child in children) p.Children.Add(child); return p; }
    internal static StackPanel Section(Panel parent, string title, bool expanded = false)
    {
        var body = new StackPanel { Margin = new Thickness(0, 12, 0, 4) };
        parent.Children.Add(new Expander { Header = Text(title, 14, Ink, FontWeights.SemiBold), Content = body, IsExpanded = expanded, Margin = new Thickness(0, 6, 0, 12) }); return body;
    }
    internal static void ColorPicker(Panel parent, TextBox target, string label)
    {
        var button = Button(label, () =>
        {
            var color = ColorPickerWindow.Pick(Window.GetWindow(target), label, target.Text);
            if (color != null) target.Text = color;
        }); button.Margin = new Thickness(0, 0, 0, 12); parent.Children.Add(button);
    }
    internal static void Error(string message) => Message(message, false);
    internal static void Guard(Action action) { try { action(); } catch (Exception ex) { Error(ex.Message); } }
    internal static bool Confirm(string message) => Message(message, true);
    private static bool Message(string message, bool confirm)
    {
        var dialog = new FormWindow(L.Text(confirm ? "Подтверждение" : "Обратите внимание"), 480) { MinHeight = 175, MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 80) };
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
        if (owner != null) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        if (confirm) { var no = Button(L.Text("Нет"), () => dialog.DialogResult = false); no.IsCancel = true; buttons.Children.Add(no); }
        var yes = Button(L.Text(confirm ? "Да" : "Хорошо"), () => dialog.DialogResult = true, true); yes.IsDefault = true; buttons.Children.Add(yes);
        dialog.Body.Children.Add(Text(message)); dialog.Body.Children.Add(buttons); dialog.ClearActions();
        return dialog.ShowDialog() == true;
    }
    internal static TextBox Input(Panel panel, string label, string value = "") { Label(panel, label); var box = new TextBox { Text = value }; AutomationProperties.SetName(box, label); panel.Children.Add(box); return box; }
    internal static TextBox HotkeyInput(Panel panel, string label, string value, AppController app)
    {
        var box = Input(panel, label, value); box.IsReadOnly = true;
        box.ToolTip = L.Text("Нажмите Ctrl, Alt или Win вместе с клавишей. Backspace — отключить.");
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        box.GotKeyboardFocus += (_, _) => app.SuspendHotkeys(true);
        box.LostKeyboardFocus += (_, _) => app.SuspendHotkeys(false);
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;
            e.Handled = true;
            if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None) { box.Text = ""; return; }
            var modifiers = Keyboard.Modifiers;
            var prefix = (modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + (modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + (modifiers.HasFlag(ModifierKeys.Windows) ? "Win+" : "");
            var name = key.ToString(); if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name[1..];
            if (HotkeyGesture.TryParse(prefix + name, out var captured)) box.Text = prefix + name;
        }; return box;
    }
    internal static CheckBox Check(Panel panel, string label, bool value) { var check = new CheckBox { Content = label, IsChecked = value }; panel.Children.Add(check); return check; }
    internal static ComboBox Combo(Panel panel, string label, IEnumerable<string> values, int selected = 0)
    {
        Label(panel, label); var combo = new ComboBox { ItemsSource = values.ToList(), SelectedIndex = selected }; EnsureComboTooltip(combo); AutomationProperties.SetName(combo, label); panel.Children.Add(combo); return combo;
    }
    internal static Slider Slider(Panel panel, string label, double min, double max, double value)
    {
        var text = Text(label + " · " + value.ToString("0", L.Culture), 12, Muted); panel.Children.Add(text);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Margin = new Thickness(0, 5, 0, 13), TickFrequency = 1, IsSnapToTickEnabled = true };
        AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, _) => text.Text = label + " · " + slider.Value.ToString("0", L.Culture); panel.Children.Add(slider); return slider;
    }
    internal static string? Ask(string title, string label, string initial = "")
    {
        string? result = null; var dialog = new FormWindow(title, 450); var input = Input(dialog.Body, label, initial);
        dialog.AddSave(() => { if (string.IsNullOrWhiteSpace(input.Text)) throw new InvalidOperationException(L.Text("Введите название.")); result = input.Text.Trim(); });
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; dialog.ShowDialog(); return result;
    }
}

internal sealed class FormWindow : Window
{
    internal StackPanel Body { get; } = new();
    private readonly Border _navigation = new() { Visibility = Visibility.Collapsed };
    private readonly ScrollViewer _bodyScroll;
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
    internal FormWindow(string title, double width = 520, bool resizable = false)
    {
        SetResourceReference(BackgroundProperty, "Paper"); SetResourceReference(ForegroundProperty, "Ink"); FontFamily = Ui.AppFont;
        Title = title; Icon = Brand.WindowIcon; Language = XmlLanguage.GetLanguage(L.Culture.Name); Width = width; SizeToContent = SizeToContent.Height; MaxHeight = Math.Max(450, SystemParameters.WorkArea.Height - 80); ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var manager = Application.Current?.Windows.OfType<ManagerWindow>().FirstOrDefault(window => window.IsVisible && window.WindowState != WindowState.Minimized);
        if (manager != null)
        {
            Owner = Application.Current?.Windows.OfType<FormWindow>().FirstOrDefault(window => window.IsActive && window.IsVisible) ?? (Window)manager;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        if (resizable)
        {
            SizeToContent = SizeToContent.Manual; ResizeMode = ResizeMode.CanResize;
            MinWidth = 540; MinHeight = Math.Min(440, MaxHeight); Height = Math.Min(690, MaxHeight);
        }
        Ui.WindowFrame(this);
        var shell = new DockPanel(); var header = new Border { Height = 40, Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(15, 0, 7, 0) }; DockPanel.SetDock(header, Dock.Top); shell.Children.Add(header);
        var titleRow = new DockPanel(); var close = Ui.IconButton("close", Close, L.Text("Закрыть")); close.Margin = new Thickness(0); close.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(close, Dock.Right); titleRow.Children.Add(close);
        titleRow.Children.Add(Ui.Text(title, 13, Ui.Ink, FontWeights.SemiBold)); header.Child = titleRow;
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock or Border or DockPanel) { DragMove(); e.Handled = true; } };
        DockPanel.SetDock(_navigation, Dock.Top); shell.Children.Add(_navigation);
        var layout = new DockPanel { Margin = new Thickness(20, 18, 20, 18) }; DockPanel.SetDock(_actions, Dock.Bottom); layout.Children.Add(_actions);
        _bodyScroll = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layout.Children.Add(_bodyScroll); shell.Children.Add(layout);
        Content = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Background = Ui.Paper, Child = shell };
        var cancel = Ui.Button(L.Text("Отмена"), Close, id: "dialog-cancel"); cancel.IsCancel = true; _actions.Children.Add(cancel);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
    internal void SetNavigation(UIElement content)
    {
        _navigation.Child = content; _navigation.Margin = new Thickness(20, 14, 20, 0); _navigation.Visibility = Visibility.Visible;
    }
    internal void ScrollBodyToTop() => _bodyScroll.ScrollToTop();
    internal void ClearActions() => _actions.Children.Clear();
    internal Button AddSave(Action action)
    {
        var save = Ui.Button(L.Text("Сохранить"), () =>
        {
            try { action(); DialogResult = true; }
            catch (OperationCanceledException) { /* A declined nested confirmation keeps the editor open. */ }
        }, true, "dialog-save"); save.IsDefault = true; _actions.Children.Add(save); return save;
    }
}

// Only pointer feedback moves. Keyboard changes and Windows reduced motion stay immediate.
// Template opacity/position setters remain the source of truth after each short animation.
public sealed class SwitchPositionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is true ? 16d : 0d;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
}

public static class InputFeedback
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(InputFeedback), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(FeedbackState), typeof(InputFeedback));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Control control) return;
        if (control.GetValue(StateProperty) is FeedbackState state) { state.Dispose(); control.ClearValue(StateProperty); }
        if (e.NewValue is true) control.SetValue(StateProperty, new FeedbackState(control));
    }

    private sealed class FeedbackState
    {
        private readonly Control _control;
        private Border? _hover;
        private TranslateTransform? _dot;
        private AnimationClock? _hoverClock, _switchClock;
        private double _hoverTarget, _switchTarget;
        private bool _pointerToggle;
        internal FeedbackState(Control control)
        {
            _control = control;
            control.Loaded += Loaded; control.Unloaded += Unloaded;
            control.MouseEnter += Entered; control.MouseLeave += Left;
            control.PreviewMouseLeftButtonDown += PointerDown; control.PreviewMouseLeftButtonUp += PointerUp;
            control.LostMouseCapture += CaptureLost;
            control.PreviewKeyDown += KeyboardUsed; control.PreviewKeyUp += KeyboardUsed;
            control.IsEnabledChanged += EnabledChanged;
            if (control is CheckBox toggle) { toggle.Checked += Toggled; toggle.Unchecked += Toggled; toggle.Indeterminate += Toggled; }
            if (control.IsLoaded) Prepare();
        }
        private void Prepare()
        {
            _control.ApplyTemplate();
            _hover = _control.Template?.FindName("hover", _control) as Border;
            _dot = (_control.Template?.FindName("dot", _control) as Border)?.RenderTransform as TranslateTransform;
            _hoverTarget = _control.IsMouseOver ? .07 : 0;
            _switchTarget = _control is CheckBox { IsChecked: true } ? 16 : 0;
            Stop();
        }
        private void Loaded(object sender, RoutedEventArgs e) => Prepare();
        private void Unloaded(object sender, RoutedEventArgs e) { Stop(); _hover = null; _dot = null; _pointerToggle = false; }
        private void Entered(object sender, MouseEventArgs e) => Hover(_control is ButtonBase { IsPressed: true } ? .14 : .07);
        private void Left(object sender, MouseEventArgs e) { Hover(0); _pointerToggle = false; }
        private void PointerDown(object sender, MouseButtonEventArgs e)
        {
            if (_control is CheckBox) _pointerToggle = true;
            else Hover(.14);
        }
        private void PointerUp(object sender, MouseButtonEventArgs e) { if (_control is not CheckBox) Hover(_control.IsMouseOver ? .07 : 0); }
        private void CaptureLost(object sender, MouseEventArgs e) { if (_control is not CheckBox) Hover(_control.IsMouseOver ? .07 : 0); }
        private void KeyboardUsed(object sender, KeyEventArgs e) { _pointerToggle = false; Stop(); }
        private void EnabledChanged(object sender, DependencyPropertyChangedEventArgs e) { _pointerToggle = false; Stop(); }
        private void Hover(double target)
        {
            if (_hover != null)
            {
                var from = _hoverClock?.CurrentState == ClockState.Active ? _hover.Opacity : _hoverTarget;
                _hoverClock = Animate(_hover, UIElement.OpacityProperty, from, target, 110, _control.IsEnabled);
            }
            _hoverTarget = target;
        }
        private void Toggled(object sender, RoutedEventArgs e)
        {
            var target = _control is CheckBox { IsChecked: true } ? 16 : 0;
            if (_dot != null)
            {
                // A stopped FillBehavior.Stop clock still makes HasAnimatedProperties true.
                // Its value already follows the new binding, so retain the settled old target.
                var from = _switchClock?.CurrentState == ClockState.Active ? _dot.X : _switchTarget;
                _switchClock = Animate(_dot, TranslateTransform.XProperty, from, target, 140, _pointerToggle);
            }
            _switchTarget = target; _pointerToggle = false;
        }
        private static AnimationClock? Animate(IAnimatable target, DependencyProperty property, double from, double to, int milliseconds, bool pointer)
        {
            if (!pointer || !SystemParameters.ClientAreaAnimation || Math.Abs(from - to) < .001)
            {
                target.BeginAnimation(property, null); return null;
            }
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
            };
            var clock = (AnimationClock)animation.CreateClock(true);
            target.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
            // Align the replacement immediately: before the next render tick WPF otherwise
            // exposes the newly bound end value, producing a flash on a quick reversal.
            clock.Controller!.Begin();
            clock.Controller.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
            return clock;
        }
        private void Stop()
        {
            _hover?.BeginAnimation(UIElement.OpacityProperty, null);
            _dot?.BeginAnimation(TranslateTransform.XProperty, null);
            _hoverClock = null; _switchClock = null;
        }
        internal void Dispose()
        {
            Stop();
            _control.Loaded -= Loaded; _control.Unloaded -= Unloaded;
            _control.MouseEnter -= Entered; _control.MouseLeave -= Left;
            _control.PreviewMouseLeftButtonDown -= PointerDown; _control.PreviewMouseLeftButtonUp -= PointerUp;
            _control.LostMouseCapture -= CaptureLost;
            _control.PreviewKeyDown -= KeyboardUsed; _control.PreviewKeyUp -= KeyboardUsed;
            _control.IsEnabledChanged -= EnabledChanged;
            if (_control is CheckBox toggle) { toggle.Checked -= Toggled; toggle.Unchecked -= Toggled; toggle.Indeterminate -= Toggled; }
        }
    }
}
