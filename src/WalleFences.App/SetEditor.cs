namespace WalleFences.App;

internal static class SetEditor
{
    internal static void Create(AppController app, SetTemplate template = SetTemplate.Empty)
    {
        var dialog = new FormWindow(L.Text("Новый набор"));
        var name = Ui.Input(dialog.Body, L.Text("Название набора"), template == SetTemplate.Widgets ? L.Text("Виджеты") : template == SetTemplate.CopyCurrent ? L.Format("{0} — копия", app.Workspace.Pages.First(p => p.Id == app.Workspace.ActivePageId).Name) : L.Text("Новый набор"));
        var choice = Ui.Combo(dialog.Body, L.Text("Начальное содержимое"), [L.Text("Пустой набор"), L.Text("Копия текущего набора"), L.Text("Группа с файловыми вкладками"), L.Text("Только виджеты: обзор, фокус, заметки")], (int)template);
        var displays = AppController.Displays();
        var monitor = Ui.Combo(dialog.Body, L.Text("Монитор для новых групп"), displays.Select((d, i) => L.Format("Монитор {0} · {1} · {2:0}%", i + 1, d.Name, d.Scale / AppController.GlobalScale * 100)).ToArray(), Array.FindIndex(displays, d => d.Primary));
        dialog.Body.Children.Add(Ui.Text(L.Text("Копия получает независимые настройки и заметки. Файлы копировать не нужно — используются ссылки. Новые панели раскрываются из полоски толщиной 3 пикселя."), 12, Ui.Muted));
        void State() => monitor.IsEnabled = choice.SelectedIndex is (int)SetTemplate.TabbedFiles or (int)SetTemplate.Widgets;
        choice.SelectionChanged += (_, _) => State(); State();
        dialog.AddSave(() =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException(L.Text("Укажите название набора."));
            app.CreateSet(name.Text.Trim(), (SetTemplate)choice.SelectedIndex, displays[Math.Max(0, monitor.SelectedIndex)]);
        }); dialog.ShowDialog();
    }
}

internal sealed partial class AppController
{
    internal void CreateSet(string name, SetTemplate template, DisplayArea display)
    {
        if (IsPeek) Peek();
        Mutate(() =>
        {
            var page = WorkspaceSets.Create(Workspace, name, template);
            if (template is SetTemplate.TabbedFiles or SetTemplate.Widgets)
                foreach (var fence in Workspace.Fences.Where(f => f.PageId == page.Id))
                { fence.X = display.X + 16 * display.Scale; fence.Y = display.Y; DisplayLayout.Capture(fence, Displays(), display); }
        });
        SetStatus(L.Text("Набор создан") + " · " + name);
    }
}
