namespace WalleFences.App;

// Native groups have their own background, independent of the manager's theme.
internal static class GroupColorAppearance
{
    private static readonly Color LightText = Color.FromRgb(238, 241, 238);
    private static readonly Color DarkText = Color.FromRgb(22, 35, 29);
    internal static Color Text(Color background)
    {
        var preferred = Contrast(LightText, background) >= Contrast(DarkText, background) ? LightText : DarkText;
        if (Contrast(preferred, background) >= 4.5) return preferred;
        return Contrast(Colors.White, background) >= Contrast(Colors.Black, background) ? Colors.White : Colors.Black;
    }
    internal static Color Muted(Color background)
    {
        var foreground = Text(background);
        var preferred = RelativeLuminance(foreground) > .5 ? Color.FromRgb(161, 170, 164) : Color.FromRgb(89, 102, 94);
        if (Contrast(preferred, background) >= 4.5) return preferred;
        // Keep secondary text readable on custom colours near the light/dark crossover.
        var low = 0d; var high = 1d;
        for (var i = 0; i < 12; i++)
        {
            var amount = (low + high) / 2;
            if (Contrast(Mix(background, foreground, amount), background) >= 4.5) high = amount; else low = amount;
        }
        return Mix(background, foreground, high);
    }
    internal static double RelativeLuminance(Color color)
    {
        static double Linear(byte channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    internal static double Contrast(Color first, Color second)
    {
        var a = RelativeLuminance(first); var b = RelativeLuminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static Color Mix(Color background, Color foreground, double amount)
    {
        byte Channel(byte bg, byte fg) => (byte)Math.Clamp(Math.Round(bg + (fg - bg) * amount), 0, 255);
        return Color.FromRgb(Channel(background.R, foreground.R), Channel(background.G, foreground.G), Channel(background.B, foreground.B));
    }
}
