namespace WalleFences.App;

internal sealed partial class AppController
{
    private Native.Rect[] _gameModeFullscreenMonitors = [];
    private nint _gameModeForeground;
    private long _gameModeSampleAt = long.MinValue;
    internal event Action? GameModeStateChanged;

    internal string GameModeStatus => !Workspace.Preferences.GameMode
        ? L.Text("Выключен · группы раскрываются по обычным правилам")
        : _gameModeFullscreenMonitors.Length > 0
            ? L.Text("Активен · автоматическое раскрытие приостановлено")
            : L.Text("Включён · ожидает полноэкранное приложение");

    internal void UpdateGameModeState()
    {
        if (_disposed) return;
        Native.Rect[] monitors = [];
        if (Workspace.Preferences.GameMode)
        {
            var now = Environment.TickCount64;
            var foreground = Native.GetForegroundWindow();
            // React immediately to a focus change, but avoid inspecting native geometry on every
            // 50 ms pointer tick while the same application remains in front.
            if (foreground == _gameModeForeground && _gameModeSampleAt != long.MinValue && now - _gameModeSampleAt < 250) return;
            _gameModeForeground = foreground; _gameModeSampleAt = now;
            if (Native.TryGetForeignFullscreenBounds(foreground, out var fullscreen))
                monitors = System.Windows.Forms.Screen.AllScreens.Select(screen => screen.Bounds)
                    .Where(monitor => fullscreen.Left <= monitor.Left + 2 && fullscreen.Top <= monitor.Top + 2
                        && fullscreen.Right >= monitor.Right - 2 && fullscreen.Bottom >= monitor.Bottom - 2)
                    .Select(monitor => new Native.Rect { Left = monitor.Left, Top = monitor.Top, Right = monitor.Right, Bottom = monitor.Bottom }).ToArray();
        }
        if (_gameModeFullscreenMonitors.SequenceEqual(monitors)) return;
        _gameModeFullscreenMonitors = monitors; GameModeStateChanged?.Invoke();
    }

    internal bool AutomaticRevealBlocked(Fence fence)
    {
        if (!Workspace.Preferences.GameMode || IsPeek || _gameModeFullscreenMonitors.Length == 0) return false;
        var display = DisplayLayout.Select(fence, Displays()); var scale = GlobalScale;
        // The native detector first verifies full physical monitor coverage. Here the persisted
        // group home chooses the affected monitor; groups on another display keep working.
        var centerX = (display.X + display.Width / 2) / scale; var centerY = (display.Y + display.Height / 2) / scale;
        return _gameModeFullscreenMonitors.Any(bounds => centerX >= bounds.Left && centerX < bounds.Right && centerY >= bounds.Top && centerY < bounds.Bottom);
    }

    internal bool AutomaticRevealBlocked(Native.Point pointer)
    {
        if (!Workspace.Preferences.GameMode || IsPeek || _gameModeFullscreenMonitors.Length == 0) return false;
        return _gameModeFullscreenMonitors.Any(bounds => pointer.X >= bounds.Left && pointer.X < bounds.Right && pointer.Y >= bounds.Top && pointer.Y < bounds.Bottom);
    }

    internal void RefreshGameModePreference()
    {
        _gameModeSampleAt = long.MinValue; UpdateGameModeState();
        foreach (var window in Windows) window.ApplyGameModeGuard(AutomaticRevealBlocked(window.Fence));
        _cornerReveal.Reset(); _cornerNeedsLeave = false;
        GameModeStateChanged?.Invoke(); ScheduleSave();
    }
}
