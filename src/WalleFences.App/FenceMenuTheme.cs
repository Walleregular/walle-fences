namespace WalleFences.App;

// Desktop menus belong to a group or tab. Their palette must not change the
// manager's shared brushes, even when a submenu is hosted in its own popup.
internal static class FenceMenuTheme
{
    internal static void Apply(ContextMenu menu, Fence fence, FenceTab? tab = null)
    {
        var activeTab = tab ?? fence.Tabs.FirstOrDefault(value => value.Id == fence.ActiveTabId) ?? fence.Tabs.FirstOrDefault();
        var surface = Opaque(Parse(fence.Color, Color.FromRgb(29, 32, 33)));
        var accent = Opaque(Parse(activeTab?.Color ?? fence.Accent, Parse(fence.Accent, Color.FromRgb(160, 233, 192))));
        var ink = ReadableText(surface);
        var focus = accent;
        for (var i = 0; i < 20 && Contrast(focus, surface) < 3; i++) focus = Blend(focus, ink, .12);

        var palette = new Dictionary<string, Brush>
        {
            ["Paper"] = Brush(surface),
            ["Surface"] = Brush(surface),
            ["Sidebar"] = Brush(surface),
            ["Ink"] = Brush(ink),
            ["Muted"] = Brush(Blend(surface, ink, .72)),
            ["Line"] = Brush(Blend(surface, ink, .23)),
            ["Accent"] = Brush(accent),
            ["FocusAccent"] = Brush(focus),
            ["OnAccent"] = Brush(ReadableText(accent)),
            ["Raised"] = Brush(Blend(surface, ink, .06)),
            ["Hover"] = Brush(Blend(Blend(surface, ink, .055), focus, .14)),
            ["AccentWash"] = Brush(Blend(surface, focus, .16))
        };

        Scope(menu, palette);
        menu.SetResourceReference(Control.BackgroundProperty, "Surface");
        menu.SetResourceReference(Control.ForegroundProperty, "Ink");
        menu.SetResourceReference(Control.BorderBrushProperty, "Line");
        ThemeItems(menu.Items, palette);
    }

    private static void ThemeItems(ItemCollection items, IReadOnlyDictionary<string, Brush> palette)
    {
        foreach (var value in items)
        {
            if (value is not FrameworkElement element) continue;
            Scope(element, palette);
            ThemeTooltip(element, palette);
            if (element is not MenuItem item) continue;

            item.SetResourceReference(Control.ForegroundProperty, "Ink");
            // Keep the existing rounded menu template and its hover/focus
            // triggers; DynamicResource now resolves in this item's scope.
            if (item.Header is FrameworkElement header) ThemeContent(header, palette, "Ink");
            if (item.Icon is FrameworkElement icon) ThemeContent(icon, palette, "Muted");
            ThemeItems(item.Items, palette);
        }
    }

    private static void Scope(FrameworkElement element, IReadOnlyDictionary<string, Brush> palette)
    {
        foreach (var pair in palette) element.Resources[pair.Key] = pair.Value;
    }

    private static void ThemeTooltip(FrameworkElement element, IReadOnlyDictionary<string, Brush> palette)
    {
        if (element.ToolTip is not { } content) return;
        var tooltip = content as ToolTip ?? new ToolTip { Content = content };
        Scope(tooltip, palette);
        tooltip.SetResourceReference(Control.ForegroundProperty, "Ink");
        tooltip.SetResourceReference(Control.BackgroundProperty, "Surface");
        tooltip.SetResourceReference(Control.BorderBrushProperty, "Line");
        if (tooltip.Content is FrameworkElement child) ThemeContent(child, palette, "Ink");
        if (!ReferenceEquals(content, tooltip)) element.ToolTip = tooltip;
    }

    private static void ThemeContent(FrameworkElement element, IReadOnlyDictionary<string, Brush> palette, string role)
    {
        Scope(element, palette);
        if (element is Control control) control.SetResourceReference(Control.ForegroundProperty, role);
        else if (element is TextBlock text) text.SetResourceReference(TextBlock.ForegroundProperty, role);
        else if (element is System.Windows.Shapes.Shape shape)
        {
            if (VisibleBrush(shape.Stroke)) shape.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, role);
            if (VisibleBrush(shape.Fill)) shape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, role);
        }
        else if (element is Image { Source: DrawingImage source } image)
        {
            // Brand.Icon captures Ui.Muted directly in its vector drawing.
            // Clone only the drawing owned by this menu, leaving live shared
            // theme brushes and other icons untouched.
            var drawing = source.Drawing.CloneCurrentValue();
            Recolor(drawing, palette[role]);
            image.Source = new DrawingImage(drawing);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<FrameworkElement>()) ThemeContent(child, palette, role);
    }

    private static void Recolor(Drawing drawing, Brush brush)
    {
        if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children) Recolor(child, brush);
        }
        else if (drawing is GeometryDrawing geometry)
        {
            if (VisibleBrush(geometry.Brush)) geometry.Brush = brush;
            if (geometry.Pen is { } pen && VisibleBrush(pen.Brush)) pen.Brush = brush;
        }
    }

    private static bool VisibleBrush(Brush? brush) => brush is not null && brush.Opacity > 0 && (brush is not SolidColorBrush solid || solid.Color.A > 0);

    private static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try { return ColorConverter.ConvertFromString(value) is Color color ? color : fallback; }
        catch (FormatException) { return fallback; }
        catch (ArgumentException) { return fallback; }
        catch (NotSupportedException) { return fallback; }
    }

    private static Color Opaque(Color color) => Color.FromRgb(color.R, color.G, color.B);
    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }

    private static Color Blend(Color basis, Color tint, double amount)
    {
        byte Channel(byte first, byte second) => (byte)Math.Round(first * (1 - amount) + second * amount);
        return Color.FromRgb(Channel(basis.R, tint.R), Channel(basis.G, tint.G), Channel(basis.B, tint.B));
    }

    private static Color ReadableText(Color background) => Contrast(Colors.White, background) >= Contrast(Colors.Black, background) ? Colors.White : Colors.Black;
    private static double Contrast(Color first, Color second)
    {
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
}
