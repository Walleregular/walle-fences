using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace WalleFences.App;

internal sealed record GroupColorPalette(string Id, string Name, string Background, string Accent);

internal sealed class GroupColorsControl : StackPanel
{
    internal static IReadOnlyList<GroupColorPalette> Palettes => [
        new("mint", L.Text("Мята"), "#1D2021", "#A0E9C0"),
        new("ice", L.Text("Лёд"), "#192438", "#A7C7FF"),
        new("amber", L.Text("Янтарь"), "#302923", "#E7BD82"),
        new("lavender", L.Text("Лаванда"), "#2C2538", "#C9B3F5"),
        new("coral", L.Text("Коралл"), "#34262B", "#EFB09F"),
        new("ocean", L.Text("Океан"), "#1B2B34", "#86CDD9"),
        new("olive", L.Text("Олива"), "#2A2E23", "#CDD79A"),
        new("graphite", L.Text("Графит"), "#262B31", "#C5CED4")];

    private readonly TextBox _background, _accent;
    private readonly TextBlock _error, _previewName;
    private readonly Border _preview, _activeTab, _selection;
    private readonly TextBlock _previewTab, _previewLabel;
    private readonly Image _previewIcon, _fileIcon;
    private readonly List<(GroupColorPalette Palette, Button Button)> _choices = [];
    private readonly string _originalBackground, _originalAccent;
    private Color _validBackground, _validAccent;
    private bool _changing;
    private double _opacity = .97;
    private SurfaceStyle _surface = SurfaceStyle.Solid;
    private string _previewIconId = "folder";

    internal event EventHandler? ColorsChanged;
    internal string BackgroundHex => ColorPickerWindow.TryHex(_background.Text, out var hex, out _) ? hex : _background.Text;
    internal string AccentHex => ColorPickerWindow.TryHex(_accent.Text, out var hex, out _) ? hex : _accent.Text;
    internal bool IsValid => ColorPickerWindow.TryHex(_background.Text, out _, out _) && ColorPickerWindow.TryHex(_accent.Text, out _, out _);
    internal string ValidationMessage => L.Text("Укажите фон и подсветку в формате #RRGGBB.");

    internal GroupColorsControl(string background, string accent, string name = "")
    {
        ColorPickerWindow.TryHex(background, out _originalBackground, out _validBackground);
        ColorPickerWindow.TryHex(accent, out _originalAccent, out _validAccent);
        if (string.IsNullOrEmpty(_originalBackground)) { _originalBackground = "#1D2021"; _validBackground = Ui.Brush(_originalBackground).Color; }
        if (string.IsNullOrEmpty(_originalAccent)) { _originalAccent = "#A0E9C0"; _validAccent = Ui.Brush(_originalAccent).Color; }
        Margin = new Thickness(0, 8, 0, 12); AutomationProperties.SetAutomationId(this, "group-colors");
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var reset = Ui.IconButton("undo", () => SetColors(_originalBackground, _originalAccent), L.Text("Вернуть исходные цвета"), "group-colors-reset");
        reset.Width = reset.Height = 28; DockPanel.SetDock(reset, Dock.Right); heading.Children.Add(reset);
        heading.Children.Add(Ui.Text(L.Text("Фон и подсветка"), 13, Ui.Ink, FontWeights.SemiBold)); Children.Add(heading);

        var palettes = new UniformGrid { Columns = 4, Margin = new Thickness(-2, 0, -2, 8) };
        foreach (var palette in Palettes)
        {
            var button = Ui.Button("", () => SetColors(palette.Background, palette.Accent), id: "group-palette-" + palette.Id);
            button.Margin = new Thickness(2); button.Padding = new Thickness(7, 5, 7, 5); button.Height = 46;
            button.ToolTip = L.Format("{0}: фон {1}, подсветка {2}", palette.Name, palette.Background, palette.Accent);
            AutomationProperties.SetName(button, button.ToolTip.ToString());
            var sample = new Grid { Height = 9, Margin = new Thickness(0, 0, 0, 3) };
            sample.Children.Add(new Border { Background = Ui.Brush(palette.Background), CornerRadius = new CornerRadius(2), BorderBrush = Ui.Brush(palette.Accent, .32), BorderThickness = new Thickness(1) });
            sample.Children.Add(new Border { Width = 16, Background = Ui.Brush(palette.Accent), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left });
            var content = new StackPanel(); content.Children.Add(sample);
            var label = Ui.Text(palette.Name, 10.5); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(label);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Content = content;
            palettes.Children.Add(button); _choices.Add((palette, button));
        }
        Children.Add(palettes);

        var fields = new Grid(); fields.ColumnDefinitions.Add(new()); fields.ColumnDefinitions.Add(new());
        _background = ColorField(fields, L.Text("Фон"), _originalBackground, 0, "group-background");
        _accent = ColorField(fields, L.Text("Подсветка"), _originalAccent, 1, "group-accent"); Children.Add(fields);
        _error = Ui.Text("", 11, Ui.Brush(Ui.Paper.Color.R + Ui.Paper.Color.G + Ui.Paper.Color.B > 384 ? "#B13C4B" : "#EFADB4")); _error.Margin = new Thickness(0, 3, 0, 5); _error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_error, "group-colors-error"); Children.Add(_error);

        var previewLabel = Ui.Text(L.Text("Предпросмотр"), 10, Ui.Muted); previewLabel.Margin = new Thickness(0, 6, 0, 5); Children.Add(previewLabel);
        _preview = new Border { Padding = new Thickness(9, 5, 9, 6), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        AutomationProperties.SetAutomationId(_preview, "group-colors-preview");
        var previewBody = new Grid(); previewBody.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); previewBody.ColumnDefinitions.Add(new() { Width = new GridLength(120) });
        var left = new StackPanel(); var title = new Grid(); title.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); title.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _previewIcon = Brand.Icon("folder", null, 16); title.Children.Add(_previewIcon);
        _previewName = Ui.Text(string.IsNullOrWhiteSpace(name) ? L.Text("Группа") : name, 12, Ui.Brush("#EEF1EE"), FontWeights.SemiBold);
        _previewName.TextWrapping = TextWrapping.NoWrap; _previewName.TextTrimming = TextTrimming.CharacterEllipsis; _previewName.Margin = new Thickness(0, 0, 6, 0); Grid.SetColumn(_previewName, 1); title.Children.Add(_previewName); left.Children.Add(title);
        _previewTab = Ui.Text(L.Text("Файлы"), 10); _previewTab.Margin = new Thickness(4, 6, 5, 4);
        _activeTab = new Border { Child = _previewTab, BorderThickness = new Thickness(0, 0, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
        left.Children.Add(_activeTab); previewBody.Children.Add(left);
        _fileIcon = Brand.Icon("file", null, 16); _previewLabel = Ui.Text(L.Text("Файлы"), 10); _previewLabel.Margin = new Thickness(6, 0, 0, 0);
        _selection = new Border { Child = Ui.Row(_fileIcon, _previewLabel), Padding = new Thickness(9, 7, 9, 7), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_selection, 1); previewBody.Children.Add(_selection); _preview.Child = previewBody;
        Children.Add(new Border { Background = Ui.Brush("#343938"), CornerRadius = new CornerRadius(6), Child = _preview });
        _background.TextChanged += (_, _) => DraftChanged(); _accent.TextChanged += (_, _) => DraftChanged(); Paint();
    }

    private TextBox ColorField(Grid fields, string label, string initial, int column, string id)
    {
        var stack = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 0 ? 6 : 0, 0) }; Ui.Label(stack, label);
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(36) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var box = new TextBox { Text = initial, Height = 32, Margin = new Thickness(6, 0, 0, 0), MaxLength = 16 };
        AutomationProperties.SetAutomationId(box, id + "-hex"); AutomationProperties.SetName(box, label + " · HEX"); box.ToolTip = "#RRGGBB";
        var swatch = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("#FFFFFF", .2) };
        var button = Ui.IconButton(swatch, () =>
        {
            var chosen = ColorPickerWindow.Pick(Window.GetWindow(this), label, box.Text);
            if (chosen != null) box.Text = chosen;
        }, L.Format("Выбрать цвет: {0}", label), id + "-picker"); button.Width = 36; button.Height = 32;
        void UpdateField()
        {
            var valid = ColorPickerWindow.TryHex(box.Text, out _, out var color);
            swatch.Background = valid ? new SolidColorBrush(color) : Brushes.Transparent;
            box.BorderBrush = valid ? Ui.Line : Ui.Brush("#D6747D");
        }
        box.TextChanged += (_, _) => UpdateField(); UpdateField();
        row.Children.Add(button); Grid.SetColumn(box, 1); row.Children.Add(box); stack.Children.Add(row); Grid.SetColumn(stack, column); fields.Children.Add(stack); return box;
    }

    internal void SetColors(string background, string accent)
    {
        _changing = true;
        try { _background.Text = background; _accent.Text = accent; }
        finally { _changing = false; }
        DraftChanged();
    }
    internal void SetPreviewOpacity(double opacity) { _opacity = Math.Clamp(opacity, .15, 1); Paint(); }
    internal void SetPreviewSurface(SurfaceStyle surface) { _surface = surface; Paint(); }
    internal void SetPreviewName(string name) => _previewName.Text = string.IsNullOrWhiteSpace(name) ? L.Text("Группа") : name;
    internal void SetPreviewIcon(string id) { _previewIconId = id; Paint(); }
    private void DraftChanged() { if (_changing) return; Paint(); ColorsChanged?.Invoke(this, EventArgs.Empty); }
    private void Paint()
    {
        if (ColorPickerWindow.TryHex(_background.Text, out _, out var background)) _validBackground = background;
        if (ColorPickerWindow.TryHex(_accent.Text, out _, out var accent)) _validAccent = accent;
        _error.Text = IsValid ? "" : ValidationMessage; _error.Visibility = IsValid ? Visibility.Collapsed : Visibility.Visible;
        var ink = new SolidColorBrush(GroupColorAppearance.Text(_validBackground)); var backgroundBrush = new SolidColorBrush(_validBackground) { Opacity = _opacity };
        var accentBrush = new SolidColorBrush(_validAccent);
        _preview.Background = backgroundBrush; _preview.BorderBrush = _surface == SurfaceStyle.Outline ? new SolidColorBrush(_validAccent) { Opacity = .65 } : new SolidColorBrush(ink.Color) { Opacity = .18 };
        _preview.BorderThickness = new Thickness(_surface == SurfaceStyle.Soft ? 0 : 1);
        _previewName.Foreground = _previewTab.Foreground = _previewLabel.Foreground = ink;
        _previewIcon.Source = Brand.Icon(_previewIconId, accentBrush, 16).Source; _fileIcon.Source = Brand.Icon("file", accentBrush, 16).Source;
        _activeTab.BorderBrush = accentBrush;
        _selection.Background = new SolidColorBrush(_validAccent) { Opacity = 0x40 / 255d }; _selection.BorderBrush = new SolidColorBrush(_validAccent) { Opacity = 0x70 / 255d };
        foreach (var (palette, button) in _choices)
        {
            var selected = IsValid && string.Equals(BackgroundHex, palette.Background, StringComparison.OrdinalIgnoreCase) && string.Equals(AccentHex, palette.Accent, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = selected ? Ui.FocusAccent : Ui.Line; button.BorderThickness = new Thickness(selected ? 2 : 1);
            AutomationProperties.SetHelpText(button, selected ? L.Text("Выбрано") : "");
        }
    }
}
