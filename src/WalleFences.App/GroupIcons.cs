using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace WalleFences.App;

internal static class GroupIcons
{
    internal static string Symbol(Fence fence, GroupIcon? choice = null)
    {
        var icon = choice ?? fence.Icon;
        return icon == GroupIcon.Automatic
            ? fence.IsSidebar || fence.Tabs.All(tab => tab.Widgets.Count > 0 && tab.Items.Count == 0 && string.IsNullOrEmpty(tab.PortalPath)) ? "widgets" : "folder"
            : icon == GroupIcon.Processor ? "processor" : icon.ToString().ToLowerInvariant();
    }

    internal static string Name(GroupIcon icon) => L.Text(icon switch
    {
        GroupIcon.Automatic => "Автоматически",
        GroupIcon.Folder => "Папка",
        GroupIcon.Briefcase => "Работа",
        GroupIcon.Desktop => "Программы",
        GroupIcon.Code => "Код",
        GroupIcon.Terminal => "Терминал",
        GroupIcon.Gamepad => "Игры",
        GroupIcon.Star => "Избранное",
        GroupIcon.Heart => "Сердце",
        GroupIcon.Music => "Музыка",
        GroupIcon.Image => "Изображения",
        GroupIcon.Video => "Видео",
        GroupIcon.Globe => "Интернет",
        GroupIcon.Book => "Книги",
        GroupIcon.Mail => "Почта",
        GroupIcon.Calendar => "Календарь",
        GroupIcon.Drive => "Диск",
        GroupIcon.Cloud => "Облако",
        GroupIcon.Download => "Загрузки",
        GroupIcon.Widgets => "Виджеты",
        GroupIcon.Processor => "Процессор",
        GroupIcon.Network => "Сеть",
        GroupIcon.Note => "Заметки",
        _ => "Слои"
    });

    internal static Image Create(Fence fence, Brush? brush = null, double size = 18) => Brand.Icon(Symbol(fence), brush, size);

    internal static GroupIcon? Pick(Fence fence, GroupIcon selected, Window? owner)
    {
        var dialog = new FormWindow(L.Text("Выбрать значок группы"), 500) { Tag = fence.Id };
        if (owner is { IsVisible: true }) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        AutomationProperties.SetAutomationId(dialog, "group-icon-picker");
        var hint = Ui.Text(L.Text("Значок относится только к этой группе и использует её цвет подсветки."), 12, Ui.Muted);
        hint.Margin = new Thickness(0, 0, 0, 14); dialog.Body.Children.Add(hint);
        var ink = new SolidColorBrush(GroupColorAppearance.Text(Ui.Brush(fence.Color).Color));
        var preview = new Grid(); preview.ColumnDefinitions.Add(new() { Width = new GridLength(36) }); preview.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var glyph = Brand.Icon(Symbol(fence, selected), Ui.Brush(fence.Accent), 22); preview.Children.Add(glyph);
        var name = Ui.Text(fence.Name, 14, ink, FontWeights.SemiBold); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(name, 1); preview.Children.Add(name);
        dialog.Body.Children.Add(new Border { Child = preview, Background = Ui.Brush(fence.Color), BorderBrush = Ui.Brush(fence.Accent, .35), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12) });
        var choices = new UniformGrid { Columns = 5, Margin = new Thickness(-3) };
        KeyboardNavigation.SetDirectionalNavigation(choices, KeyboardNavigationMode.Cycle);
        var buttons = new List<(GroupIcon Icon, Button Button)>();
        void RefreshSelection()
        {
            glyph.Source = Brand.Icon(Symbol(fence, selected), Ui.Brush(fence.Accent), 22).Source;
            foreach (var (icon, button) in buttons)
            {
                var active = icon == selected; button.Tag = active ? "selected" : null;
                button.BorderBrush = active ? Ui.FocusAccent : Ui.Line;
                button.Background = active ? Ui.AccentWash : Ui.Surface;
                AutomationProperties.SetHelpText(button, active ? L.Text("Выбрано") : "");
            }
        }
        foreach (var icon in Enum.GetValues<GroupIcon>())
        {
            var button = Ui.Segment("", () => { selected = icon; RefreshSelection(); }, id: "group-icon-" + icon);
            button.Margin = new Thickness(3); button.Padding = new Thickness(3, 7, 3, 5); button.Height = 64;
            var content = new StackPanel(); content.Children.Add(Brand.Icon(Symbol(fence, icon), Ui.Ink, 22));
            var label = Ui.Text(Name(icon), 10, Ui.Ink); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 5, 0, 0); content.Children.Add(label);
            button.Content = content; button.ToolTip = Name(icon); AutomationProperties.SetName(button, Name(icon));
            buttons.Add((icon, button)); choices.Children.Add(button);
        }
        dialog.Body.Children.Add(choices); RefreshSelection(); dialog.AddSave(() => { });
        dialog.Loaded += (_, _) => buttons.First(pair => pair.Icon == selected).Button.Focus();
        return dialog.ShowDialog() == true ? selected : null;
    }
}

internal sealed class GroupIconControl : StackPanel
{
    private readonly Fence _fence;
    private readonly Button _button;
    internal GroupIcon SelectedIcon { get; private set; }
    internal event Action? SelectionChanged;
    internal GroupIconControl(Fence fence)
    {
        _fence = fence; SelectedIcon = fence.Icon;
        Children.Add(Ui.Text(L.Text("Значок группы"), 12, Ui.Muted));
        _button = Ui.Button("", Choose, id: "editor-group-icon"); _button.HorizontalContentAlignment = HorizontalAlignment.Left;
        _button.Margin = new Thickness(0, 5, 0, 12); _button.ToolTip = L.Text("Выбрать значок группы");
        AutomationProperties.SetName(_button, (string)_button.ToolTip); Children.Add(_button); Refresh();
    }
    private void Choose()
    {
        if (GroupIcons.Pick(_fence, SelectedIcon, Window.GetWindow(this)) is not { } selected) return;
        SelectedIcon = selected; Refresh(); SelectionChanged?.Invoke();
    }
    private void Refresh() => _button.Content = Ui.Row(Brand.Icon(GroupIcons.Symbol(_fence, SelectedIcon), Ui.Brush(_fence.Accent), 20), Ui.Text(GroupIcons.Name(SelectedIcon), 12), Brand.Icon("chevron-down", Ui.Muted, 14));
}
