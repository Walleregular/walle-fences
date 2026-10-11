using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace WalleFences.App;

// This editor stores tab metadata and explicit operations only. Existing file references,
// launch metadata and live widget state stay on the latest workspace model until Save.
internal sealed class GroupTabSettings : StackPanel
{
    private sealed class TabDraft
    {
        internal required string Id { get; init; }
        internal required string Name { get; set; }
        internal string? Color { get; set; }
        internal bool Portal { get; set; }
        internal string Path { get; set; } = "";
        internal required string OriginalName { get; init; }
        internal string? OriginalColor { get; init; }
        internal string? OriginalPortalPath { get; init; }
        internal bool Added { get; init; }
        internal int Items { get; init; }
        internal int Widgets { get; init; }
        internal TextBlock? RowName { get; set; }
        internal TextBlock? RowKind { get; set; }
        internal Border? RowColor { get; set; }
    }

    private readonly List<TabDraft> _drafts;
    private readonly HashSet<string> _removed = [];
    private readonly HashSet<string> _confirmedRemovals = [];
    private readonly HashSet<string> _confirmedPortalTabs = [];
    private readonly Fence _source;
    private readonly bool _initiallyLocked;
    private string _groupAccent;
    private readonly ListBox _list;
    private readonly StackPanel _fields;
    private readonly StackPanel _colorFields;
    private readonly DockPanel _activeRow;
    private readonly Button _up, _down, _remove, _activate;
    private readonly TextBox _name, _color, _path;
    private readonly ComboBox _type;
    private readonly Button _browse;
    private readonly Border _colorSwatch;
    private readonly TextBlock _activeLabel;
    private TabDraft? _selected;
    private string _activeId;
    private bool _activeChanged, _orderChanged, _loading;

    internal GroupTabSettings(Fence fence)
    {
        AutomationProperties.SetAutomationId(this, "editor-group-tabs");
        _source = fence;
        _initiallyLocked = fence.ContentLocked;
        _groupAccent = fence.Accent;
        _activeId = fence.Tabs.Any(tab => tab.Id == fence.ActiveTabId) ? fence.ActiveTabId : fence.Tabs.FirstOrDefault()?.Id ?? "";
        _drafts = fence.Tabs.Select(tab => new TabDraft
        {
            Id = tab.Id, Name = tab.Name, Color = tab.Color,
            Portal = !string.IsNullOrWhiteSpace(tab.PortalPath), Path = tab.PortalPath ?? "",
            OriginalName = tab.Name, OriginalColor = tab.Color, OriginalPortalPath = tab.PortalPath,
            Items = tab.Items.Count, Widgets = tab.Widgets.Count
        }).ToList();

        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _up = Ui.IconButton("move-up", () => Move(-1), L.Text("Выше"), "editor-tab-up");
        _down = Ui.IconButton("move-down", () => Move(1), L.Text("Ниже"), "editor-tab-down");
        _remove = Ui.IconButton("trash", Remove, L.Text("Удалить вкладку"), "editor-tab-remove");
        foreach (var button in new[] { _up, _down, _remove }) { button.Width = button.Height = 30; button.Margin = new Thickness(2, 0, 0, 0); actions.Children.Add(button); }
        DockPanel.SetDock(actions, Dock.Right); heading.Children.Add(actions);
        heading.Children.Add(Ui.Text(L.Text("Вкладки группы"), 13, Ui.Ink, FontWeights.SemiBold)); Children.Add(heading);

        _list = new ListBox { MinHeight = 86, MaxHeight = 176, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(_list, "editor-tab-list");
        AutomationProperties.SetName(_list, L.Text("Вкладки группы"));
        _list.SelectionChanged += (_, _) => Select((_list.SelectedItem as ListBoxItem)?.Tag as TabDraft);
        _list.PreviewKeyDown += (_, e) =>
        {
            if (_initiallyLocked || Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox) return;
            if (e.Key is not (Key.F2 or Key.Delete)) return;
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_list, source) is ListBoxItem row && row.Tag is TabDraft tab) SelectRow(tab);
            if (e.Key == Key.F2) { Ui.Guard(RenameSelected); e.Handled = true; }
            else if (e.Key == Key.Delete) { Ui.Guard(Remove); e.Handled = true; }
        };
        Children.Add(new Border { Child = _list, Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(5) });
        var addRow = new WrapPanel { Margin = new Thickness(-3, 8, -3, 12) };
        var add = Ui.Button(L.Text("Добавить вкладку"), () => Add(false), id: "editor-tab-add");
        var portal = Ui.Button(L.Text("Добавить портал"), () => Add(true), id: "editor-tab-add-portal");
        foreach (var button in new[] { add, portal }) { button.Margin = new Thickness(3, 0, 3, 0); button.MinHeight = 30; button.IsEnabled = !_initiallyLocked; addRow.Children.Add(button); }
        Children.Add(addRow);

        _fields = new StackPanel { IsEnabled = !_initiallyLocked };
        _name = Ui.Input(_fields, L.Text("Название вкладки")); AutomationProperties.SetAutomationId(_name, "editor-tab-name");
        _name.TextChanged += (_, _) => { if (!_loading && _selected != null) { _selected.Name = _name.Text; PaintRow(_selected); } };

        _colorFields = new StackPanel();
        Ui.Label(_colorFields, L.Text("Цвет вкладки · HEX или пустое поле"));
        var colorRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        colorRow.ColumnDefinitions.Add(new() { Width = new GridLength(36) });
        colorRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        colorRow.ColumnDefinitions.Add(new() { Width = new GridLength(36) });
        _colorSwatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
        var pickColor = Ui.IconButton(_colorSwatch, PickColor, L.Text("Выбрать цвет вкладки…"), "editor-tab-color-picker"); pickColor.Height = 32;
        _color = new TextBox { MaxLength = 16, Height = 32, Margin = new Thickness(6, 0, 6, 0) };
        AutomationProperties.SetAutomationId(_color, "editor-tab-color"); AutomationProperties.SetName(_color, L.Text("Цвет вкладки · HEX или пустое поле"));
        _color.TextChanged += (_, _) =>
        {
            if (!_loading && _selected != null) { _selected.Color = EmptyToNull(_color.Text); PaintRow(_selected); }
            PaintColor();
        };
        var resetColor = Ui.IconButton("undo", () => _color.Text = "", L.Text("У вкладки нет собственного цвета: используется подсветка группы."), "editor-tab-color-reset"); resetColor.Height = 32;
        colorRow.Children.Add(pickColor); Grid.SetColumn(_color, 1); colorRow.Children.Add(_color); Grid.SetColumn(resetColor, 2); colorRow.Children.Add(resetColor); _colorFields.Children.Add(colorRow); _fields.Children.Add(_colorFields);

        _type = Ui.Combo(_fields, L.Text("Содержимое"), [L.Text("Ссылки и виджеты"), L.Text("Портал папки")]);
        AutomationProperties.SetAutomationId(_type, "editor-tab-type");
        _type.SelectionChanged += (_, _) =>
        {
            if (!_loading && _selected != null) { _selected.Portal = _type.SelectedIndex == 1; PaintRow(_selected); }
            UpdatePath();
        };
        _path = Ui.Input(_fields, L.Text("Папка портала")); AutomationProperties.SetAutomationId(_path, "editor-tab-portal-path");
        _path.TextChanged += (_, _) => { if (!_loading && _selected != null) _selected.Path = _path.Text; };
        _browse = Ui.Button(L.Text("Выбрать папку…"), BrowseFolder, id: "editor-tab-portal-browse"); _browse.Margin = new Thickness(0, 0, 0, 12); _fields.Children.Add(_browse);

        _activeRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        _activate = Ui.Button(L.Text("Сделать активной"), Activate, id: "editor-tab-activate"); _activate.MinHeight = 30; _activate.Margin = new Thickness(0); DockPanel.SetDock(_activate, Dock.Right); _activeRow.Children.Add(_activate);
        _activeLabel = Ui.Text("", 12, Ui.Muted); _activeLabel.Margin = new Thickness(0, 0, 8, 0); _activeRow.Children.Add(_activeLabel); _fields.Children.Add(_activeRow);
        Children.Add(_fields);
        var hint = Ui.Text(L.Text(_initiallyLocked ? "Содержимое группы защищено. Отключите защиту в настройках группы." : "Изменения вкладок применятся после «Сохранить»."), 12, Ui.Muted);
        hint.Margin = new Thickness(0, 2, 0, 12); Children.Add(hint);
        RebuildList(_activeId);
    }

    internal void SetGroupAccent(string accent)
    {
        _groupAccent = accent;
        foreach (var tab in _drafts) PaintRow(tab);
        PaintColor();
    }

    internal void SetAdvancedMode(bool advanced)
    {
        // Keep the same draft fields alive: switching modes never discards edits.
        _colorFields.Visibility = _activeRow.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void Validate(Fence? current = null)
    {
        if (_initiallyLocked) return;
        current ??= _source;
        if (_drafts.Count == 0) throw new InvalidOperationException(L.Text("В группе должна остаться хотя бы одна вкладка."));
        foreach (var tab in _drafts)
        {
            if (string.IsNullOrWhiteSpace(tab.Name)) throw new InvalidOperationException(L.Text("Введите название вкладки."));
            if (tab.Color != null && !ColorPickerWindow.TryHex(tab.Color, out _, out _)) throw new InvalidOperationException(L.Text("Укажите цвет в формате #8FDCC0."));
            if (!PortalChanged(tab)) continue;
            if (!tab.Portal) continue;
            if (string.IsNullOrWhiteSpace(tab.Path)) throw new InvalidOperationException(L.Text("Выбранная папка недоступна."));
            var folder = System.IO.Path.GetFullPath(tab.Path.Trim());
            if (!Directory.Exists(folder)) throw new InvalidOperationException(L.Text("Выбранная папка недоступна."));
            var latest = current.Tabs.FirstOrDefault(existing => existing.Id == tab.Id);
            if (latest != null && string.IsNullOrEmpty(latest.PortalPath) && latest.Items.Count > 0 && !_confirmedPortalTabs.Contains(tab.Id))
            {
                if (!Ui.Confirm(L.Text("Ссылки этой вкладки будут скрыты на время работы в режиме портала. Продолжить?"))) throw new OperationCanceledException();
                _confirmedPortalTabs.Add(tab.Id);
            }
        }
        foreach (var tab in current.Tabs.Where(tab => _removed.Contains(tab.Id) && !_confirmedRemovals.Contains(tab.Id)))
        {
            if ((tab.Items.Count > 0 || tab.Widgets.Count > 0 || !string.IsNullOrWhiteSpace(tab.PortalPath)) && !ConfirmRemoval(tab.Name)) throw new OperationCanceledException();
            _confirmedRemovals.Add(tab.Id);
        }
    }

    internal void Apply(Fence fence, Workspace workspace)
    {
        if (_initiallyLocked) return;
        var changed = _orderChanged || _activeChanged || _removed.Count > 0 || _drafts.Any(tab => tab.Added || NameChanged(tab) || ColorChanged(tab) || PortalChanged(tab));
        if (!changed) return;
        WorkspaceActions.RequireEditable(fence);

        // A tab removed or transferred while this dialog was open is not recreated.
        // Tabs added elsewhere remain in their current positions when known tabs reorder.
        var retained = fence.Tabs.Where(tab => !_removed.Contains(tab.Id)).ToList();
        var removedHere = fence.Tabs.Where(tab => _removed.Contains(tab.Id)).Select(tab => tab.Id).ToHashSet(StringComparer.Ordinal);
        var added = _drafts.Where(tab => tab.Added && retained.All(current => current.Id != tab.Id)).Select(tab => new FenceTab
        {
            Id = tab.Id, Name = tab.Name.Trim(), Color = NormalColor(tab.Color), PortalPath = PortalPath(tab)
        }).ToList();
        if (retained.Count + added.Count == 0) throw new InvalidOperationException(L.Text("В группе должна остаться хотя бы одна вкладка."));
        foreach (var draft in _drafts.Where(tab => !tab.Added))
        {
            var current = retained.FirstOrDefault(tab => tab.Id == draft.Id);
            if (current == null) continue;
            if (NameChanged(draft)) current.Name = draft.Name.Trim();
            if (ColorChanged(draft)) current.Color = NormalColor(draft.Color);
            if (PortalChanged(draft)) current.PortalPath = PortalPath(draft);
        }
        retained.AddRange(added);
        if (_orderChanged)
        {
            var byId = retained.ToDictionary(tab => tab.Id, StringComparer.Ordinal);
            var knownIds = _drafts.Select(tab => tab.Id).Where(byId.ContainsKey).ToHashSet(StringComparer.Ordinal);
            var ordered = new Queue<FenceTab>(_drafts.Where(tab => byId.ContainsKey(tab.Id)).Select(tab => byId[tab.Id]));
            retained = retained.Select(tab => knownIds.Contains(tab.Id) ? ordered.Dequeue() : tab).ToList();
        }
        fence.Tabs.Clear(); fence.Tabs.AddRange(retained);
        workspace.Rules.RemoveAll(rule => rule.FenceId == fence.Id && removedHere.Contains(rule.TabId));
        if (_activeChanged && retained.Any(tab => tab.Id == _activeId)) fence.ActiveTabId = _activeId;
        else if (retained.All(tab => tab.Id != fence.ActiveTabId)) fence.ActiveTabId = retained.First().Id;
    }

    private void RebuildList(string? selectedId)
    {
        _loading = true;
        _list.Items.Clear();
        foreach (var tab in _drafts)
        {
            var row = new Grid { Margin = new Thickness(6, 5, 6, 5) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(6) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(30) });
            tab.RowColor = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 1, 0, 1) }; row.Children.Add(tab.RowColor);
            tab.RowName = Ui.Text(tab.Name, 12, Ui.Ink, FontWeights.Medium); tab.RowName.TextWrapping = TextWrapping.NoWrap; tab.RowName.TextTrimming = TextTrimming.CharacterEllipsis; tab.RowName.VerticalAlignment = VerticalAlignment.Center; tab.RowName.Margin = new Thickness(6, 0, 10, 0); Grid.SetColumn(tab.RowName, 1); row.Children.Add(tab.RowName);
            tab.RowKind = Ui.Text("", 10.5, Ui.Muted); tab.RowKind.TextWrapping = TextWrapping.NoWrap; tab.RowKind.VerticalAlignment = VerticalAlignment.Center; tab.RowKind.Margin = new Thickness(0, 0, 6, 0); Grid.SetColumn(tab.RowKind, 2); row.Children.Add(tab.RowKind);
            PaintRow(tab);
            var item = new ListBoxItem { Content = row, Tag = tab, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 1, 0, 1) };
            item.ContextMenu = TabMenu(tab);
            item.PreviewMouseRightButtonDown += (_, _) => { SelectRow(tab); item.Focus(); };
            item.ContextMenuOpening += (_, _) => SelectRow(tab);
            Button more = null!;
            more = Ui.IconButton("more", () =>
            {
                if (!SelectRow(tab)) return;
                var menu = TabMenu(tab); menu.PlacementTarget = more; menu.Placement = PlacementMode.Bottom;
                more.ContextMenu = menu; menu.IsOpen = true;
            }, L.Text("Вкладки"), "editor-tab-more-" + tab.Id);
            more.Width = more.Height = 26; more.Margin = new Thickness(2, 0, 0, 0);
            Grid.SetColumn(more, 3); row.Children.Add(more);
            AutomationProperties.SetAutomationId(item, "editor-tab-" + tab.Id); _list.Items.Add(item);
            if (tab.Id == selectedId) _list.SelectedItem = item;
        }
        if (_list.SelectedItem == null && _list.Items.Count > 0) _list.SelectedIndex = 0;
        _loading = false;
        Select((_list.SelectedItem as ListBoxItem)?.Tag as TabDraft);
    }

    private void Select(TabDraft? tab)
    {
        if (_loading) return;
        _selected = tab; _loading = true;
        _fields.Visibility = tab == null ? Visibility.Collapsed : Visibility.Visible;
        _name.Text = tab?.Name ?? ""; _color.Text = tab?.Color ?? ""; _type.SelectedIndex = tab?.Portal == true ? 1 : 0; _path.Text = tab?.Path ?? "";
        _loading = false; PaintColor(); UpdatePath(); UpdateActions();
    }

    private bool SelectRow(TabDraft tab)
    {
        var item = _list.Items.OfType<ListBoxItem>().FirstOrDefault(row => ReferenceEquals(row.Tag, tab));
        if (item == null) return false;
        _list.SelectedItem = item;
        return true;
    }

    private ContextMenu TabMenu(TabDraft tab)
    {
        var menu = new ContextMenu();
        AutomationProperties.SetAutomationId(menu, "editor-tab-menu-" + tab.Id);
        MenuItem Command(string label, string icon, Action action, string id, string? gesture = null)
        {
            var command = new MenuItem { Header = L.Text(label), Icon = Brand.Icon(icon, Ui.Ink, 16), InputGestureText = gesture ?? "" };
            AutomationProperties.SetAutomationId(command, id + "-" + tab.Id);
            command.Click += (_, _) => Ui.Guard(() => { if (SelectRow(tab) && !_initiallyLocked) action(); });
            return command;
        }
        var rename = Command("Переименовать", "edit", () => Dispatcher.BeginInvoke((Action)(() => { if (SelectRow(tab)) RenameSelected(); }), DispatcherPriority.Input), "editor-tab-menu-rename", "F2");
        var activate = Command("Сделать активной", "check", Activate, "editor-tab-menu-activate");
        var up = Command("Выше", "move-up", () => Move(-1), "editor-tab-menu-up");
        var down = Command("Ниже", "move-down", () => Move(1), "editor-tab-menu-down");
        var remove = Command("Удалить вкладку", "trash", Remove, "editor-tab-menu-remove", "Delete");
        menu.Items.Add(rename); menu.Items.Add(activate); menu.Items.Add(new Separator());
        menu.Items.Add(up); menu.Items.Add(down); menu.Items.Add(new Separator()); menu.Items.Add(remove);
        menu.Opened += (_, _) =>
        {
            var editable = SelectRow(tab) && !_initiallyLocked;
            var index = _drafts.IndexOf(tab);
            rename.IsEnabled = editable;
            activate.IsChecked = tab.Id == _activeId; activate.IsEnabled = editable && !activate.IsChecked;
            up.IsEnabled = editable && index > 0; down.IsEnabled = editable && index >= 0 && index < _drafts.Count - 1;
            remove.IsEnabled = editable && _drafts.Count > 1;
            remove.ToolTip = L.Text(_initiallyLocked ? "Содержимое группы защищено. Отключите защиту в настройках группы." : _drafts.Count < 2 ? "В группе должна остаться хотя бы одна вкладка." : "Удалить вкладку");
            ToolTipService.SetShowOnDisabled(remove, true);
        };
        return menu;
    }

    private void RenameSelected()
    {
        if (_selected == null || _initiallyLocked) return;
        _name.Focus(); _name.SelectAll();
    }

    private void Activate()
    {
        if (_selected == null || _initiallyLocked) return;
        _activeId = _selected.Id; _activeChanged = true; UpdateActions();
    }

    private void Add(bool portal)
    {
        if (_initiallyLocked) return;
        var tab = new TabDraft { Id = Guid.NewGuid().ToString("N"), Name = L.Text(portal ? "Папка" : "Файлы"), OriginalName = "", Portal = portal, Added = true };
        _drafts.Add(tab); _activeId = tab.Id; _activeChanged = true;
        RebuildList(tab.Id); _name.Focus(); _name.SelectAll();
    }

    private void Move(int direction)
    {
        if (_selected == null || _initiallyLocked) return;
        var index = _drafts.IndexOf(_selected); var next = index + direction;
        if (next < 0 || next >= _drafts.Count) return;
        (_drafts[index], _drafts[next]) = (_drafts[next], _drafts[index]); _orderChanged = true; RebuildList(_selected.Id);
        (_list.SelectedItem as ListBoxItem)?.Focus();
    }

    private void Remove()
    {
        if (_selected == null || _initiallyLocked || _drafts.Count < 2) return;
        var tab = _selected;
        if (tab.Items > 0 || tab.Widgets > 0 || !string.IsNullOrWhiteSpace(tab.OriginalPortalPath))
        {
            if (!ConfirmRemoval(tab.Name)) return;
            _confirmedRemovals.Add(tab.Id);
        }
        var index = _drafts.IndexOf(tab); _drafts.Remove(tab); if (!tab.Added) _removed.Add(tab.Id);
        var next = _drafts[Math.Min(index, _drafts.Count - 1)].Id;
        if (_activeId == tab.Id) { _activeId = next; _activeChanged = true; }
        RebuildList(next);
        (_list.SelectedItem as ListBoxItem)?.Focus();
    }

    private void BrowseFolder()
    {
        if (_selected == null || _initiallyLocked || Window.GetWindow(this) is not Window owner) return;
        var path = ContentPickers.SelectFolder(owner, L.Text("Выберите папку для портала"), _path.Text);
        if (path != null) _path.Text = path;
    }

    private void PickColor()
    {
        if (_selected == null || _initiallyLocked) return;
        var color = ColorPickerWindow.Pick(Window.GetWindow(this), L.Text("Выбрать цвет вкладки…"), string.IsNullOrWhiteSpace(_color.Text) ? _groupAccent : _color.Text);
        if (color != null) _color.Text = color;
    }

    private void PaintColor()
    {
        var inherited = string.IsNullOrWhiteSpace(_color.Text);
        var valid = ColorPickerWindow.TryHex(inherited ? _groupAccent : _color.Text, out _, out var color);
        _colorSwatch.Background = valid ? new SolidColorBrush(color) : Brushes.Transparent;
        _color.BorderBrush = valid ? Ui.Line : Ui.Brush("#D6747D");
        _color.ToolTip = inherited ? L.Text("У вкладки нет собственного цвета: используется подсветка группы.") : null;
    }

    private void PaintRow(TabDraft tab)
    {
        if (tab.RowName != null) tab.RowName.Text = tab.Name;
        if (tab.RowKind != null) tab.RowKind.Text = tab.Portal ? L.Text("Портал папки") : tab.Widgets > 0 && tab.Items == 0 ? L.Text("Виджеты") : L.Text("Файлы");
        if (tab.RowColor != null) tab.RowColor.Background = ColorPickerWindow.TryHex(tab.Color ?? _groupAccent, out _, out var color) ? new SolidColorBrush(color) : Ui.Accent;
    }

    private void UpdatePath()
    {
        var visible = _type.SelectedIndex == 1;
        _path.Visibility = _browse.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        // Ui.Input creates a separate label directly before the field.
        var index = _fields.Children.IndexOf(_path);
        if (index > 0) _fields.Children[index - 1].Visibility = _path.Visibility;
    }

    private void UpdateActions()
    {
        var index = _selected == null ? -1 : _drafts.IndexOf(_selected);
        _up.IsEnabled = !_initiallyLocked && index > 0; _down.IsEnabled = !_initiallyLocked && index >= 0 && index < _drafts.Count - 1;
        _remove.IsEnabled = !_initiallyLocked && index >= 0 && _drafts.Count > 1;
        _remove.ToolTip = L.Text(_drafts.Count < 2 ? "В группе должна остаться хотя бы одна вкладка." : "Удалить вкладку");
        var active = _selected?.Id == _activeId;
        _activate.IsEnabled = !_initiallyLocked && _selected != null && !active;
        _activeLabel.Text = active ? L.Text("Активная вкладка") : "";
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool ConfirmRemoval(string name) => Ui.Confirm(L.Format("Удалить вкладку «{0}»? Ссылки и виджеты будут удалены из группы; исходные файлы останутся на месте.", name));
    private static string? NormalColor(string? value) => value == null ? null : ColorPickerWindow.TryHex(value, out var hex, out _) ? hex : value.Trim();
    private static string? PortalPath(TabDraft tab) => tab.Portal ? System.IO.Path.GetFullPath(tab.Path.Trim()) : null;
    private static bool NameChanged(TabDraft tab) => tab.Name.Trim() != tab.OriginalName;
    private static bool ColorChanged(TabDraft tab) => !string.Equals(EmptyToNull(tab.Color), EmptyToNull(tab.OriginalColor), StringComparison.OrdinalIgnoreCase);
    private static bool PortalChanged(TabDraft tab) => tab.Added || tab.Portal != !string.IsNullOrWhiteSpace(tab.OriginalPortalPath) || tab.Portal && !string.Equals(tab.Path.Trim(), tab.OriginalPortalPath?.Trim(), StringComparison.OrdinalIgnoreCase);
}
