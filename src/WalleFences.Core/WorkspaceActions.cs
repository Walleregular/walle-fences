namespace WalleFences.Core;

public static class WorkspaceActions
{
    public static void MoveTab(Workspace workspace, string sourceId, string tabId, string targetId, int index)
    {
        var source = workspace.Fences.First(f => f.Id == sourceId); var target = workspace.Fences.First(f => f.Id == targetId);
        RequireEditable(source); RequireEditable(target);
        var tab = source.Tabs.First(t => t.Id == tabId);
        source.Tabs.Remove(tab); target.Tabs.Insert(Math.Clamp(index, 0, target.Tabs.Count), tab); target.ActiveTabId = tab.Id;
        if (source != target)
        {
            foreach (var rule in workspace.Rules.Where(r => r.TabId == tabId)) rule.FenceId = target.Id;
            if (source.Tabs.Count == 0) workspace.Fences.Remove(source);
            else if (source.ActiveTabId == tabId) source.ActiveTabId = source.Tabs[0].Id;
        }
    }
    public static void RequireEditable(Fence fence)
    {
        if (fence.ContentLocked) throw new InvalidOperationException(L.Text("Содержимое группы защищено. Отключите защиту в настройках группы."));
    }
    public static void ValidateContentChanges(Workspace before, Workspace after)
    {
        foreach (var source in before.Fences.Where(f => f.ContentLocked))
        {
            var target = after.Fences.FirstOrDefault(f => f.Id == source.Id);
            if (target is { ContentLocked: false }) continue;
            if (target == null || ContentSignature(source) != ContentSignature(target)) RequireEditable(source);
        }
    }
    private static string ContentSignature(Fence fence) => System.Text.Json.JsonSerializer.Serialize(fence.Tabs.Select(t => new { t.Id, t.Name, t.Color, t.PortalPath, t.Items, t.ReferenceLaunches, Widgets = t.Widgets.Select(w => new { w.Id, w.Kind, w.Title, w.Text, w.FocusMinutes, w.Use24HourClock }) }));
    public static void Snap(Fence fence, IEnumerable<Fence> neighbours, DisplayArea display, double gap)
    {
        if (fence.Edge != DockEdge.None) return;
        var width = fence.Width * display.Scale; var height = (fence.ContentCollapsed ? FenceLayout.CollapsedHeight(fence) : fence.Height) * display.Scale;
        var distance = 10 * display.Scale; gap *= display.Scale;
        var xs = new List<double> { display.X + gap, display.Right - width - gap }; var ys = new List<double> { display.Y + gap, display.Bottom - height - gap };
        foreach (var other in neighbours.Where(f => f != fence && f.PageId == fence.PageId && f.Edge == DockEdge.None && f.X >= display.X && f.X < display.Right && f.Y >= display.Y && f.Y < display.Bottom))
        {
            var otherWidth = other.Width * display.Scale; var otherHeight = (other.ContentCollapsed ? FenceLayout.CollapsedHeight(other) : other.Height) * display.Scale;
            if (fence.Y < other.Y + otherHeight + gap + distance && fence.Y + height > other.Y - gap - distance) xs.AddRange([other.X - width - gap, other.X + otherWidth + gap, other.X, other.X + otherWidth - width]);
            if (fence.X < other.X + otherWidth + gap + distance && fence.X + width > other.X - gap - distance) ys.AddRange([other.Y - height - gap, other.Y + otherHeight + gap, other.Y, other.Y + otherHeight - height]);
        }
        var x = xs.OrderBy(v => Math.Abs(v - fence.X)).First(); var y = ys.OrderBy(v => Math.Abs(v - fence.Y)).First();
        if (Math.Abs(x - fence.X) <= distance) fence.X = x; if (Math.Abs(y - fence.Y) <= distance) fence.Y = y;
        DisplayLayout.Clamp(fence, display);
    }
}
