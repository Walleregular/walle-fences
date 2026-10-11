using System.Runtime.CompilerServices;

namespace WalleFences.Core;

/// <summary>Projects edge handles into non-overlapping intervals without changing their requested span.</summary>
public static class EdgeCollisionLayout
{
    private static readonly ConditionalWeakTable<Fence, Projection> Projections = new();

    public sealed class Snapshot
    {
        internal Snapshot(Dictionary<Fence, Captured> projections) => Projections = projections;
        internal Dictionary<Fence, Captured> Projections { get; }
    }

    // Gesture callers restore this baseline before each new pointer position. This prevents neighbours
    // from drifting a little farther on every tick and also lets cancellation restore their projection.
    public static Snapshot Capture(IReadOnlyList<Fence> fences)
    {
        var captured = new Dictionary<Fence, Captured>();
        foreach (var fence in fences)
            captured[fence] = new(Projections.TryGetValue(fence, out var projection) ? projection : null, fence.X, fence.Y);
        return new(captured);
    }

    public static void Restore(Snapshot snapshot)
    {
        foreach (var (fence, captured) in snapshot.Projections)
        {
            Projections.Remove(fence);
            if (captured.Projection != null) Projections.Add(fence, captured.Projection);
            fence.X = captured.X; fence.Y = captured.Y;
        }
    }

    /// <summary>Returns an offset and length in display coordinates, rather than logical DIPs.</summary>
    public static bool TryGetResolvedSpan(Fence fence, DisplayArea display, out double offset, out double length)
    {
        if (Projections.TryGetValue(fence, out var projection) && projection.Group.Display == display &&
            projection.Input.HasSameSpan(Input.From(fence)) && fence.Edge != DockEdge.None)
        {
            offset = projection.Offset; length = projection.Length;
            return true;
        }
        offset = length = 0;
        return false;
    }

    /// <summary>Resolves each set, display and edge independently; returns only changed geometry.</summary>
    public static IReadOnlyList<Fence> Resolve(IReadOnlyList<Fence> fences, IReadOnlyList<DisplayArea> displays, string? preferredFenceId = null)
    {
        if (displays.Count == 0) return [];
        var changed = new List<Fence>();
        var groups = new Dictionary<GroupKey, List<(Fence Fence, DisplayArea Display)>>();
        foreach (var fence in fences)
        {
            if (fence.Edge == DockEdge.None)
            {
                Projections.Remove(fence);
                continue;
            }
            var display = DisplayLayout.Select(fence, displays);
            var key = new GroupKey(fence.PageId, display.Id.ToUpperInvariant(), fence.Edge);
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = [];
            group.Add((fence, display));
        }

        foreach (var group in groups.Values)
        {
            var display = group[0].Display;
            var inputs = group.Select(item => Input.From(item.Fence)).ToArray();
            GroupState? unchanged = null;
            if (Projections.TryGetValue(group[0].Fence, out var first) && first.Group.Matches(display, inputs) &&
                group.All(item => Projections.TryGetValue(item.Fence, out var projection) && ReferenceEquals(projection.Group, first.Group)))
                unchanged = first.Group;

            // Rebuilds after a preferred edit must retain that edit's resolved order and positions.
            if (unchanged != null)
            {
                foreach (var (fence, _) in group)
                {
                    var bounds = EdgeLayout.Bounds(fence, display, true);
                    if (Different(fence.X, bounds.X) || Different(fence.Y, bounds.Y))
                    {
                        fence.X = bounds.X; fence.Y = bounds.Y;
                        changed.Add(fence);
                    }
                }
                continue;
            }

            var available = group[0].Fence.Edge is DockEdge.Left or DockEdge.Right ? display.Height : display.Width;
            if (!double.IsFinite(available) || available <= 0 || !double.IsFinite(display.Scale) || display.Scale <= 0) continue;
            var state = new GroupState(display, inputs);
            var rows = new List<Row>(group.Count);
            foreach (var (fence, _) in group)
            {
                var input = Input.From(fence);
                RequestedSpan(fence, display, out var wanted, out var length);
                Projection? previous = null;
                if (Projections.TryGetValue(fence, out var cached))
                {
                    previous = cached;
                    // An unrelated edit keeps each neighbour near its already visible handle. Its full
                    // desired length still participates, so temporary compression is reversible.
                    if (cached.Group.Display == display && cached.Input.HasSameSpan(input)) wanted = cached.Offset;
                }
                var savedOffset = fence.Placement is { } placement && placement.MonitorId.Equals(display.Id, StringComparison.OrdinalIgnoreCase)
                    ? (fence.Edge is DockEdge.Left or DockEdge.Right ? placement.OffsetY : placement.OffsetX) * display.Scale
                    : wanted;
                rows.Add(new(fence, input, wanted, length, previous, double.IsFinite(savedOffset) ? savedOffset : wanted, rows.Count));
            }
            var requestedTotal = rows.Sum(row => row.Length);
            var compression = requestedTotal > available ? available / requestedTotal : 1;
            foreach (var row in rows) row.Length *= compression;
            rows.Sort((left, right) =>
            {
                var comparison = left.Wanted.CompareTo(right.Wanted);
                if (comparison != 0) return comparison;
                // A new or moved handle at the same start pushes existing neighbours forward.
                var leftPreferred = left.Fence.Id == preferredFenceId;
                var rightPreferred = right.Fence.Id == preferredFenceId;
                if (leftPreferred != rightPreferred) return leftPreferred ? -1 : 1;
                // Full spans and duplicate block slots have identical requested starts. Their saved
                // placement breaks ties so a restart retains the visible order without changing mode.
                comparison = left.SavedOffset.CompareTo(right.SavedOffset);
                if (comparison != 0) return comparison;
                return left.Order.CompareTo(right.Order);
            });
            Pack(rows, available, preferredFenceId);

            foreach (var row in rows)
            {
                var before = EdgeLayout.Bounds(row.Fence, display, true);
                var next = new Projection(state, row.Input, row.Offset, row.Length);
                Projections.Remove(row.Fence); Projections.Add(row.Fence, next);
                var after = EdgeLayout.Bounds(row.Fence, display, true);
                next.Expanded = after;
                var spanChanged = row.Previous != null
                    ? Different(row.Previous.Offset, row.Offset) || Different(row.Previous.Length, row.Length)
                    : Different(row.Wanted, row.Offset) || Different(RequestedLength(row.Fence, display), row.Length);
                if (spanChanged || before != after || row.Previous != null && row.Previous.Expanded != after ||
                    Different(row.Fence.X, after.X) || Different(row.Fence.Y, after.Y))
                    changed.Add(row.Fence);
                row.Fence.X = after.X; row.Fence.Y = after.Y;
            }
        }
        return changed;
    }

    private static void Pack(List<Row> rows, double available, string? preferredFenceId)
    {
        var preferred = rows.FindIndex(row => row.Fence.Id == preferredFenceId);
        if (preferred < 0)
        {
            var cursor = 0d;
            foreach (var row in rows)
            {
                row.Offset = Math.Max(row.Wanted, cursor);
                cursor = row.Offset + row.Length;
            }
            cursor = available;
            for (var index = rows.Count - 1; index >= 0; index--)
            {
                var row = rows[index];
                row.Offset = Math.Min(row.Offset, cursor - row.Length);
                cursor = row.Offset;
            }
            return;
        }

        var prefix = new double[rows.Count + 1];
        for (var index = 0; index < rows.Count; index++) prefix[index + 1] = prefix[index] + rows[index].Length;
        var anchor = rows[preferred];
        var minimum = prefix[preferred];
        var maximum = Math.Max(minimum, available - (prefix[rows.Count] - prefix[preferred]));
        anchor.Offset = Math.Clamp(anchor.Wanted, minimum, maximum);
        var boundary = anchor.Offset;
        for (var index = preferred - 1; index >= 0; index--)
        {
            var row = rows[index];
            row.Offset = Math.Max(prefix[index], Math.Min(row.Wanted, boundary - row.Length));
            boundary = row.Offset;
        }
        boundary = anchor.Offset + anchor.Length;
        for (var index = preferred + 1; index < rows.Count; index++)
        {
            var row = rows[index];
            var latest = available - (prefix[rows.Count] - prefix[index]);
            row.Offset = Math.Min(Math.Max(row.Wanted, boundary), Math.Max(boundary, latest));
            boundary = row.Offset + row.Length;
        }
    }

    private static double RequestedLength(Fence fence, DisplayArea display)
    {
        RequestedSpan(fence, display, out _, out var length);
        return length;
    }

    private static void RequestedSpan(Fence fence, DisplayArea display, out double offset, out double length)
    {
        var available = fence.Edge is DockEdge.Left or DockEdge.Right ? display.Height : display.Width;
        if (fence.EdgeSpanMode != EdgeSpanMode.Custom)
        {
            var count = fence.EdgeSpanMode == EdgeSpanMode.Full ? 1 : Math.Clamp(fence.EdgeBlockCount, 1, 12);
            var index = fence.EdgeSpanMode == EdgeSpanMode.Full ? 0 : Math.Clamp(fence.EdgeBlockIndex, 0, count - 1);
            offset = available * index / count;
            length = available * (index + 1) / count - offset;
            return;
        }
        length = Math.Min(double.IsFinite(fence.EdgeLength) && fence.EdgeLength > 0 ? fence.EdgeLength * display.Scale : 160 * display.Scale, available);
        var desired = double.IsFinite(fence.EdgeOffset) ? fence.EdgeOffset * display.Scale : 0;
        // The stored offset can describe a compressed neighbour. Packing constrains it using the
        // projected length; constraining against its larger requested length would lose saved order.
        offset = Math.Clamp(desired, 0, available);
    }

    private static bool Different(double left, double right) => Math.Abs(left - right) > .000001;

    private readonly record struct GroupKey(string PageId, string DisplayId, DockEdge Edge);
    internal sealed record Input(Fence Fence, string PageId, DockEdge Edge, EdgeSpanMode Mode, int Count, int Index,
        double Offset, double Length, double Width, double Height, double Thickness)
    {
        internal static Input From(Fence fence) => new(fence, fence.PageId, fence.Edge, fence.EdgeSpanMode,
            fence.EdgeBlockCount, fence.EdgeBlockIndex, fence.EdgeOffset, fence.EdgeLength, fence.Width, fence.Height, fence.EdgeThickness);
        internal bool HasSameSpan(Input other) => ReferenceEquals(Fence, other.Fence) && PageId == other.PageId && Edge == other.Edge &&
            Mode == other.Mode && Count == other.Count && Index == other.Index && Offset == other.Offset && Length == other.Length;
    }
    internal sealed record GroupState(DisplayArea Display, Input[] Inputs)
    {
        internal bool Matches(DisplayArea display, Input[] inputs) => Display == display && Inputs.SequenceEqual(inputs);
    }
    internal sealed record Projection(GroupState Group, Input Input, double Offset, double Length)
    {
        internal LayoutRect Expanded { get; set; }
    }
    internal sealed record Captured(Projection? Projection, double X, double Y);
    private sealed class Row(Fence fence, Input input, double wanted, double length, Projection? previous, double savedOffset, int order)
    {
        internal Fence Fence { get; } = fence;
        internal Input Input { get; } = input;
        internal double Wanted { get; } = wanted;
        internal double Length { get; set; } = length;
        internal double Offset { get; set; }
        internal Projection? Previous { get; } = previous;
        internal double SavedOffset { get; } = savedOffset;
        internal int Order { get; } = order;
    }
}
