using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private void ShowEdgeBlocksEditor()
    {
        var displays = AppController.Displays();
        var currentDisplay = _selectedMonitorId ?? (SelectedFence is { } fence ? DisplayLayout.Select(fence, displays).Id : (displays.FirstOrDefault(d => d.Primary) ?? displays[0]).Id);
        var dialog = new FormWindow(L.Text("Разделить край на блоки"), 580) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        AutomationProperties.SetAutomationId(dialog, "edge-blocks-editor");
        var display = Ui.Combo(dialog.Body, L.Text("Монитор"), displays.Select((item, i) => L.Format("Монитор {0} · {1}", i + 1, item.Name)), Math.Max(0, displays.ToList().FindIndex(item => item.Id == currentDisplay)));
        var edge = Ui.Combo(dialog.Body, L.Text("Край экрана"), Editors.EdgeLabels.Skip(1), SelectedFence is { Edge: not DockEdge.None } selected ? (int)selected.Edge - 1 : 0);
        var count = Ui.Combo(dialog.Body, L.Text("Количество блоков"), Enumerable.Range(1, 12).Select(value => value.ToString(L.Culture)), 4);
        var preview = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetAutomationId(preview, "edge-blocks-preview"); dialog.Body.Children.Add(preview);
        var dimensions = Ui.Text("", 11, Ui.Muted); dimensions.Margin = new Thickness(0, 0, 0, 12); dialog.Body.Children.Add(dimensions);
        Ui.Label(dialog.Body, L.Text("Группы выбранного монитора"));
        var groupList = new StackPanel();
        var scroll = new ScrollViewer { Content = groupList, MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 0, 10) };
        dialog.Body.Children.Add(scroll);
        var create = Ui.Check(dialog.Body, L.Text("Создать пустые группы для свободных блоков"), false);
        var hint = Ui.Text(L.Text("Группы размещаются по порядку: каждой соответствует отдельный блок."), 11, Ui.Muted); dialog.Body.Children.Add(hint);
        var selectedIds = new HashSet<string>();
        Fence[] eligible = [];

        void RefreshPreview()
        {
            var blockCount = count.SelectedIndex + 1;
            var assigned = eligible.Where(item => selectedIds.Contains(item.Id)).ToArray();
            preview.Columns = blockCount; preview.Children.Clear();
            for (var i = 0; i < blockCount; i++)
            {
                var name = i < assigned.Length ? assigned[i].Name : create.IsChecked == true ? L.Format("Блок {0}", i + 1) : L.Text("Свободно");
                var text = Ellipsis(name, 10); text.HorizontalAlignment = HorizontalAlignment.Center; text.TextAlignment = TextAlignment.Center;
                var block = new Border { Child = text, Height = 38, Padding = new Thickness(3), Margin = new Thickness(0, 0, i == blockCount - 1 ? 0 : 2, 0), CornerRadius = new CornerRadius(4), BorderBrush = i < assigned.Length ? Ui.Brush(assigned[i].Accent) : Ui.Line, BorderThickness = new Thickness(1), Background = i < assigned.Length || create.IsChecked == true ? Ui.AccentWash : Ui.Surface, ToolTip = L.Format("Блок {0} из {1}", i + 1, blockCount) + " · " + name };
                preview.Children.Add(block);
            }
            var target = displays[display.SelectedIndex];
            var vertical = edge.SelectedIndex is 1 or 2;
            var length = (vertical ? target.Height : target.Width) / target.Scale / blockCount;
            dimensions.Text = L.Format("Размер блока вдоль края: {0:0.#} px", length) + " · " + L.Text(vertical ? "Сверху вниз" : "Слева направо");
            hint.Text = assigned.Length > blockCount ? L.Text("Выберите не больше групп, чем блоков.") : L.Text("Группы размещаются по порядку: каждой соответствует отдельный блок.");
        }
        void RefreshGroups()
        {
            var target = displays[display.SelectedIndex];
            eligible = _app.Workspace.Fences.Where(item => item.PageId == _app.Workspace.ActivePageId && DisplayLayout.Select(item, displays).Id == target.Id).ToArray();
            selectedIds.Clear();
            foreach (var item in eligible.Take(count.SelectedIndex + 1)) selectedIds.Add(item.Id);
            groupList.Children.Clear();
            foreach (var item in eligible)
            {
                var id = item.Id;
                var check = Ui.Check(groupList, item.Name, selectedIds.Contains(id));
                AutomationProperties.SetAutomationId(check, "edge-blocks-group-" + id);
                check.Checked += (_, _) => { selectedIds.Add(id); RefreshPreview(); };
                check.Unchecked += (_, _) => { selectedIds.Remove(id); RefreshPreview(); };
            }
            if (eligible.Length == 0) groupList.Children.Add(Ui.Text(L.Text("На этом мониторе пока нет групп."), 12, Ui.Muted));
            RefreshPreview();
        }
        display.SelectionChanged += (_, _) => RefreshGroups();
        edge.SelectionChanged += (_, _) => RefreshPreview();
        count.SelectionChanged += (_, _) => RefreshPreview();
        create.Checked += (_, _) => RefreshPreview(); create.Unchecked += (_, _) => RefreshPreview();
        dialog.AddSave(() =>
        {
            var ids = eligible.Where(item => selectedIds.Contains(item.Id)).Select(item => item.Id).ToArray();
            if (ids.Length == 0 && create.IsChecked != true) throw new InvalidOperationException(L.Text("Выберите группу или включите создание пустых блоков."));
            _app.ArrangeEdgeBlocks((DockEdge)(edge.SelectedIndex + 1), count.SelectedIndex + 1, displays[display.SelectedIndex], ids, create.IsChecked == true);
            _selectedMonitorId = displays[display.SelectedIndex].Id;
            _app.SaveNow();
        });
        RefreshGroups(); dialog.ShowDialog();
    }
}
