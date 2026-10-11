using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private Border GameModeSettings()
    {
        _app.UpdateGameModeState();
        var panel = new StackPanel();
        var title = Ui.Row(Brand.Icon("gamepad", Ui.Accent, 20), Ui.Text(L.Text("Игровой режим"), 17, Ui.Ink, FontWeights.SemiBold));
        title.Margin = new Thickness(0, 0, 0, 13); panel.Children.Add(title);
        var enabled = Ui.Check(panel, L.Text("Не раскрывать группы поверх полноэкранных приложений"), _app.Workspace.Preferences.GameMode);
        enabled.Margin = new Thickness(0, 0, 0, 10); AutomationProperties.SetAutomationId(enabled, "game-mode-enabled");
        var state = Ui.Text(_app.GameModeStatus, 12, Ui.Muted); state.Margin = new Thickness(0, 0, 0, 10); panel.Children.Add(state);
        AutomationProperties.SetAutomationId(state, "game-mode-state"); AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Polite);
        var hint = Ui.Text(L.Text("В игре или при просмотре видео на весь экран наведение, приближение и горячий угол не открывают группы. На других мониторах они работают как обычно. Щелчки и горячие клавиши остаются доступны."), 12, Ui.Muted);
        panel.Children.Add(hint);
        var shortcut = Ui.Text(L.Text("Быстрый доступ") + ": " + _app.Workspace.Preferences.PeekHotkey.Replace("+", " + "), 11, Ui.Ink);
        shortcut.Margin = new Thickness(0, 10, 0, 0); panel.Children.Add(shortcut);
        var card = Ui.Card(panel); AutomationProperties.SetAutomationId(card, "game-mode-settings");
        void UpdateState()
        {
            enabled.IsChecked = _app.Workspace.Preferences.GameMode;
            state.Text = _app.GameModeStatus; state.Foreground = enabled.IsChecked == true ? Ui.Accent : Ui.Muted;
        }
        enabled.Click += (_, _) =>
        {
            _app.Workspace.Preferences.GameMode = enabled.IsChecked == true;
            _app.RefreshGameModePreference(); UpdateState();
        };
        card.Loaded += (_, _) => { _app.GameModeStateChanged -= UpdateState; _app.GameModeStateChanged += UpdateState; UpdateState(); };
        card.Unloaded += (_, _) => _app.GameModeStateChanged -= UpdateState;
        return card;
    }
}
