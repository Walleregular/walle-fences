using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private string? _widgetFenceId, _widgetTabId;
    private Fence? StyleSettings()
    {
        var body = new StackPanel(); var prefs = _app.Workspace.Preferences;
        body.Children.Add(Ui.Text(L.Text("Стиль приложения"), 18, Ui.Ink, FontWeights.SemiBold));
        var choices = new WrapPanel { Margin = new Thickness(0, 14, 0, 8) };
        string[] labels = [L.Text("Графит"), L.Text("Светлый"), L.Text("Полночь")];
        string[] themeColors = ["#1D2021", "#EEF1F3", "#192438"];
        foreach (var (label, i) in labels.Select((label, i) => (label, i)))
        {
            var button = Ui.Button("", () => { _app.Mutate(() => _app.Workspace.Preferences.Theme = (InterfaceTheme)i, false); Ui.ApplyTheme(_app.Workspace.Preferences); Refresh(); }, id: "theme-" + i);
            var row = Ui.Row(new Border { Width = 22, Height = 22, Background = Ui.Brush(themeColors[i]), CornerRadius = new CornerRadius(5), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 9, 0) }, Ui.Text(label));
            button.Content = row; button.BorderBrush = (int)prefs.Theme == i ? Ui.Accent : Ui.Line;
            AutomationProperties.SetName(button, label); choices.Children.Add(button);
        }
        body.Children.Add(choices);
        var accentRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        string[] standardAccents = ["#A0E9C0", "#A7C7FF", "#E7BD82", "#C9B3F5", "#EFADB4"];
        foreach (var color in standardAccents)
        {
            var button = Ui.Button("", () => { _app.Mutate(() => _app.Workspace.Preferences.InterfaceAccent = color, false); Ui.ApplyTheme(_app.Workspace.Preferences); Refresh(); });
            button.Content = new Border { Width = 28, Height = 16, Background = Ui.Brush(color), CornerRadius = new CornerRadius(4) };
            button.ToolTip = color; button.BorderBrush = prefs.InterfaceAccent == color ? Ui.Ink : Ui.Line;
            AutomationProperties.SetName(button, L.Text("Цвет акцента") + " " + color); accentRow.Children.Add(button);
        }
        var custom = Ui.Button("", () =>
        {
            var selected = ColorPickerWindow.Pick(this, L.Text("Свой цвет акцента"), _app.Workspace.Preferences.InterfaceAccent);
            if (selected == null) return;
            _app.Mutate(() => _app.Workspace.Preferences.InterfaceAccent = selected, false);
            _app.SaveNow(); Ui.ApplyTheme(_app.Workspace.Preferences); Refresh();
        }, id: "interface-custom-accent");
        custom.Content = Ui.Row(new Border { Width = 20, Height = 16, Background = Ui.Brush(prefs.InterfaceAccent), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 7, 0) }, Brand.Icon("eyedropper", Ui.Ink, 16));
        custom.ToolTip = L.Text("Свой цвет акцента") + " · " + prefs.InterfaceAccent;
        custom.BorderBrush = standardAccents.Contains(prefs.InterfaceAccent, StringComparer.OrdinalIgnoreCase) ? Ui.Line : Ui.Ink;
        AutomationProperties.SetName(custom, L.Text("Свой цвет акцента")); accentRow.Children.Add(custom);
        body.Children.Add(accentRow);
        body.Children.Add(Ui.Text(L.Text("Тема применяется к настройкам, редакторам и меню. Цвет и форма каждой группы настраиваются отдельно."), 12, Ui.Muted));
        _page.Children.Add(Ui.Card(body));
        return IndividualGroupProfile();
    }
    private void Sidebars()
    {
        Intro(L.Text("Панели у края экрана с файлами, вкладками и виджетами."));
        var body = new StackPanel(); var displays = AppController.Displays();
        var name = Ui.Input(body, L.Text("Название панели"), L.Text("Моя панель"));
        var edge = Ui.Combo(body, L.Text("Край экрана"), [L.Text("Слева"), L.Text("Справа")], 1);
        var display = Ui.Combo(body, L.Text("Монитор"), displays.Select((d, i) => L.Format("Монитор {0} · {1}", i + 1, d.Name)));
        body.Children.Add(Ui.Button(L.Text("Создать боковую панель"), () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException(L.Text("Введите название группы."));
            _app.CreateSidebar(edge.SelectedIndex == 0 ? DockEdge.Left : DockEdge.Right, displays[display.SelectedIndex], name.Text.Trim());
        }, true, "create-sidebar"));
        body.Children.Add(Ui.Text(L.Text("В закрытом виде панель занимает 3 px. Внутри — быстрый доступ, часы, заметка и таймер. Любую вкладку можно заменить порталом папки."), 12, Ui.Muted));
        _page.Children.Add(Ui.Card(body));
        foreach (var fence in _app.Workspace.Fences.Where(f => f.IsSidebar)) _page.Children.Add(GroupCard(fence));
        if (!_app.Workspace.Fences.Any(f => f.IsSidebar)) _page.Children.Add(Ui.Text(L.Text("Боковых панелей пока нет."), 13, Ui.Muted));
    }
    private void Shortcuts()
    {
        Intro(L.Text("Нажмите сочетание в поле. Backspace очищает назначение."));
        var prefs = _app.Workspace.Preferences; var body = new StackPanel();
        var bindings = new (string Label, string Value, Action<string> Set)[]
        {
            (L.Text("Быстрый доступ"), prefs.PeekHotkey, v => _app.Workspace.Preferences.PeekHotkey = v),
            (L.Text("Показать / скрыть группы"), prefs.HideHotkey, v => _app.Workspace.Preferences.HideHotkey = v),
            (L.Text("Предыдущая страница"), prefs.PreviousPageHotkey, v => _app.Workspace.Preferences.PreviousPageHotkey = v),
            (L.Text("Следующая страница"), prefs.NextPageHotkey, v => _app.Workspace.Preferences.NextPageHotkey = v),
            (L.Text("Настройки"), prefs.ManagerHotkey, v => _app.Workspace.Preferences.ManagerHotkey = v),
            (L.Text("Поиск и команды"), prefs.SearchHotkey, v => _app.Workspace.Preferences.SearchHotkey = v)
        };
        var fields = bindings.Select(b => (b.Label, Box: Ui.HotkeyInput(body, b.Label, b.Value, _app), b.Set)).ToList();
        body.Children.Add(Ui.Button(L.Text("Сохранить сочетания"), () =>
        {
            var used = new HashSet<HotkeyGesture>();
            foreach (var field in fields.Where(f => f.Box.Text.Length > 0))
            {
                if (!HotkeyGesture.TryParse(field.Box.Text, out var gesture)) throw new InvalidOperationException(L.Text("Некорректное сочетание клавиш."));
                if (!used.Add(gesture)) throw new InvalidOperationException(L.Text("Сочетания не должны повторяться."));
            }
            _app.Mutate(() => { foreach (var field in fields) field.Set(field.Box.Text); }, false);
            _app.UpdateIntegration(); _app.SetStatus(_app.HotkeyStatus); Refresh();
        }, true, "save-shortcuts"));
        body.Children.Add(Ui.Text(_app.HotkeyStatus, 12, Ui.Muted));
        _page.Children.Add(Ui.Card(body));
        _page.Children.Add(Ui.Text(L.Text("Клавиша отдельной группы назначается в её настройках. Внутри группы: Ctrl+F — поиск, Ctrl+Tab — вкладки, F5 — обновить. В настройках Ctrl+K открывает поиск и команды."), 13, Ui.Muted));
    }
    private void Widgets()
    {
        Intro(L.Text("Каждый виджет — отдельная панель. Добавляйте и размещайте их по одному."));
        void AddPanel()
        {
            var display = AppController.Displays().FirstOrDefault(item => item.Id == _selectedMonitorId);
            var panel = _app.AddWidgetPanel(this, display: display);
            if (panel == null) return;
            _widgetFenceId = panel.Id; _widgetTabId = panel.ActiveTabId; _selectedMonitorId = DisplayLayout.Select(panel, AppController.Displays()).Id;
            Refresh();
        }
        var entries = _app.Workspace.Fences.Where(fence => fence.PageId == _app.Workspace.ActivePageId)
            .SelectMany(fence => fence.Tabs.SelectMany(tab => tab.Widgets.Select(widget => (Fence: fence, Tab: tab, Widget: widget)))).ToArray();
        var displays = AppController.Displays();
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var summary = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        summary.Children.Add(Ui.Text(L.Text("Виджеты в наборе"), 18, Ui.Ink, FontWeights.SemiBold));
        var count = Ui.Text(L.Text("Виджеты") + ": " + entries.Length.ToString(L.Culture), 11, Ui.Muted); count.Margin = new Thickness(0, 4, 0, 0); AutomationProperties.SetAutomationId(count, "widget-count"); summary.Children.Add(count); heading.Children.Add(summary);
        var add = LabelAction("Добавить виджет", "plus", AddPanel, true, "add-widget"); add.Margin = new Thickness(12, 0, 0, 0); add.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(add, 1); heading.Children.Add(add); _page.Children.Add(heading);
        var library = new StackPanel(); AutomationProperties.SetAutomationId(library, "widget-library"); _page.Children.Add(library);
        if (entries.Length == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(4, 6, 4, 6) };
            var glyph = Brand.Icon("widgets", Ui.Accent, 32); glyph.HorizontalAlignment = HorizontalAlignment.Left; empty.Children.Add(glyph);
            var title = Ui.Text(L.Text("Добавьте первый виджет"), 17, Ui.Ink, FontWeights.SemiBold); title.Margin = new Thickness(0, 12, 0, 7); empty.Children.Add(title);
            var hint = Ui.Text(L.Text("Выберите виджет. Он появится отдельной панелью; создавать группу заранее не нужно."), 12, Ui.Muted); hint.MaxWidth = 460; hint.HorizontalAlignment = HorizontalAlignment.Left; hint.Margin = new Thickness(0, 0, 0, 16); empty.Children.Add(hint);
            var first = LabelAction("Добавить первый виджет", "plus", AddPanel, id: "widget-empty-add"); first.HorizontalAlignment = HorizontalAlignment.Left; first.Margin = new Thickness(0); empty.Children.Add(first);
            library.Children.Add(Ui.Card(empty)); return;
        }
        var cards = new Grid(); cards.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); cards.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); library.Children.Add(cards);
        for (var index = 0; index < entries.Length; index++)
        {
            if (index % 2 == 0) cards.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var (fence, tab, widget) = entries[index]; var widgetId = widget.Id; var cardBody = new StackPanel();
            var standalone = SingleWidgetPanels.IsSingleWidgetPanel(fence, widgetId);
            var titleRow = new Grid(); titleRow.ColumnDefinitions.Add(new() { Width = new GridLength(28) }); titleRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.Children.Add(Brand.Icon(WidgetCatalog.Icon(widget.Kind), Ui.Accent, 18));
            var name = Ui.Text(string.IsNullOrWhiteSpace(widget.Title) ? WidgetEditor.Labels[(int)widget.Kind] : widget.Title, 13, Ui.Ink, FontWeights.SemiBold); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = name.Text; Grid.SetColumn(name, 1); titleRow.Children.Add(name); cardBody.Children.Add(titleRow);
            var kindLabel = Ui.Text(WidgetEditor.Labels[(int)widget.Kind], 10, Ui.Muted); kindLabel.Margin = new Thickness(28, 3, 0, 5); cardBody.Children.Add(kindLabel);
            var display = DisplayLayout.Select(fence, displays); var monitorIndex = Array.FindIndex(displays, item => item.Id == display.Id);
            var location = Ui.Text((standalone ? fence.Name : fence.Name + " / " + tab.Name) + " · " + L.Format("Монитор {0}", monitorIndex + 1), 10, Ui.Muted); location.TextWrapping = TextWrapping.NoWrap; location.TextTrimming = TextTrimming.CharacterEllipsis; location.ToolTip = location.Text; location.Margin = new Thickness(0, 0, 0, 10); cardBody.Children.Add(location);
            var previewLabel = Ui.Text(L.Text("Пример оформления"), 9, Ui.Muted); previewLabel.Margin = new Thickness(0, 0, 0, 5); cardBody.Children.Add(previewLabel);
            var preview = WidgetCatalog.Preview(_app, fence, widget); preview.Height = 174; cardBody.Children.Add(preview);
            var controls = new Grid { Margin = new Thickness(0, 11, 0, 0) }; controls.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); controls.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var open = Ui.Button(L.Text("Открыть"), () =>
            {
                var current = WidgetEditor.Resolve(_app, fence.Id, tab.Id, widgetId); _widgetFenceId = current.Fence.Id; _widgetTabId = current.Tab.Id; _app.OpenGroup(current.Fence, current.Tab);
            }, id: "widget-open-" + widgetId); open.Margin = new Thickness(0, 0, 6, 0); open.Padding = new Thickness(10, 6, 10, 6); open.FontSize = 11; controls.Children.Add(open);
            var edit = Ui.Button(L.Text("Настроить"), () => { var current = WidgetEditor.Resolve(_app, fence.Id, tab.Id, widgetId); _widgetFenceId = current.Fence.Id; _widgetTabId = current.Tab.Id; WidgetEditor.Edit(_app, current.Fence, current.Tab, current.Widget); }, id: "widget-settings-" + widgetId); edit.IsEnabled = !fence.ContentLocked; edit.Margin = new Thickness(0, 0, 6, 0); edit.Padding = new Thickness(10, 6, 10, 6); edit.FontSize = 11; Grid.SetColumn(edit, 1); controls.Children.Add(edit);
            var remove = Ui.IconButton("trash", () =>
            {
                var current = WidgetEditor.Resolve(_app, fence.Id, tab.Id, widgetId); WorkspaceActions.RequireEditable(current.Fence);
                if (!Ui.Confirm(L.Text("Удалить виджет и его содержимое?"))) return;
                _app.Mutate(() =>
                {
                    var latest = WidgetEditor.Resolve(_app, fence.Id, tab.Id, widgetId); WorkspaceActions.RequireEditable(latest.Fence);
                    if (SingleWidgetPanels.IsSingleWidgetPanel(latest.Fence, widgetId)) _app.Workspace.Fences.Remove(latest.Fence);
                    else latest.Tab.Widgets.Remove(latest.Widget!);
                });
            }, L.Text("Удалить"), "widget-remove-" + widgetId); remove.IsEnabled = !fence.ContentLocked; remove.Width = remove.Height = 32; remove.MinHeight = 0; remove.Margin = new Thickness(0); remove.Padding = new Thickness(7); Grid.SetColumn(remove, 2); controls.Children.Add(remove); cardBody.Children.Add(controls);
            if (fence.ContentLocked)
            {
                var locked = Ui.Text(L.Text("Содержимое группы заблокировано. Снимите блокировку в настройках группы, чтобы изменить виджеты."), 10, Ui.Muted); locked.Margin = new Thickness(0, 8, 0, 0); cardBody.Children.Add(locked);
            }
            if (!standalone)
            {
                var secondary = new Grid { Margin = new Thickness(0, 8, 0, 0) }; secondary.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); secondary.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var separate = Ui.Button(L.Text("В отдельную панель"), () =>
                {
                    var panel = SingleWidgetPanels.Detach(_app, fence.Id, tab.Id, widgetId);
                    _widgetFenceId = panel.Id; _widgetTabId = panel.ActiveTabId; _selectedMonitorId = DisplayLayout.Select(panel, AppController.Displays()).Id; Refresh();
                }, id: "widget-separate-" + widgetId);
                separate.IsEnabled = !fence.ContentLocked; separate.FontSize = 11; separate.Margin = new Thickness(0, 0, 6, 0); separate.Padding = new Thickness(9, 6, 9, 6); separate.ToolTip = L.Text("Перенести этот виджет в собственную панель, сохранив его данные."); secondary.Children.Add(separate);
                var actions = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(actions, 1); secondary.Children.Add(actions);
                void Move(int direction)
                {
                    var current = WidgetEditor.Resolve(_app, fence.Id, tab.Id, widgetId); WorkspaceActions.RequireEditable(current.Fence);
                    var old = current.Tab.Widgets.FindIndex(w => w.Id == widgetId); var next = old + direction; if (old < 0 || next < 0 || next >= current.Tab.Widgets.Count) return;
                    _app.Mutate(() => { var item = current.Tab.Widgets[old]; current.Tab.Widgets.RemoveAt(old); current.Tab.Widgets.Insert(next, item); });
                }
                var widgetIndex = tab.Widgets.FindIndex(item => item.Id == widgetId);
                var up = Ui.IconButton("chevron-up", () => Move(-1), L.Text("Выше"), "widget-up-" + widgetId); up.IsEnabled = !fence.ContentLocked && widgetIndex > 0; actions.Children.Add(up);
                var down = Ui.IconButton("chevron-down", () => Move(1), L.Text("Ниже"), "widget-down-" + widgetId); down.IsEnabled = !fence.ContentLocked && widgetIndex < tab.Widgets.Count - 1; actions.Children.Add(down);
                foreach (Button action in actions.Children) { action.Width = action.Height = 30; action.MinHeight = 0; action.Margin = new Thickness(2, 0, 0, 0); action.Padding = new Thickness(6); }
                cardBody.Children.Add(secondary);
            }
            var card = Ui.Card(cardBody, new Thickness(index % 2 == 0 ? 0 : 6, 0, index % 2 == 0 ? 6 : 0, 12)); card.Padding = new Thickness(12); card.VerticalAlignment = VerticalAlignment.Top;
            if (fence.Id == _widgetFenceId && tab.Id == _widgetTabId) card.BorderBrush = Ui.Accent;
            AutomationProperties.SetAutomationId(card, "widget-card-" + widgetId); AutomationProperties.SetName(card, name.Text); Grid.SetColumn(card, index % 2); Grid.SetRow(card, index / 2); cards.Children.Add(card);
        }
    }
}
