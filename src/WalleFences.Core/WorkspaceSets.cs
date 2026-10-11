namespace WalleFences.Core;

public enum SetTemplate { Empty, CopyCurrent, TabbedFiles, Widgets }

// Sets reuse persisted pages, so existing profiles and monitor placements need no migration.
public static class WorkspaceSets
{
    public static DesktopPage Create(Workspace workspace, string name, SetTemplate template)
    {
        if (!Enum.IsDefined(template)) throw new ArgumentOutOfRangeException(nameof(template));
        var page = new DesktopPage { Name = name };
        if (template == SetTemplate.CopyCurrent)
        {
            var copy = WorkspaceStore.Clone(workspace);
            var groups = copy.Fences.Where(f => f.PageId == copy.ActivePageId).ToArray();
            foreach (var group in groups)
            {
                var oldId = group.Id; group.Id = Guid.NewGuid().ToString("N"); group.PageId = page.Id; group.Hotkey = "";
                foreach (var tab in group.Tabs)
                {
                    var oldTabId = tab.Id; tab.Id = Guid.NewGuid().ToString("N");
                    if (group.ActiveTabId == oldTabId) group.ActiveTabId = tab.Id;
                    foreach (var widget in tab.Widgets) { widget.Id = Guid.NewGuid().ToString("N"); if (widget.Kind == WidgetKind.Focus) FocusClock.Reset(widget); }
                    foreach (var rule in copy.Rules.Where(r => r.FenceId == oldId && r.TabId == oldTabId))
                    { rule.Id = Guid.NewGuid().ToString("N"); rule.FenceId = group.Id; rule.TabId = tab.Id; workspace.Rules.Add(rule); }
                }
                workspace.Fences.Add(group);
            }
        }
        else if (template == SetTemplate.Widgets)
        {
            WidgetKind[] kinds = [WidgetKind.Clock, WidgetKind.CpuTemperature, WidgetKind.System, WidgetKind.Drives, WidgetKind.Focus, WidgetKind.Note];
            for (var index = 0; index < kinds.Length; index++)
            {
                var kind = kinds[index];
                var title = L.Text(kind switch
                {
                    WidgetKind.Clock => "Часы и дата", WidgetKind.CpuTemperature => "Температура CPU",
                    WidgetKind.System => "CPU и память", WidgetKind.Drives => "Свободное место",
                    WidgetKind.Focus => "Фокус-таймер", _ => "Заметка"
                });
                var (width, height) = kind switch
                {
                    WidgetKind.Clock => (300, 220), WidgetKind.Focus => (300, 240),
                    WidgetKind.Drives => (320, 400), WidgetKind.Note => (320, 300), _ => (320, 280)
                };
                var tab = new FenceTab { Name = title, Widgets = [new() { Kind = kind }] };
                workspace.Fences.Add(new Fence
                {
                    Name = title, PageId = page.Id, Layer = FenceLayer.AlwaysOnTop, Edge = DockEdge.Right,
                    EdgeThickness = 3, EdgeLength = 64, EdgeOffset = 16 + index * 72,
                    HoverExpand = true, Opacity = .97, DisplayMode = FenceDisplayMode.Compact,
                    Accent = workspace.Preferences.InterfaceAccent, CloseDelayMs = 700,
                    Width = width, Height = height, IsSidebar = true, Tabs = [tab], ActiveTabId = tab.Id
                });
            }
        }
        else if (template == SetTemplate.TabbedFiles)
        {
            var group = new Fence { Name = name, PageId = page.Id, Layer = FenceLayer.AlwaysOnTop, Edge = DockEdge.Top,
                EdgeThickness = 3, EdgeLength = 180, HoverExpand = true, Opacity = .97, DisplayMode = FenceDisplayMode.Compact,
                Accent = workspace.Preferences.InterfaceAccent, CloseDelayMs = 700, Width = 420, Height = 650 };
            group.Tabs = [new() { Name = L.Text("Приложения") }, new() { Name = L.Text("Документы") }, new() { Name = L.Text("Папки") }];
            group.ActiveTabId = group.Tabs[0].Id; workspace.Fences.Add(group);
        }
        workspace.Pages.Add(page); workspace.ActivePageId = page.Id;
        return page;
    }
}
