namespace WalleFences.Core;

// Geometry only: the host supplies monotonic timestamps and applies the sampled rectangle.
public sealed class RevealMotion
{
    public const int OpeningDurationMs = 180;
    public const int ClosingDurationMs = 140;
    public bool IsRunning { get; private set; }
    public LayoutRect Target { get; private set; }
    private LayoutRect _from, _current;
    private long _startedAt, _sampledAt;
    private int _durationMs;
    private bool _initialized;

    public bool Start(LayoutRect current, LayoutRect target, long now, int durationMs)
    {
        Validate(current); Validate(target);
        if (_initialized && Target == target) return false;
        // A reversal starts at the present interpolation, even when the caller's rectangle is stale.
        var wasRunning = IsRunning;
        var origin = wasRunning ? Sample(now) : current;
        var startedAt = wasRunning ? Math.Max(now, _sampledAt) : now;
        if (durationMs <= 0 || origin == target)
        {
            Reset(target);
            return origin != target;
        }
        _from = _current = origin; Target = target; _durationMs = durationMs;
        _startedAt = _sampledAt = startedAt; _initialized = IsRunning = true;
        return true;
    }

    public LayoutRect Sample(long now)
    {
        if (!IsRunning) return _current;
        _sampledAt = Math.Max(now, _sampledAt);
        var elapsed = unchecked((ulong)_sampledAt - (ulong)_startedAt);
        if (elapsed >= (ulong)_durationMs)
        {
            _current = Target; IsRunning = false;
            return _current;
        }
        var remaining = 1 - elapsed / (double)_durationMs;
        var amount = 1 - remaining * remaining * remaining;
        static double Lerp(double from, double to, double amount) => from * (1 - amount) + to * amount;
        _current = new(Lerp(_from.X, Target.X, amount), Lerp(_from.Y, Target.Y, amount), Lerp(_from.Width, Target.Width, amount), Lerp(_from.Height, Target.Height, amount));
        return _current;
    }

    public void Reset(LayoutRect rectangle)
    {
        Validate(rectangle);
        _from = _current = Target = rectangle; IsRunning = false; _initialized = true;
        _startedAt = _sampledAt = 0; _durationMs = 0;
    }

    private static void Validate(LayoutRect rectangle)
    {
        if (!double.IsFinite(rectangle.X) || !double.IsFinite(rectangle.Y) || !double.IsFinite(rectangle.Width) || !double.IsFinite(rectangle.Height) || rectangle.Width < 0 || rectangle.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(rectangle), "Reveal geometry must be finite with nonnegative dimensions.");
    }
}
