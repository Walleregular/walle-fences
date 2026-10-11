namespace WalleFences.Core;

public static class Defaults
{
    public static Workspace Create(string desktop, string publicDesktop, string downloads)
    {
        var workspace = new Workspace();
        var apps = new Fence { Name = L.Text("Приложения"), X = 40, Y = 64, Width = 330, Height = 310 };
        var docs = new Fence { Name = L.Text("Документы"), X = 394, Y = 64, Width = 370, Height = 310, Accent = "#A7C7FF" };
        var other = new Fence { Name = L.Text("Рабочий стол"), X = 40, Y = 400, Width = 330, Height = 260, Accent = "#C5CED4" };
        var portal = new Fence { Name = L.Text("Загрузки"), X = 394, Y = 400, Width = 370, Height = 260, Accent = "#E7BD82", Sort = SortOrder.Newest };
        portal.Tabs[0].Name = L.Text("Загрузки"); portal.Tabs[0].PortalPath = downloads;
        workspace.Fences.AddRange([apps, docs, other, portal]);
        workspace.Rules.AddRange([
            new() { Name = L.Text("Приложения и ярлыки"), Kind = RuleKind.Extension, Pattern = ".lnk .url .exe .appref-ms", FenceId = apps.Id, TabId = apps.Tabs[0].Id },
            new() { Name = L.Text("Документы и таблицы"), Kind = RuleKind.Extension, Pattern = ".pdf .doc .docx .xlsx .xls .pptx .txt .md .csv .odt", FenceId = docs.Id, TabId = docs.Tabs[0].Id },
            new() { Name = L.Text("Остальные файлы"), Kind = RuleKind.Everything, FenceId = other.Id, TabId = other.Tabs[0].Id }
        ]);
        RuleEngine.Organize(workspace, FileCatalog.ReadFolder(desktop).Concat(FileCatalog.ReadFolder(publicDesktop)));
        return WorkspaceStore.Validate(workspace);
    }
}
