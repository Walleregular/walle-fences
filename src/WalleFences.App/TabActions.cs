using FenceModel = WalleFences.Core.Fence;

namespace WalleFences.App;

internal static partial class Editors
{
    internal static void RenameTab(AppController app, FenceModel fence, FenceTab tab)
    {
        var current = app.Workspace.Fences.FirstOrDefault(group => group.Id == fence.Id)
            ?? throw new InvalidOperationException(L.Text("Группа удалена"));
        WorkspaceActions.RequireEditable(current);
        var selected = current.Tabs.FirstOrDefault(candidate => candidate.Id == tab.Id)
            ?? throw new InvalidOperationException(L.Text("Вкладка удалена"));
        var name = Ui.Ask(L.Text("Переименовать вкладку"), L.Text("Название вкладки"), selected.Name);
        if (name == null) return;
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException(L.Text("Введите название вкладки."));
        if (name.Trim() == selected.Name) return;
        app.Mutate(() =>
        {
            var latest = app.Workspace.Fences.FirstOrDefault(group => group.Id == fence.Id)
                ?? throw new InvalidOperationException(L.Text("Группа удалена"));
            WorkspaceActions.RequireEditable(latest);
            var target = latest.Tabs.FirstOrDefault(candidate => candidate.Id == tab.Id)
                ?? throw new InvalidOperationException(L.Text("Вкладка удалена"));
            target.Name = name.Trim();
        });
        app.SaveNow();
    }

    internal static void DeleteTab(AppController app, FenceModel fence, FenceTab tab)
    {
        var current = app.Workspace.Fences.FirstOrDefault(group => group.Id == fence.Id)
            ?? throw new InvalidOperationException(L.Text("Группа удалена"));
        WorkspaceActions.RequireEditable(current);
        var selected = current.Tabs.FirstOrDefault(candidate => candidate.Id == tab.Id)
            ?? throw new InvalidOperationException(L.Text("Вкладка удалена"));
        var confirmedLast = current.Tabs.Count == 1;
        var question = confirmedLast
            ? L.Format("Удалить последнюю вкладку «{0}» вместе с группой «{1}»? Ссылки, виджеты и связанные правила будут удалены; исходные файлы останутся на диске.", selected.Name, current.Name)
            : L.Format("Удалить вкладку «{0}»? Ссылки и виджеты будут удалены из группы; исходные файлы останутся на месте.", selected.Name);
        if (!Ui.Confirm(question)) return;
        app.Mutate(() =>
        {
            var latest = app.Workspace.Fences.FirstOrDefault(group => group.Id == fence.Id)
                ?? throw new InvalidOperationException(L.Text("Группа удалена"));
            WorkspaceActions.RequireEditable(latest);
            var index = latest.Tabs.FindIndex(candidate => candidate.Id == tab.Id);
            if (index < 0) throw new InvalidOperationException(L.Text("Вкладка удалена"));
            if (latest.Tabs.Count == 1)
            {
                if (!confirmedLast) throw new InvalidOperationException(L.Text("Состав вкладок изменился. Повторите удаление."));
                app.Workspace.Fences.Remove(latest);
                app.Workspace.Rules.RemoveAll(rule => rule.FenceId == latest.Id);
                return;
            }
            latest.Tabs.RemoveAt(index);
            app.Workspace.Rules.RemoveAll(rule => rule.FenceId == latest.Id && rule.TabId == tab.Id);
            if (latest.ActiveTabId == tab.Id || latest.Tabs.All(candidate => candidate.Id != latest.ActiveTabId))
                latest.ActiveTabId = latest.Tabs[Math.Min(index, latest.Tabs.Count - 1)].Id;
        });
        app.SaveNow();
    }
}
