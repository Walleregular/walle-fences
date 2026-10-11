using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private static string PrimarySection(string section) => section switch
    {
        "groups" or "pages" or "widgets" or "monitors" or "snapshots" or "sidebars" => "groups",
        "shortcuts" => "settings",
        _ => section
    };

    private UIElement DesktopWorkspaceNavigation()
    {
        var bar = new Grid { Margin = new Thickness(0, 0, 0, 12), Height = 32 };
        bar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var views = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (section, label) in new[] { ("groups", "Группы"), ("widgets", "Виджеты"), ("pages", "Наборы") })
        {
            var button = RouteButton(Ui.Segment(L.Text(label), () => { }, section == CurrentSection, "workspace-view-" + section), section);
            button.Height = 32; button.MinWidth = 0; button.Padding = new Thickness(9, 0, 9, 0);
            button.Margin = new Thickness(0, 0, 2, 0); button.FontSize = 11;
            button.ToolTip = L.Text(label); AutomationProperties.SetName(button, L.Text(label));
            views.Children.Add(button);
        }
        bar.Children.Add(views);
        var tools = Ui.Button("", () => { }, id: "desktop-tools");
        tools.Content = IconText("settings", L.Text("Управление"), Ui.Muted, 15);
        tools.Height = 32; tools.MinWidth = 0; tools.Margin = new Thickness(6, 0, 0, 0); tools.Padding = new Thickness(8, 0, 8, 0);
        tools.ToolTip = L.Text("Мониторы, снимки и боковые панели"); AutomationProperties.SetName(tools, L.Text("Управление рабочим столом"));
        var menu = new ContextMenu { PlacementTarget = tools, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (section, label, icon) in new[] { ("monitors", "Мониторы", "monitors"), ("snapshots", "Снимки раскладки", "snapshots"), ("sidebars", "Боковые панели", "sidebars") })
        {
            var entry = RouteMenuItem(MenuAction(label, icon, () => { }), section);
            AutomationProperties.SetAutomationId(entry, "desktop-tool-" + section); menu.Items.Add(entry);
        }
        menu.Items.Add(new Separator());
        var learning = MenuAction("Показать обучение", "help", StartOnboarding);
        AutomationProperties.SetAutomationId(learning, "desktop-tool-onboarding"); menu.Items.Add(learning);
        tools.ContextMenu = menu;
        tools.Click += (_, _) => menu.IsOpen = true;
        Grid.SetColumn(tools, 1); bar.Children.Add(tools);
        AutomationProperties.SetAutomationId(bar, "workspace-navigation");
        return bar;
    }
}
