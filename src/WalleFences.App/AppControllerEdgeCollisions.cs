namespace WalleFences.App;

internal sealed record EdgeCollisionGesture(EdgeCollisionLayout.Snapshot Baseline, Fence[] Fences);

internal sealed partial class AppController
{
    private static bool EdgePlacementChanged(Fence fence, Fence? before) => before == null
        || before.PageId != fence.PageId || before.Edge != fence.Edge || before.EdgeOffset != fence.EdgeOffset
        || before.EdgeLength != fence.EdgeLength || before.EdgeSpanMode != fence.EdgeSpanMode
        || before.EdgeBlockCount != fence.EdgeBlockCount || before.EdgeBlockIndex != fence.EdgeBlockIndex
        || before.Width != fence.Width || before.Height != fence.Height || before.Placement?.MonitorId != fence.Placement?.MonitorId;

    private void ResolveEdgeCollisions(Workspace previous)
    {
        var displays = Displays();
        var edited = Workspace.Fences.Where(fence => EdgePlacementChanged(fence, previous.Fences.FirstOrDefault(before => before.Id == fence.Id))).ToArray();
        var origins = previous.Fences.Where(before => !Workspace.Fences.Any(fence => fence.Id == before.Id)
            || edited.Any(fence => fence.Id == before.Id)).ToArray();
        var scopes = edited.Concat(origins).Where(fence => fence.Edge != DockEdge.None)
            .Select(fence => (fence.PageId, Monitor: DisplayLayout.Select(fence, displays).Id, fence.Edge)).ToHashSet();
        if (scopes.Count == 0) return;
        var anchors = edited.Where(fence => fence.Edge != DockEdge.None).ToArray();
        var preferred = anchors.Length == 1 ? anchors[0].Id : null;
        EdgeCollisionLayout.Resolve(Workspace.Fences, displays, preferred);
        var affected = Workspace.Fences.Where(fence => fence.Edge != DockEdge.None
            && scopes.Contains((fence.PageId, DisplayLayout.Select(fence, displays).Id, fence.Edge))).ToArray();
        PersistEdgeCollisionPlacements(preferred, affected);
    }

    // Only an explicit edit commits a shifted handle. Rebuild/display fallback remains a
    // projection, preserving the original monitor when it is temporarily disconnected.
    private void PersistEdgeCollisionPlacements(string? preferredFenceId, IReadOnlyList<Fence> fences)
    {
        var displays = Displays();
        var resolved = fences.Where(fence => fence.Edge != DockEdge.None).Select(fence =>
        {
            var display = DisplayLayout.Select(fence, displays);
            var visibleHome = fence.Placement == null || displays.Any(item => item.Id.Equals(fence.Placement.MonitorId, StringComparison.OrdinalIgnoreCase));
            var hasSpan = EdgeCollisionLayout.TryGetResolvedSpan(fence, display, out var offset, out _);
            return (Fence: fence, Display: display, VisibleHome: visibleHome, HasSpan: hasSpan, Offset: offset);
        }).ToArray();
        // Capture every result before changing inputs: each projection has a signature.
        foreach (var item in resolved.Where(item => item.VisibleHome && item.HasSpan && item.Fence.EdgeSpanMode == EdgeSpanMode.Custom))
            item.Fence.EdgeOffset = item.Offset / item.Display.Scale;
        EdgeCollisionLayout.Resolve(Workspace.Fences, displays, preferredFenceId);
        foreach (var item in resolved.Where(item => item.VisibleHome)) DisplayLayout.Capture(item.Fence, displays, item.Display);
    }

    internal EdgeCollisionGesture? BeginEdgeCollisionGesture(Fence fence)
    {
        if (fence.Edge == DockEdge.None) return null;
        var displays = Displays(); var display = DisplayLayout.Select(fence, displays);
        var neighbours = Workspace.Fences.Where(other => other.PageId == fence.PageId && other.Edge == fence.Edge
            && DisplayLayout.Select(other, displays).Id == display.Id).ToArray();
        return new(EdgeCollisionLayout.Capture(neighbours), neighbours);
    }

    internal void RestoreEdgeCollisionGesture(EdgeCollisionGesture? gesture, bool applyBounds = true)
    {
        if (gesture == null) return;
        EdgeCollisionLayout.Restore(gesture.Baseline);
        if (applyBounds) { ApplyEdgeCollisionWindows(gesture.Fences); ScheduleSave(); }
    }

    internal void PreviewEdgeCollisionGesture(Fence fence, EdgeCollisionGesture? gesture)
    {
        var changed = EdgeCollisionLayout.Resolve(Workspace.Fences, Displays(), fence.Id);
        // Baseline neighbours may no longer be displaced at this pointer position. Update
        // them too so their HWND returns to the baseline, rather than keeping the last tick.
        ApplyEdgeCollisionWindows(gesture == null ? changed : gesture.Fences.Concat(changed));
    }

    private void ApplyEdgeCollisionWindows(IEnumerable<Fence> fences)
    {
        var ids = fences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var window in Windows.Where(window => ids.Contains(window.Fence.Id))) window.ApplyMonitorBounds();
    }
}
