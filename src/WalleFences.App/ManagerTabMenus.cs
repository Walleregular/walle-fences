using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private ContextMenu CreateTabActionsMenu(Fence fence, FenceTab tab)
    {
        var menu = new ContextMenu();
        AutomationProperties.SetAutomationId(menu, "manager-tab-menu-" + tab.Id);
        AutomationProperties.SetName(menu, L.Text("Действия с вкладкой"));
        void Populate()
        {
            menu.Items.Clear();
            var current = _app.Workspace.Fences.FirstOrDefault(group => group.Id == fence.Id);
            var selected = current?.Tabs.FirstOrDefault(candidate => candidate.Id == tab.Id);
            if (current == null || selected == null) return;
            var editable = !current.ContentLocked;
            var index = current.Tabs.IndexOf(selected);
            var activate = MenuAction("Открыть вкладку", "tabs", () => ActivateInspectorTab(current.Id, selected.Id), "manager-tab-open");
            activate.IsCheckable = true;
            activate.IsChecked = current.ActiveTab.Id == selected.Id;
            menu.Items.Add(activate);
            var rename = MenuAction("Переименовать вкладку", "edit", () => Editors.RenameTab(_app, current, selected), "manager-tab-rename");
            rename.IsEnabled = editable; menu.Items.Add(rename);
            var settings = MenuAction("Настройки вкладки", "settings", () => Editors.Tab(_app, current, selected), "manager-tab-settings");
            settings.IsEnabled = editable; menu.Items.Add(settings);
            if (current.Tabs.Count > 1)
            {
                var move = new MenuItem { Header = L.Text("Переместить вкладку"), Icon = Brand.Icon("tabs", Ui.Muted, 16), IsEnabled = editable };
                AutomationProperties.SetAutomationId(move, "manager-tab-move");
                AutomationProperties.SetName(move, L.Text("Переместить вкладку"));
                var up = MenuAction("Выше", "move-up", () => MoveInspectorTab(current.Id, selected.Id, -1), "manager-tab-up");
                up.IsEnabled = editable && index > 0; move.Items.Add(up);
                var down = MenuAction("Ниже", "move-down", () => MoveInspectorTab(current.Id, selected.Id, 1), "manager-tab-down");
                down.IsEnabled = editable && index < current.Tabs.Count - 1; move.Items.Add(down);
                var detach = MenuAction("Выделить в отдельную группу", "groups", () => _app.DetachTab(current, selected), "manager-tab-detach");
                detach.IsEnabled = editable; move.Items.Add(detach);
                menu.Items.Add(move);
            }
            menu.Items.Add(new Separator());
            var remove = MenuAction("Удалить вкладку…", "trash", () => Editors.DeleteTab(_app, current, selected), "manager-tab-delete");
            remove.IsEnabled = editable;
            if (current.Tabs.Count == 1) remove.ToolTip = L.Text("Последняя вкладка удаляется вместе с группой после подтверждения.");
            if (!editable)
            {
                remove.ToolTip = L.Text("Содержимое группы защищено. Отключите защиту в настройках группы.");
                ToolTipService.SetShowOnDisabled(remove, true);
            }
            menu.Items.Add(remove);
            FenceMenuTheme.Apply(menu, current, selected);
        }
        menu.Opened += (_, _) => Populate();
        Populate();
        return menu;
    }

    private void ActivateInspectorTab(string fenceId, string tabId)
    {
        var current = _app.Workspace.Fences.FirstOrDefault(group => group.Id == fenceId)
            ?? throw new InvalidOperationException(L.Text("Группа удалена"));
        var selected = current.Tabs.FirstOrDefault(tab => tab.Id == tabId)
            ?? throw new InvalidOperationException(L.Text("Вкладка удалена"));
        _selectedFenceId = current.Id;
        _inspectorContentFenceId = current.Id; _inspectorContentTabId = selected.Id;
        _app.OpenGroup(current, selected); _app.SaveNow();
        if (CurrentSection == "groups") RenderInspector();
    }

    private void MoveInspectorTab(string fenceId, string tabId, int direction)
    {
        _app.Mutate(() =>
        {
            var current = _app.Workspace.Fences.FirstOrDefault(group => group.Id == fenceId)
                ?? throw new InvalidOperationException(L.Text("Группа удалена"));
            WorkspaceActions.RequireEditable(current);
            var index = current.Tabs.FindIndex(tab => tab.Id == tabId);
            if (index < 0) throw new InvalidOperationException(L.Text("Вкладка удалена"));
            var next = index + direction;
            if (next < 0 || next >= current.Tabs.Count) return;
            (current.Tabs[index], current.Tabs[next]) = (current.Tabs[next], current.Tabs[index]);
        });
        _app.SaveNow();
        if (CurrentSection == "groups") RenderInspector();
    }

    private static void OpenTabActionsMenu(ContextMenu menu, UIElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
