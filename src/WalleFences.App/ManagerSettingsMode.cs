using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private Action? _refreshSettingsMode;

    private UIElement SettingsModePicker(Action applyVisibility)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(L.Text("Режим настроек"), 17, Ui.Ink, FontWeights.SemiBold));
        var choices = new Grid { Margin = new Thickness(0, 10, 0, 8) };
        choices.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        choices.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Button? basic = null, advanced = null;
        void Change(bool value)
        {
            if (_app.Workspace.Preferences.AdvancedSettings == value) return;
            _app.Mutate(() => _app.Workspace.Preferences.AdvancedSettings = value, false);
            _app.SaveNow(); _refreshSettingsMode?.Invoke();
        }
        basic = Ui.Segment(L.Text("Простые"), () => Change(false), id: "settings-mode-basic");
        advanced = Ui.Segment(L.Text("Продвинутые"), () => Change(true), id: "settings-mode-advanced");
        basic.HorizontalContentAlignment = advanced.HorizontalContentAlignment = HorizontalAlignment.Center;
        basic.Height = advanced.Height = 34; basic.Margin = new Thickness(0, 0, 4, 0); advanced.Margin = new Thickness(4, 0, 0, 0);
        AutomationProperties.SetName(basic, L.Text("Простые")); AutomationProperties.SetName(advanced, L.Text("Продвинутые"));
        choices.Children.Add(basic); Grid.SetColumn(advanced, 1); choices.Children.Add(advanced); body.Children.Add(choices);
        var hint = Ui.Text("", 12, Ui.Muted); body.Children.Add(hint);
        _refreshSettingsMode = () =>
        {
            var enabled = _app.Workspace.Preferences.AdvancedSettings;
            basic.Tag = enabled ? null : "selected"; advanced.Tag = enabled ? "selected" : null;
            hint.Text = L.Text(enabled ? "Все параметры доступны. Режим также применяется к настройкам групп." : "Только основные параметры. Дополнительные настройки сохраняются и доступны в продвинутом режиме.");
            applyVisibility();
        };
        _refreshSettingsMode();
        return Ui.Card(body);
    }

    private static void AdvancedField(List<FrameworkElement> fields, Panel parent, FrameworkElement control)
    {
        var index = parent.Children.IndexOf(control);
        if (index > 0 && parent.Children[index - 1] is TextBlock label) fields.Add(label);
        fields.Add(control);
    }

    private void SetAdvancedVisibility(IEnumerable<FrameworkElement> fields)
    {
        var visibility = _app.Workspace.Preferences.AdvancedSettings ? Visibility.Visible : Visibility.Collapsed;
        foreach (var field in fields) field.Visibility = visibility;
    }
}
