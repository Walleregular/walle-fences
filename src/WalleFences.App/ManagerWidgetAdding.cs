namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    internal void SelectAddedWidget(Fence panel)
    {
        _widgetFenceId = panel.Id; _widgetTabId = panel.ActiveTabId;
        _selectedMonitorId = DisplayLayout.Select(panel, AppController.Displays()).Id;
        if (CurrentSection == "groups")
        {
            _selectedFenceId = panel.Id;
            _inspectorContentFenceId = panel.Id; _inspectorContentTabId = panel.ActiveTabId;
        }
        if (CurrentSection is "groups" or "widgets") Refresh();
    }
}
