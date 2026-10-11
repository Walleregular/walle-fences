namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private string? _inspectorContentFenceId, _inspectorContentTabId;
    private Border? _inspectorContentHost;
    private readonly Dictionary<string, Border> _inspectorTabRows = [];

    private FenceTab InspectorContentTab(Fence fence)
    {
        var tab = _inspectorContentFenceId == fence.Id
            ? fence.Tabs.FirstOrDefault(candidate => candidate.Id == _inspectorContentTabId)
            : null;
        tab ??= fence.ActiveTab;
        _inspectorContentFenceId = fence.Id; _inspectorContentTabId = tab.Id;
        return tab;
    }

    private void SelectInspectorContentTab(string fenceId, string tabId)
    {
        var current = SelectedFence;
        if (current == null || current.Id != fenceId || current.Tabs.All(tab => tab.Id != tabId)) return;
        if (_inspectorContentFenceId != fenceId || _inspectorContentTabId != tabId)
        {
            _inspectorContentFenceId = fenceId; _inspectorContentTabId = tabId;
            RefreshInspectorTabContent();
        }
        var host = _inspectorContentHost;
        if (host == null) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(host, _inspectorContentHost) && CurrentSection == "groups" && IsVisible)
                host.BringIntoView();
        }), DispatcherPriority.ContextIdle);
    }

    private void RefreshInspectorTabContent()
    {
        if (_inspectorContentHost == null || SelectedFence is not { } fence) return;
        var tab = InspectorContentTab(fence);
        foreach (var (id, row) in _inspectorTabRows)
        {
            row.Background = id == tab.Id ? Ui.AccentWash : Brushes.Transparent;
            row.BorderBrush = id == tab.Id ? Ui.FocusAccent : Ui.Line;
        }
        _inspectorContentHost.Child = InspectorTabContent(fence, tab);
    }
}
