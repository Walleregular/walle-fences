namespace WalleFences.Core;

public sealed record DesktopCategory(string Id, string NameKey, string Color, string Accent, string Extensions = "");
public sealed record DesktopOrganizationResult(int Items, int Groups);

public static class DesktopOrganizer
{
    public static readonly DesktopCategory[] Categories =
    [
        new("applications", "Приложения", "#1D2021", "#A0E9C0", ".lnk .url .appref-ms .exe .msi .bat .cmd .ps1 .rdp"),
        new("documents", "Документы", "#1D2021", "#A7C7FF", ".pdf .doc .docx .xls .xlsx .ppt .pptx .txt .md .csv .odt .ods .rtf .html .htm .mhtml"),
        new("folders", "Папки", "#1D2021", "#E7BD82"),
        new("images", "Изображения", "#1D2021", "#96D5E5", ".png .jpg .jpeg .gif .webp .bmp .tif .tiff .svg .ico .heic"),
        new("archives", "Архивы", "#1D2021", "#C9B3F5", ".zip .rar .7z .tar .gz .bz2 .xz .iso"),
        new("other", "Прочее", "#1D2021", "#C5CED4")
    ];
    public static string Category(FileEntry entry)
    {
        if (entry.IsDirectory) return "folders";
        var extension = entry.Extension.ToLowerInvariant();
        return Categories.FirstOrDefault(c => c.Extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(extension, StringComparer.OrdinalIgnoreCase))?.Id ?? "other";
    }
    // Explicit redistribution changes only references to the supplied desktop items.
    // Unrelated files, tabs, pages and portal content remain intact.
    public static DesktopOrganizationResult Redistribute(Workspace workspace, IEnumerable<FileEntry> entries, Func<FileEntry, ImportedReference>? resolve = null)
    {
        var activeGroups = workspace.Fences.Where(f => f.PageId == workspace.ActivePageId).ToArray();
        var activeIds = activeGroups.Select(f => f.Id).ToHashSet();
        var protectedPaths = activeGroups.Where(f => f.ContentLocked).SelectMany(f => f.Tabs).SelectMany(t => t.Items).Select(PathExtensions.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desktop = entries.Where(e => e.Exists && !e.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) && !protectedPaths.Contains(PathExtensions.Key(e.Path)))
            .Select(entry => (Entry: entry, Reference: resolve?.Invoke(entry) ?? new ImportedReference(entry.Path, entry.Path)))
            .Where(item => !protectedPaths.Contains(PathExtensions.Key(item.Reference.Path)))
            .DistinctBy(item => PathExtensions.Key(item.Reference.Path), StringComparer.OrdinalIgnoreCase).ToList();
        var paths = desktop.SelectMany(item => new[] { PathExtensions.Key(item.Entry.Path), PathExtensions.Key(item.Reference.Path) }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in activeGroups.Where(f => !f.ContentLocked).SelectMany(f => f.Tabs).Where(t => string.IsNullOrEmpty(t.PortalPath))) FileCatalog.RemoveReferences(tab, paths);
        var count = 0; var added = 0;
        foreach (var category in Categories)
        {
            var items = desktop.Where(item => Category(item.Entry) == category.Id).ToList();
            if (items.Count == 0) continue;
            var names = L.Codes.Select(code => L.Catalog(code).GetValueOrDefault(category.NameKey, category.NameKey)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (category.Id == "other") foreach (var code in L.Codes) names.Add(L.Catalog(code).GetValueOrDefault("Рабочий стол", "Рабочий стол"));
            var fence = workspace.Fences.FirstOrDefault(f => !f.ContentLocked && f.PageId == workspace.ActivePageId && f.DesktopCategory == category.Id && f.Tabs.Any(t => string.IsNullOrEmpty(t.PortalPath)))
                ?? workspace.Fences.FirstOrDefault(f => !f.ContentLocked && f.PageId == workspace.ActivePageId && names.Contains(f.Name) && f.Tabs.Any(t => string.IsNullOrEmpty(t.PortalPath)));
            if (fence == null)
            {
                fence = new() { Name = L.Text(category.NameKey), PageId = workspace.ActivePageId, Color = category.Color, Accent = category.Accent, Sort = SortOrder.Name, DisplayMode = FenceDisplayMode.Minimal, Layer = FenceLayer.AlwaysOnTop, RolledUp = true, HoverExpand = true, Height = 420 };
                fence.Tabs[0].Name = L.Text("Файлы"); workspace.Fences.Add(fence);
            }
            fence.DesktopCategory = category.Id;
            var tab = fence.Tabs.First(t => string.IsNullOrEmpty(t.PortalPath));
            added += FileCatalog.ImportReferences(tab, items.Select(item => item.Reference)).Added;
            fence.ActiveTabId = tab.Id;
            // These rules precede the user's rules only for this explicitly managed category.
            workspace.Rules.RemoveAll(r => r.DesktopCategory == category.Id && activeIds.Contains(r.FenceId));
            var rule = new SortingRule { Name = L.Format("Рабочий стол: {0}", L.Text(category.NameKey)), DesktopCategory = category.Id, FenceId = fence.Id, TabId = tab.Id };
            if (category.Id == "folders") rule.Kind = RuleKind.Folder;
            else if (category.Id == "other") rule.Kind = RuleKind.Everything;
            else { rule.Kind = RuleKind.Extension; rule.Pattern = category.Extensions; }
            workspace.Rules.Add(rule); count++;
        }
        // Move the generated category rules together to the front; fallback must be last.
        var managedRules = workspace.Rules.Where(r => r.DesktopCategory != null).OrderBy(r => r.DesktopCategory == "other").ToList();
        workspace.Rules.RemoveAll(r => managedRules.Contains(r)); workspace.Rules.InsertRange(0, managedRules);
        return new(added, count);
    }
    public static void ArrangeTop(Workspace workspace, IReadOnlyList<DisplayArea> displays)
    {
        var remaining = new Queue<Fence>(workspace.Fences.Where(f => f.PageId == workspace.ActivePageId));
        var ordered = displays.OrderByDescending(d => d.Primary).ToArray();
        for (var i = 0; i < ordered.Length && remaining.Count > 0; i++)
        {
            var display = ordered[i]; var logicalWidth = display.Width / display.Scale;
            var capacity = Math.Max(1, (int)Math.Floor((logicalWidth - 10) / 310));
            var count = i == ordered.Length - 1 ? remaining.Count : Math.Min(remaining.Count, capacity);
            var perRow = Math.Min(count, Math.Max(1, (int)Math.Floor((logicalWidth - 10) / 210)));
            var width = Math.Min(400, Math.Max(200, (logicalWidth - (perRow + 1) * 10) / perRow));
            for (var j = 0; j < count; j++)
            {
                var fence = remaining.Dequeue(); fence.Width = width;
                fence.X = display.X + (10 + j % perRow * (width + 10)) * display.Scale;
                fence.Y = display.Y + (8 + j / perRow * (FenceLayout.CollapsedHeight(fence) + 6)) * display.Scale;
                DisplayLayout.Capture(fence, displays, display);
            }
        }
    }
    public static void ArrangeEdge(Workspace workspace, IReadOnlyList<DisplayArea> displays, DockEdge edge, double thickness, double proximity, int openDelay, int closeDelay)
    {
        if (edge == DockEdge.None || !Enum.IsDefined(edge)) throw new ArgumentException("An edge is required.", nameof(edge));
        var groups = workspace.Fences.Where(f => f.PageId == workspace.ActivePageId).ToList();
        var ordered = displays.OrderByDescending(d => d.Primary).ToArray();
        var capacities = ordered.Select(d => Math.Max(1, (int)Math.Floor(((edge is DockEdge.Left or DockEdge.Right ? d.Height : d.Width) / d.Scale - 8) / 40))).ToArray();
        if (groups.Count > capacities.Sum()) throw new InvalidOperationException(L.Text("Недостаточно места у края. Перенесите часть групп на другую страницу."));
        var remaining = new Queue<Fence>(groups);
        for (var i = 0; i < ordered.Length && remaining.Count > 0; i++)
        {
            var display = ordered[i]; var count = Math.Min(remaining.Count, capacities[i]);
            var available = (edge is DockEdge.Left or DockEdge.Right ? display.Height : display.Width) / display.Scale;
            var length = Math.Min(180, Math.Max(32, (available - (count + 1) * 8) / count));
            for (var j = 0; j < count; j++)
            {
                var fence = remaining.Dequeue(); fence.Edge = edge; fence.EdgeSpanMode = EdgeSpanMode.Custom; fence.EdgeLength = length; fence.EdgeOffset = 8 + j * (length + 8); fence.EdgeThickness = thickness;
                fence.ActivationDistance = proximity; fence.OpenDelayMs = openDelay; fence.CloseDelayMs = closeDelay; fence.RevealTrigger = RevealTrigger.Proximity;
                fence.Layer = FenceLayer.AlwaysOnTop; fence.HoverExpand = true; DisplayLayout.Capture(fence, displays, display);
            }
        }
    }
}
