namespace WalleFences.Core;

public static class OpeningSizes
{
    public static (double Width, double Height) Dimensions(OpeningSize size) => size switch
    {
        OpeningSize.Small => (300, 240), OpeningSize.Medium => (440, 380),
        OpeningSize.Wide => (720, 380), OpeningSize.Tall => (380, 640),
        OpeningSize.Sidebar => (320, 760), _ => throw new ArgumentOutOfRangeException(nameof(size))
    };
    public static void Apply(Fence fence, OpeningSize size, DisplayArea display)
    {
        if (size == OpeningSize.Custom) { fence.OpeningSize = size; return; }
        EdgeLayout.UseCustomSpan(fence, display);
        var dimensions = Dimensions(size);
        fence.Width = Math.Min(dimensions.Width, display.Width / display.Scale);
        fence.Height = Math.Min(dimensions.Height, display.Height / display.Scale);
        fence.OpeningSize = size;
        DisplayLayout.Clamp(fence, display);
        DisplayLayout.Capture(fence, [display]);
    }
}

public static class FocusClock
{
    public static int Remaining(DesktopWidget widget, DateTimeOffset now) => widget.FocusEndsAt is { } end
        ? (int)Math.Clamp(Math.Ceiling((end - now).TotalSeconds), 0, 180 * 60)
        : Math.Clamp(widget.RemainingSeconds, 0, 180 * 60);
    public static void Start(DesktopWidget widget, DateTimeOffset now)
    {
        var remaining = Remaining(widget, now);
        if (remaining == 0) remaining = Math.Clamp(widget.FocusMinutes, 1, 180) * 60;
        widget.RemainingSeconds = remaining; widget.FocusEndsAt = now.AddSeconds(remaining);
    }
    public static void Pause(DesktopWidget widget, DateTimeOffset now)
    { widget.RemainingSeconds = Remaining(widget, now); widget.FocusEndsAt = null; }
    public static void Reset(DesktopWidget widget)
    { widget.FocusEndsAt = null; widget.RemainingSeconds = Math.Clamp(widget.FocusMinutes, 1, 180) * 60; }
}

public readonly record struct HotkeyGesture(uint Modifiers, uint Key)
{
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 5) return false;
        uint modifiers = 0;
        foreach (var part in parts[..^1])
        {
            uint modifier = part.ToUpperInvariant() switch { "CTRL" => 2, "ALT" => 1, "SHIFT" => 4, "WIN" => 8, _ => 0 };
            if (modifier == 0 || (modifiers & modifier) != 0) return false;
            modifiers |= modifier;
        }
        var keyName = parts[^1].ToUpperInvariant();
        uint key = keyName switch
        {
            "SPACE" => 0x20, "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "HOME" => 0x24, "END" => 0x23, "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
            "ENTER" => 0x0D, "TAB" => 0x09, _ => 0
        };
        if (keyName.Length == 1 && (keyName[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')) key = keyName[0];
        if (keyName.StartsWith('F') && int.TryParse(keyName.AsSpan(1), out var number) && number is >= 1 and <= 24) key = (uint)(0x6F + number);
        // Shift alone must not steal typing and selection from other applications.
        if (key == 0 || key == 0x7B || (modifiers & (1 | 2 | 8)) == 0) return false;
        gesture = new(modifiers, key); return true;
    }
    public static string Normalize(string? text, string fallback = "") => string.IsNullOrWhiteSpace(text) ? "" : TryParse(text, out _) ? text.Trim() : fallback;
}
