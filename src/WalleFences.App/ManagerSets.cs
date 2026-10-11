using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private const string SetDragFormat = "WalleFences.ManagerSet";

    private void Pages()
    {
        var actions = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        actions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var description = Ellipsis(L.Text("В каждом наборе — свои группы, вкладки и виджеты."), 11, Ui.Muted);
        description.ToolTip = description.Text; description.Margin = new Thickness(0, 0, 12, 0); actions.Children.Add(description);
        var create = Ui.Row(LabelAction("Новый набор", "plus", () => SetEditor.Create(_app), true, "new-page"));
        Button templates = null!;
        templates = CompactAction("Шаблоны набора", "chevron-down", () =>
        {
            var menu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = templates };
            menu.Items.Add(MenuAction("Набор виджетов", "widgets", () => SetEditor.Create(_app, SetTemplate.Widgets), "new-widget-set"));
            menu.Items.Add(MenuAction("Набор с файловыми вкладками", "tabs", () => SetEditor.Create(_app, SetTemplate.TabbedFiles), "new-tabbed-set"));
            menu.Items.Add(MenuAction("Копия текущего набора", "copy", () => DuplicateSet(_app.Workspace.ActivePageId), "new-copy-set"));
            templates.ContextMenu = menu; menu.IsOpen = true;
        }, "set-templates");
        create.Children.Add(templates); Grid.SetColumn(create, 1); actions.Children.Add(create); _page.Children.Add(actions);

        var list = new StackPanel(); AutomationProperties.SetAutomationId(list, "set-list");
        foreach (var page in _app.Workspace.Pages.ToArray()) list.Children.Add(SetRow(page.Id));
        _page.Children.Add(new Border { Child = list, Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true });
        var hint = Ui.Text(L.Text("Перетащите наборы за ручку слева, чтобы изменить порядок."), 10, Ui.Muted);
        hint.Margin = new Thickness(0, 9, 0, 0); _page.Children.Add(hint);
    }

    private Border SetRow(string pageId)
    {
        var page = _app.Workspace.Pages.First(p => p.Id == pageId);
        var groups = _app.Workspace.Fences.Where(f => f.PageId == pageId).ToArray();
        var widgets = groups.SelectMany(f => f.Tabs).SelectMany(t => t.Widgets).ToArray();
        var selected = pageId == _app.Workspace.ActivePageId;
        var row = new Grid { Margin = new Thickness(8, 12, 10, 12) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 90 });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(96) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(34) });
        var grip = DragGrip("page-grip-" + pageId); grip.VerticalAlignment = VerticalAlignment.Center;
        WireDragGrip(grip, SetDragFormat, pageId); row.Children.Add(grip);

        var details = new StackPanel { Margin = new Thickness(0, 0, 15, 0) };
        var name = new TextBox
        {
            Text = page.Name, IsReadOnly = true, Focusable = false, FontFamily = Ui.AppFont, FontWeight = FontWeights.SemiBold,
            FontSize = 13, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1),
            Padding = new Thickness(2, 3, 2, 3), Margin = new Thickness(-3, -3, 0, 0), MinWidth = 0, MinHeight = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch, ToolTip = page.Name, VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(name, "page-name-" + pageId); AutomationProperties.SetName(name, L.Text("Название набора"));
        details.Children.Add(name);
        var counts = Ellipsis(string.Join(" · ", new[]
        {
            L.Text("Группы") + ": " + groups.Length.ToString(L.Culture),
            L.Text("Вкладки") + ": " + groups.Sum(f => f.Tabs.Count).ToString(L.Culture),
            L.Text("Виджеты") + ": " + widgets.Length.ToString(L.Culture)
        }), 10, Ui.Muted);
        AutomationProperties.SetAutomationId(counts, "page-counts-" + pageId); counts.Margin = new Thickness(0, 2, 0, 4); details.Children.Add(counts);
        details.Children.Add(SetContents(groups, widgets)); Grid.SetColumn(details, 1); row.Children.Add(details);

        var open = Ui.Button(selected ? L.Text("Активен") : L.Text("Открыть"), () =>
        {
            if (_app.Workspace.Pages.Any(p => p.Id == pageId)) _app.SwitchPage(pageId);
        }, id: "page-switch-" + pageId);
        open.Height = 32; open.Margin = new Thickness(0, 0, 7, 0); open.Padding = new Thickness(10, 4, 10, 4);
        open.Background = selected ? Ui.AccentWash : Ui.Raised; open.BorderBrush = selected ? Ui.Accent : Ui.Line;
        open.IsEnabled = !selected; open.ToolTip = L.Text(selected ? "Этот набор сейчас открыт на рабочем столе" : "Открыть набор на рабочем столе");
        ToolTipService.SetShowOnDisabled(open, true); AutomationProperties.SetItemStatus(open, selected ? L.Text("Активен") : "");
        Grid.SetColumn(open, 2); row.Children.Add(open);

        var editing = false;
        void BeginEdit()
        {
            var current = _app.Workspace.Pages.FirstOrDefault(p => p.Id == pageId); if (current == null) return;
            name.Text = current.Name; editing = true; name.IsReadOnly = false; name.Focusable = true;
            name.Background = Ui.Raised; name.BorderBrush = Ui.Accent; name.ToolTip = L.Text("Enter — сохранить, Esc — отменить");
            name.Focus(); name.SelectAll();
        }
        void EndEdit(bool save)
        {
            if (!editing) return; editing = false;
            var current = _app.Workspace.Pages.FirstOrDefault(p => p.Id == pageId);
            var value = name.Text.Trim(); name.IsReadOnly = true; name.Focusable = false;
            name.Background = Brushes.Transparent; name.BorderBrush = Brushes.Transparent;
            if (current == null) { Refresh(); return; }
            if (save && value.Length > 0) RenameSet(pageId, value);
            else name.Text = current.Name;
            name.ToolTip = name.Text;
            Refresh();
        }
        name.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            Ui.Guard(() => EndEdit(e.Key == Key.Enter)); e.Handled = true;
        };
        name.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || editing) return;
            Ui.Guard(BeginEdit); e.Handled = true;
        };
        name.LostKeyboardFocus += (_, _) => Ui.Guard(() => EndEdit(true));

        Button more = null!;
        more = CompactAction("Действия набора", "more", () =>
        {
            var current = _app.Workspace.Pages.FirstOrDefault(p => p.Id == pageId); if (current == null) return;
            var menu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = more };
            menu.Items.Add(MenuAction("Переименовать", "edit", () => Dispatcher.BeginInvoke((Action)BeginEdit, DispatcherPriority.Input), "page-rename-" + pageId));
            menu.Items.Add(MenuAction("Дублировать набор", "copy", () => DuplicateSet(pageId), "page-copy-" + pageId));
            menu.Items.Add(new Separator());
            var index = _app.Workspace.Pages.IndexOf(current);
            var up = MenuAction("Переместить выше", "chevron-up", () => MoveSet(pageId, -1), "page-up-" + pageId); up.IsEnabled = index > 0; menu.Items.Add(up);
            var down = MenuAction("Переместить ниже", "chevron-down", () => MoveSet(pageId, 1), "page-down-" + pageId); down.IsEnabled = index < _app.Workspace.Pages.Count - 1; menu.Items.Add(down);
            menu.Items.Add(new Separator());
            var delete = MenuAction("Удалить набор", "trash", () => DeletePage(current), "page-delete-" + pageId); delete.IsEnabled = _app.Workspace.Pages.Count > 1;
            if (!delete.IsEnabled) { delete.ToolTip = L.Text("Нельзя удалить последний набор."); ToolTipService.SetShowOnDisabled(delete, true); }
            menu.Items.Add(delete); AutomationProperties.SetAutomationId(menu, "page-actions-" + pageId); more.ContextMenu = menu; menu.IsOpen = true;
        }, "page-more-" + pageId);
        more.Margin = new Thickness(0); Grid.SetColumn(more, 3); row.Children.Add(more);
        var surface = new Border
        {
            Child = row, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, _app.Workspace.Pages[^1].Id == pageId ? 0 : 1),
            Background = selected ? Ui.AccentWash : Brushes.Transparent, AllowDrop = true
        };
        AutomationProperties.SetAutomationId(surface, "page-row-" + pageId); AutomationProperties.SetName(surface, page.Name);
        AutomationProperties.SetItemStatus(surface, selected ? L.Text("Активен") : "");
        surface.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(SetDragFormat)) return;
            e.Effects = e.Data.GetData(SetDragFormat) is string source && source != pageId && _app.Workspace.Pages.Any(p => p.Id == source) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        surface.Drop += (_, e) =>
        {
            if (e.Data.GetData(SetDragFormat) is not string source) return;
            Ui.Guard(() => ReorderSets(source, pageId)); e.Handled = true;
        };
        return surface;
    }

    private static UIElement SetContents(Fence[] groups, DesktopWidget[] widgets)
    {
        if (groups.Length == 0) return Ellipsis(L.Text("Пустой набор — добавьте группы или виджеты"), 10, Ui.Muted);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var group in groups.Take(3))
        {
            var chip = new Grid { Margin = new Thickness(0, 0, 12, 0) };
            chip.ColumnDefinitions.Add(new() { Width = new GridLength(10) }); chip.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            chip.Children.Add(new Border { Width = 3, Height = 11, CornerRadius = new CornerRadius(2), Background = Ui.Brush(group.Accent), HorizontalAlignment = HorizontalAlignment.Left });
            var label = Ellipsis(group.Name, 10, Ui.Muted); label.MaxWidth = 100; label.ToolTip = group.Name; Grid.SetColumn(label, 1); chip.Children.Add(label); content.Children.Add(chip);
        }
        if (groups.Length > 3) content.Children.Add(Ui.Text("+" + (groups.Length - 3).ToString(L.Culture), 10, Ui.Muted));
        var names = groups.Select(g => g.Name).Concat(widgets.Select(w => string.IsNullOrWhiteSpace(w.Title) ? WidgetEditor.Labels[(int)w.Kind] : w.Title));
        var host = new Border { Child = content, ClipToBounds = true, ToolTip = string.Join("\n", names), MaxHeight = 20 };
        AutomationProperties.SetName(host, string.Join(" · ", names)); return host;
    }

    internal void RenameSet(string pageId, string name)
    {
        var page = _app.Workspace.Pages.FirstOrDefault(p => p.Id == pageId); if (page == null) return;
        var value = name.Trim(); if (value.Length == 0) throw new InvalidOperationException(L.Text("Укажите название набора."));
        if (page.Name == value) return;
        _app.Mutate(() => { var current = _app.Workspace.Pages.FirstOrDefault(p => p.Id == pageId); if (current != null) current.Name = value; }, false);
    }

    internal void DuplicateSet(string pageId)
    {
        var page = _app.Workspace.Pages.FirstOrDefault(p => p.Id == pageId); if (page == null) return;
        if (_app.IsPeek) _app.Peek();
        _app.Mutate(() =>
        {
            var source = _app.Workspace.Pages.FirstOrDefault(p => p.Id == pageId); if (source == null) return;
            // CopyCurrent reads the workspace's active ID. Select the source inside
            // the single mutation so no intermediate desktop or cancelled dialog appears.
            _app.Workspace.ActivePageId = source.Id;
            WorkspaceSets.Create(_app.Workspace, L.Format("{0} — копия", source.Name), SetTemplate.CopyCurrent);
        });
        _app.SetStatus(L.Text("Копия набора создана"));
    }

    internal void MoveSet(string pageId, int delta)
    {
        var pages = _app.Workspace.Pages; var page = pages.FirstOrDefault(p => p.Id == pageId); if (page == null) return;
        var from = pages.IndexOf(page); var target = from + delta; if (target < 0 || target >= pages.Count) return;
        _app.Mutate(() => { pages.RemoveAt(from); pages.Insert(target, page); }, false);
    }

    internal void ReorderSets(string sourceId, string beforeId)
    {
        var pages = _app.Workspace.Pages; var source = pages.FirstOrDefault(p => p.Id == sourceId); var target = pages.FirstOrDefault(p => p.Id == beforeId);
        if (source == null || target == null || source == target) return;
        var from = pages.IndexOf(source); var to = pages.IndexOf(target); if (from + 1 == to) return;
        _app.Mutate(() => { pages.RemoveAt(from); pages.Insert(from < to ? to - 1 : to, source); }, false);
    }

    private void DeletePage(DesktopPage page)
    {
        var current = _app.Workspace.Pages.FirstOrDefault(p => p.Id == page.Id); if (current == null) return;
        if (_app.Workspace.Pages.Count <= 1) { Ui.Error(L.Text("Нельзя удалить последний набор.")); return; }
        var target = _app.Workspace.Pages.First(p => p.Id != current.Id);
        if (!Ui.Confirm(L.Format("Удалить набор «{0}»? Группы и виджеты перейдут в «{1}». Файлы останутся на диске.", current.Name, target.Name))) return;
        _app.Mutate(() =>
        {
            var source = _app.Workspace.Pages.FirstOrDefault(p => p.Id == page.Id);
            var destination = _app.Workspace.Pages.FirstOrDefault(p => p.Id != page.Id);
            if (source == null || destination == null) return;
            foreach (var fence in _app.Workspace.Fences.Where(f => f.PageId == source.Id)) fence.PageId = destination.Id;
            _app.Workspace.Pages.Remove(source); if (_app.Workspace.ActivePageId == source.Id) _app.Workspace.ActivePageId = destination.Id;
        });
    }
}
