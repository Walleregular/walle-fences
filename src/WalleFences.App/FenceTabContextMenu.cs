using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace WalleFences.App;

internal sealed partial class FenceWindow
{
    private ContextMenu? _openTabContextMenu;
    private bool HasOpenTabContextMenu => _openTabContextMenu?.IsOpen == true;

    private void BindTabContextMenu(FrameworkElement target, FenceTab tab)
    {
        target.ContextMenu = TabMenu(tab);
        target.PreviewMouseRightButtonUp += (_, args) =>
        {
            args.Handled = true;
            OpenTabContextMenu(target, tab, false);
        };
        target.ContextMenuOpening += (_, args) =>
        {
            args.Handled = true;
            OpenTabContextMenu(target, tab, args.CursorLeft < 0 || args.CursorTop < 0);
        };
    }

    private void OpenTabContextMenu(FrameworkElement target, FenceTab tab, bool keyboard)
    {
        if (_closed || !Fence.Tabs.Contains(tab) || !_app.Workspace.Fences.Contains(Fence)) return;
        CloseTabContextMenu();
        var menu = TabMenu(tab);
        target.ContextMenu = menu;
        menu.PlacementTarget = target;
        menu.Placement = keyboard ? PlacementMode.Bottom : PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void CloseTabContextMenu()
    {
        if (_openTabContextMenu is { } menu) menu.IsOpen = false;
        _openTabContextMenu = null;
    }

    private ContextMenu TabMenu(FenceTab tab)
    {
        var menu = new ContextMenu();
        AutomationProperties.SetAutomationId(menu, "tab-context-menu-" + tab.Id);
        AutomationProperties.SetName(menu, tab.Name);
        AddTabActions(menu.Items, tab);
        FenceMenuTheme.Apply(menu, Fence, tab);
        menu.Opened += (_, _) =>
        {
            _openTabContextMenu = menu;
            var current = _app.Workspace.Fences.FirstOrDefault(group => group.Id == Fence.Id);
            var selected = current?.Tabs.FirstOrDefault(candidate => candidate.Id == tab.Id);
            if (current == null || selected == null) { menu.IsOpen = false; return; }
            menu.Items.Clear();
            AddTabActions(menu.Items, selected);
            FenceMenuTheme.Apply(menu, current, selected);
            if (_tabViews.TryGetValue(selected.Id, out var view)) UpdateTabFold(view);
        };
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_openTabContextMenu, menu)) _openTabContextMenu = null;
            if (_tabViews.TryGetValue(tab.Id, out var view)) UpdateTabFold(view);
        };
        return menu;
    }

    private void AddTabActions(ItemCollection items, FenceTab tab)
    {
        items.Add(TabMotionMenu(L.Text("Открыть вкладку"), smooth => ActivateTab(tab, smooth), "tabs"));
        var collapsed = !KeepsExpanded && (tab == Fence.ActiveTab ? VisuallyCollapsed : Fence.RolledUp || tab.Collapsed);
        var fold = TabMotionMenu(L.Text(collapsed ? "Развернуть вкладку" : "Свернуть вкладку"), smooth => ToggleTab(tab, smooth), collapsed ? "expand" : "collapse");
        fold.IsEnabled = !KeepsExpanded;
        if (KeepsExpanded) { fold.ToolTip = AlwaysOpenHint; ToolTipService.SetShowOnDisabled(fold, true); }
        items.Add(fold);
        var rename = ContentMenu(L.Text("Переименовать вкладку…"), () => Editors.RenameTab(_app, Fence, tab), "edit");
        AutomationProperties.SetAutomationId(rename, "tab-context-rename-" + tab.Id); items.Add(rename);
        items.Add(ContentMenu(L.Text("Настроить вкладку…"), () => Editors.Tab(_app, Fence, tab), "settings"));
        if (Fence.Tabs.Count > 1)
        {
            var move = MenuHeading(L.Text("Переместить вкладку"), "tabs");
            move.IsEnabled = !Fence.ContentLocked;
            move.Items.Add(TabMoveMenu(L.Text("Влево"), tab, -1, "chevron-left"));
            move.Items.Add(TabMoveMenu(L.Text("Вправо"), tab, 1, "chevron-right"));
            move.Items.Add(ContentMenu(L.Text("Выделить в отдельную группу"), () => _app.DetachTab(Fence, tab), "groups"));
            items.Add(move);
        }
        items.Add(new Separator());
        var delete = ContentMenu(L.Text("Удалить вкладку…"), () => Editors.DeleteTab(_app, Fence, tab), "delete");
        if (Fence.Tabs.Count == 1) delete.ToolTip = L.Text("Последняя вкладка удаляется вместе с группой после подтверждения.");
        AutomationProperties.SetAutomationId(delete, "tab-context-delete-" + tab.Id); items.Add(delete);
    }
}
