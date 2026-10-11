namespace WalleFences.Core;

public enum SortingPreviewStatus { Ready, AlreadyAdded, NoRule, InvalidTarget, MissingSource, MissingReference, ReferenceError }
public enum SortingTargetProblem { GroupMissing, OtherSet, ContentLocked, TabMissing, PortalTab }
public sealed record SortingRuleIssue(SortingRule Rule, SortingTargetProblem Problem);
public sealed record SortingDestination(string FenceId, string TabId, string FenceName, string TabName);
public sealed record SortingPreviewRow(FileEntry Entry, ImportedReference? Reference, SortingPreviewStatus Status,
    SortingRule? Rule = null, SortingDestination? Destination = null,
    IReadOnlyList<SortingRuleIssue>? SkippedRules = null, bool DuplicateInPreview = false, string? Error = null);
public sealed record SortingPreviewApplyResult(int Added, int Skipped);

public static partial class RuleEngine
{
    // The preview owns no workspace changes. Both automatic sorting and reviewed
    // sorting use this same first matching valid rule policy.
    public static IReadOnlyList<SortingPreviewRow> Preview(Workspace workspace, IEnumerable<FileEntry> entries,
        DateTime? now = null, Func<FileEntry, ImportedReference>? resolve = null)
    {
        var rows = new List<SortingPreviewRow>(); var clock = now ?? DateTime.UtcNow;
        var owned = new Dictionary<string, SortingDestination>(StringComparer.OrdinalIgnoreCase);
        foreach (var fence in workspace.Fences.Where(f => f.PageId == workspace.ActivePageId))
            foreach (var tab in fence.Tabs.Where(t => string.IsNullOrEmpty(t.PortalPath)))
                foreach (var path in tab.Items)
                    owned.TryAdd(PathExtensions.Key(path), Destination(fence, tab));
        var planned = new Dictionary<string, SortingDestination>(StringComparer.OrdinalIgnoreCase);
        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rules = workspace.Rules.Where(rule => rule.Enabled).ToArray();
        foreach (var entry in entries)
        {
            try
            {
                var source = PathExtensions.Key(entry.Path);
                if (!seenSources.Add(source)) continue;
                if (!entry.Exists) { rows.Add(new(entry, null, SortingPreviewStatus.MissingSource)); continue; }
                if (owned.TryGetValue(source, out var existing))
                { rows.Add(new(entry, null, SortingPreviewStatus.AlreadyAdded, Destination: existing)); continue; }
                var reference = resolve?.Invoke(entry) ?? new ImportedReference(entry.Path, entry.Path);
                var targetPath = PathExtensions.Key(reference.Path);
                if (owned.TryGetValue(targetPath, out existing))
                { rows.Add(new(entry, reference, SortingPreviewStatus.AlreadyAdded, Destination: existing)); continue; }
                if (planned.TryGetValue(targetPath, out existing))
                { rows.Add(new(entry, reference, SortingPreviewStatus.AlreadyAdded, Destination: existing, DuplicateInPreview: true)); continue; }
                if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
                { rows.Add(new(entry, reference, SortingPreviewStatus.MissingReference)); continue; }
                var skipped = new List<SortingRuleIssue>(); SortingPreviewRow? row = null;
                foreach (var rule in rules)
                {
                    if (!Matches(rule, entry, clock)) continue;
                    var fence = workspace.Fences.FirstOrDefault(f => f.Id == rule.FenceId);
                    var tab = fence?.Tabs.FirstOrDefault(t => t.Id == rule.TabId);
                    var problem = TargetProblem(workspace, fence, tab);
                    if (problem != null) { skipped.Add(new(rule, problem.Value)); continue; }
                    var destination = Destination(fence!, tab!);
                    row = new(entry, reference, SortingPreviewStatus.Ready, rule, destination, skipped);
                    planned.Add(targetPath, destination); break;
                }
                var unavailable = skipped.FirstOrDefault()?.Rule;
                var intendedFence = unavailable == null ? null : workspace.Fences.FirstOrDefault(fence => fence.Id == unavailable.FenceId);
                var intendedTab = intendedFence?.Tabs.FirstOrDefault(tab => tab.Id == unavailable?.TabId);
                rows.Add(row ?? new(entry, reference, skipped.Count > 0 ? SortingPreviewStatus.InvalidTarget : SortingPreviewStatus.NoRule,
                    Rule: unavailable, Destination: intendedFence != null && intendedTab != null ? Destination(intendedFence, intendedTab) : null, SkippedRules: skipped));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { rows.Add(new(entry, null, SortingPreviewStatus.ReferenceError, Error: ex.Message)); }
        }
        return rows;
    }

    // Apply only rows actually reviewed. Re-read files and shortcut invocation,
    // then require the same current rule, group, tab and target as in the plan.
    // Newly appearing desktop files and edited rules wait for a fresh preview.
    public static SortingPreviewApplyResult ApplyPreview(Workspace workspace, IReadOnlyList<SortingPreviewRow> reviewed,
        Func<FileEntry, ImportedReference>? resolve = null)
    {
        var ready = reviewed.Where(row => row.Status == SortingPreviewStatus.Ready).ToArray();
        var fresh = Preview(workspace, ready.Select(row => FileEntry.FromPath(row.Entry.Path)), resolve: resolve)
            .ToDictionary(row => PathExtensions.Key(row.Entry.Path), StringComparer.OrdinalIgnoreCase);
        var added = 0; var skipped = 0;
        foreach (var previous in ready)
        {
            if (!fresh.TryGetValue(PathExtensions.Key(previous.Entry.Path), out var current)
                || current.Status != SortingPreviewStatus.Ready || !SamePlan(previous, current))
            { skipped++; continue; }
            var fence = workspace.Fences.FirstOrDefault(f => f.Id == current.Destination!.FenceId);
            var tab = fence?.Tabs.FirstOrDefault(t => t.Id == current.Destination!.TabId);
            if (TargetProblem(workspace, fence, tab) != null || current.Reference == null) { skipped++; continue; }
            var result = FileCatalog.ImportReferences(tab!, [current.Reference]);
            added += result.Added; if (result.Added == 0) skipped++;
        }
        return new(added, skipped);
    }

    private static bool SamePlan(SortingPreviewRow a, SortingPreviewRow b)
        => a.Destination?.FenceId == b.Destination?.FenceId && a.Destination?.TabId == b.Destination?.TabId
            && a.Rule != null && b.Rule != null && a.Rule.Id == b.Rule.Id && a.Rule.Enabled == b.Rule.Enabled
            && a.Rule.Kind == b.Rule.Kind && a.Rule.Pattern == b.Rule.Pattern && a.Rule.DesktopCategory == b.Rule.DesktopCategory
            && a.Rule.FenceId == b.Rule.FenceId && a.Rule.TabId == b.Rule.TabId
            && a.Reference != null && b.Reference != null && PathExtensions.Same(a.Reference.Path, b.Reference.Path)
            && a.Reference.Launch == b.Reference.Launch;

    private static SortingDestination Destination(Fence fence, FenceTab tab) => new(fence.Id, tab.Id, fence.Name, tab.Name);
    private static SortingTargetProblem? TargetProblem(Workspace workspace, Fence? fence, FenceTab? tab)
        => fence == null ? SortingTargetProblem.GroupMissing
            : fence.PageId != workspace.ActivePageId ? SortingTargetProblem.OtherSet
            : fence.ContentLocked ? SortingTargetProblem.ContentLocked
            : tab == null ? SortingTargetProblem.TabMissing
            : !string.IsNullOrEmpty(tab.PortalPath) ? SortingTargetProblem.PortalTab : null;
}
