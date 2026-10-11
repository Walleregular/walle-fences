using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private UIElement InspectorMonitorSelector(Fence fence)
    {
        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        var row = new Grid { Height = 32 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(Ellipsis(L.Text("Монитор"), 11, Ui.Muted));
        var selector = new ComboBox { FontSize = 11, Height = 30, MinHeight = 0, Margin = new Thickness(0), MaxDropDownHeight = 300 };
        AutomationProperties.SetAutomationId(selector, "inspector-monitor");
        AutomationProperties.SetName(selector, L.Text("Перенести на монитор"));
        Grid.SetColumn(selector, 1); row.Children.Add(selector); body.Children.Add(row);
        var disconnected = Ui.Text(L.Text("Экран отключён — группа временно на основном"), 10, Ui.Muted);
        disconnected.Margin = new Thickness(0, 5, 0, 0); disconnected.Visibility = Visibility.Collapsed;
        body.Children.Add(disconnected);
        var updating = false;

        void Populate()
        {
            var current = _app.Workspace.Fences.FirstOrDefault(group => group.Id == fence.Id);
            if (current == null) return;
            var displays = AppController.Displays();
            var visible = DisplayLayout.Select(current, displays);
            updating = true;
            try
            {
                selector.Items.Clear();
                foreach (var (display, index) in displays.Select((display, index) => (display, index)))
                {
                    var label = L.Format("Монитор {0}", index + 1) + (display.Primary ? L.Text(" · основной") : "");
                    var description = display.Name + "\n" + L.Format("Рабочая область: {0:0} × {1:0} · масштаб {2:0}%",
                        display.Width / AppController.GlobalScale, display.Height / AppController.GlobalScale, display.Scale / AppController.GlobalScale * 100);
                    var option = new ComboBoxItem { Content = label, Tag = display.Id, ToolTip = description };
                    AutomationProperties.SetName(option, label + " · " + display.Name);
                    selector.Items.Add(option);
                    if (display.Id == visible.Id) { selector.SelectedItem = option; selector.ToolTip = description; }
                }
                disconnected.Visibility = current.Placement is { } placement && !displays.Any(display => display.Id.Equals(placement.MonitorId, StringComparison.OrdinalIgnoreCase))
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { updating = false; }
        }

        Populate();
        selector.DropDownOpened += (_, _) => Populate();
        selector.SelectionChanged += (_, _) =>
        {
            if (updating || SelectedFence?.Id != fence.Id || selector.SelectedItem is not ComboBoxItem { Tag: string displayId }) return;
            Ui.Guard(() =>
            {
                var displays = AppController.Displays();
                var target = displays.FirstOrDefault(display => display.Id == displayId);
                if (target == null) { Populate(); return; }
                InspectorMutate(group =>
                {
                    DisplayLayout.MoveToDisplay(group, target, displays);
                    _selectedMonitorId = target.Id;
                }, refreshInspector: true);
            });
        };
        return body;
    }
}
