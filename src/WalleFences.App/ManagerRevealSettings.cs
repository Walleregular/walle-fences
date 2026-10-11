using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private void BuildRevealSettings(StackPanel body, Fence fence, Slider thickness)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 5) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(Ellipsis(L.Text("Раскрытие"), 11, Ui.Muted));
        var reveal = new ComboBox { ItemsSource = Editors.TriggerShortLabels, SelectedIndex = (int)fence.RevealTrigger, Height = 28, Margin = new Thickness(0), FontSize = 11 };
        reveal.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(ComboBox.SelectedItem)) { Source = reveal });
        AutomationProperties.SetAutomationId(reveal, "inspector-reveal");
        AutomationProperties.SetName(reveal, L.Text("Раскрытие"));
        Grid.SetColumn(reveal, 1); row.Children.Add(reveal); body.Children.Add(row);

        var hint = Ui.Text(Editors.TriggerHint(fence.RevealTrigger), 10.5, Ui.Muted);
        hint.Margin = new Thickness(0, 0, 0, 6);
        AutomationProperties.SetAutomationId(hint, "inspector-reveal-hint"); body.Children.Add(hint);

        var timing = new StackPanel { Margin = new Thickness(0, 0, 0, 4) }; body.Children.Add(timing);
        var distance = InspectorSlider(timing, "Приближение", 0, 80, fence.ActivationDistance, value => InspectorMutate(f => f.ActivationDistance = value), "inspector-reveal-distance", " px");
        var opening = InspectorSlider(timing, "Открытие", 0, 2000, fence.OpenDelayMs, value => InspectorMutate(f => f.OpenDelayMs = (int)value), "inspector-reveal-open-delay", " " + L.Text("мс"));
        var closing = InspectorSlider(timing, "Закрытие", 100, 5000, fence.CloseDelayMs, value => InspectorMutate(f => f.CloseDelayMs = (int)value), "inspector-reveal-close-delay", " " + L.Text("мс"));
        foreach (var slider in new[] { opening, closing })
        {
            slider.TickFrequency = 50;
            ((Grid)slider.Parent).ColumnDefinitions[2].Width = new GridLength(62);
        }

        var keys = new StackPanel { Margin = new Thickness(0, 0, 0, 6) }; body.Children.Add(keys);
        var hotkey = Ui.HotkeyInput(keys, L.Text("Сочетание для этой группы"), fence.Hotkey, _app);
        hotkey.Height = 30; hotkey.Padding = new Thickness(8, 3, 8, 3); hotkey.FontSize = 11; hotkey.Margin = new Thickness(0, 3, 0, 4);
        AutomationProperties.SetAutomationId(hotkey, "inspector-reveal-hotkey");
        var keyHint = Ui.Text("", 10.5, Ui.Muted); keys.Children.Add(keyHint);
        var lastKey = fence.Hotkey;
        void UpdateKeyHint()
        {
            var conflict = fence.Name + " · " + hotkey.Text;
            keyHint.Text = hotkey.Text.Length == 0
                ? L.Text("Нажмите поле и задайте сочетание Ctrl, Alt или Win с клавишей.")
                : _app.HotkeyConflicts.Contains(conflict)
                    ? L.Text("Сочетание занято. Выберите другое.")
                    : L.Text("Повторное нажатие сворачивает группу.");
        }
        hotkey.TextChanged += (_, _) => UpdateKeyHint();
        hotkey.LostKeyboardFocus += (_, _) =>
        {
            if (hotkey.Text == lastKey || SelectedFence?.Id != fence.Id) return;
            Ui.Guard(() =>
            {
                InspectorMutate(f => f.Hotkey = hotkey.Text);
                lastKey = hotkey.Text;
                UpdateKeyHint();
            });
        };

        void UpdateMode()
        {
            var trigger = fence.RevealTrigger;
            var advanced = _app.Workspace.Preferences.AdvancedSettings;
            hint.Text = Editors.TriggerHint(trigger);
            thickness.IsEnabled = fence.Edge != DockEdge.None && trigger != RevealTrigger.AlwaysOpen;
            ((FrameworkElement)thickness.Parent).Visibility = thickness.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            ((FrameworkElement)distance.Parent).Visibility = advanced && fence.Edge != DockEdge.None && (trigger is RevealTrigger.Proximity or RevealTrigger.HoverPause) ? Visibility.Visible : Visibility.Collapsed;
            ((FrameworkElement)opening.Parent).Visibility = trigger == RevealTrigger.HoverPause || advanced && trigger == RevealTrigger.Proximity ? Visibility.Visible : Visibility.Collapsed;
            ((FrameworkElement)closing.Parent).Visibility = advanced && (Editors.AutomaticReveal(trigger) || trigger is RevealTrigger.Click or RevealTrigger.DoubleClick) ? Visibility.Visible : Visibility.Collapsed;
            timing.Visibility = timing.Children.Cast<FrameworkElement>().Any(child => child.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
            keys.Visibility = trigger == RevealTrigger.Hotkey ? Visibility.Visible : Visibility.Collapsed;
            UpdateKeyHint();
        }
        reveal.SelectionChanged += (_, _) =>
        {
            if (reveal.SelectedIndex < 0) return;
            Ui.Guard(() => InspectorMutate(f => Editors.ApplyRevealTrigger(f, (RevealTrigger)reveal.SelectedIndex)));
            UpdateMode();
        };
        UpdateMode();
    }
}
