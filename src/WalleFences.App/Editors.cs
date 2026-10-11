using System.Globalization;
using FenceModel = WalleFences.Core.Fence;

namespace WalleFences.App;

internal static partial class Editors
{
    internal static string[] SortLabels => [L.Text("Вручную"), L.Text("По имени"), L.Text("По типу"), L.Text("Сначала новые"), L.Text("Сначала старые"), L.Text("По размеру")];
    internal static string[] RuleLabels => [L.Text("Расширение файла"), L.Text("Название по маске"), L.Text("Папки"), L.Text("Изменён за последние дни"), L.Text("Любой элемент")];
    internal static string[] ModeLabels => [L.Text("Обычный"), L.Text("Компактный"), L.Text("Минимальный")];
    internal static string[] LayerLabels => [L.Text("На рабочем столе"), L.Text("Поверх окон при раскрытии"), L.Text("Всегда поверх окон")];
    internal static string[] EdgeLabels => [L.Text("Без полоски у края"), L.Text("Сверху"), L.Text("Слева"), L.Text("Справа"), L.Text("Снизу")];
    internal static string[] TriggerLabels => [L.Text("При приближении курсора"), L.Text("При остановке курсора"), L.Text("Только по щелчку"), L.Text("При наведении на полоску"), L.Text("По двойному щелчку"), L.Text("По горячей клавише"), L.Text("Всегда раскрыта")];
    internal static string[] TriggerShortLabels => [L.Text("При приближении"), L.Text("С задержкой"), L.Text("По нажатию"), L.Text("При наведении"), L.Text("Двойной щелчок"), L.Text("Горячая клавиша"), L.Text("Всегда раскрыта")];
    internal static bool AutomaticReveal(RevealTrigger trigger) => trigger is RevealTrigger.Proximity or RevealTrigger.HoverPause or RevealTrigger.Hover;
    internal static string TriggerHint(RevealTrigger trigger) => L.Text(trigger switch
    {
        RevealTrigger.HoverPause => "Остановите курсор у полоски или на шапке: группа откроется после задержки.",
        RevealTrigger.Click => "Нажмите на полоску или свёрнутую шапку. После ухода курсора группа закроется.",
        RevealTrigger.Hover => "Наведите курсор прямо на полоску или шапку. Раскрытие начинается сразу.",
        RevealTrigger.DoubleClick => "Дважды нажмите на полоску или свёрнутую шапку. После ухода курсора группа закроется.",
        RevealTrigger.Hotkey => "Назначьте сочетание ниже: оно раскрывает и сворачивает группу. Уход курсора её не закрывает.",
        RevealTrigger.AlwaysOpen => "Содержимое остаётся раскрытым. Полоска и автоматическое сворачивание отключены.",
        _ => "Группа раскрывается при приближении к полоске или наведении на свёрнутую шапку."
    });
    internal static void ApplyRevealTrigger(FenceModel fence, RevealTrigger trigger)
    {
        fence.RevealTrigger = trigger;
        fence.HoverExpand = AutomaticReveal(trigger);
        fence.RolledUp = trigger != RevealTrigger.AlwaysOpen;
        if (trigger != RevealTrigger.AlwaysOpen) return;
        fence.CloseAfterLaunch = false;
        foreach (var tab in fence.Tabs) tab.Collapsed = false;
    }
    internal static string[] TabStyleLabels => [L.Text("Скруглённые вкладки"), L.Text("Плоские вкладки"), L.Text("Сегментированные вкладки")];
    internal static string[] TabVisibilityLabels => [L.Text("Только если их несколько"), L.Text("Всегда"), L.Text("Никогда")];
    internal static TabVisibility VisibleTabPolicy(FenceModel fence) => fence.TabStyle == TabStyle.MenuOnly ? TabVisibility.Hidden : fence.TabVisibility;
    internal static TabStyle VisibleTabStyle(FenceModel fence) => fence.TabStyle == TabStyle.MenuOnly ? TabStyle.Flat : fence.TabStyle;
    private static double Number(string text) => double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : throw new InvalidOperationException(L.Text("Некорректное число: ") + text);
    internal static void Tab(AppController app, FenceModel fence, FenceTab? existing = null, bool portal = false)
    {
        WorkspaceActions.RequireEditable(fence);
        var tab = existing ?? new FenceTab { Name = portal ? L.Text("Папка") : L.Text("Файлы") };
        var dialog = new FormWindow(existing == null ? L.Text("Новая вкладка") : L.Text("Настройки вкладки")) { Tag = fence.Id };
        System.Windows.Automation.AutomationProperties.SetAutomationId(dialog, "tab-editor");
        var name = Ui.Input(dialog.Body, L.Text("Название вкладки"), tab.Name);
        var color = Ui.Input(dialog.Body, L.Text("Цвет вкладки · HEX или пустое поле"), tab.Color ?? "");
        System.Windows.Automation.AutomationProperties.SetAutomationId(color, "tab-color-override"); Ui.ColorPicker(dialog.Body, color, L.Text("Выбрать цвет вкладки…"));
        var type = Ui.Combo(dialog.Body, L.Text("Содержимое"), [L.Text("Ссылки на файлы"), L.Text("Портал папки")], portal || !string.IsNullOrEmpty(tab.PortalPath) ? 1 : 0);
        var path = Ui.Input(dialog.Body, L.Text("Папка портала"), tab.PortalPath ?? "");
        var browse = Ui.Button(L.Text("Выбрать папку…"), () => { var selected = ContentPickers.SelectFolder(dialog, L.Text("Выберите папку для портала"), path.Text); if (selected != null) path.Text = selected; });
        browse.Margin = new Thickness(0, 0, 0, 16); dialog.Body.Children.Add(browse);
        void UpdatePath() { path.IsEnabled = browse.IsEnabled = type.SelectedIndex == 1; }
        type.SelectionChanged += (_, _) => UpdatePath(); UpdatePath();
        var hint = Ui.Text(L.Text("Портал показывает содержимое выбранной папки. Обычная вкладка хранит ссылки и не перемещает исходные файлы."), 12, Ui.Muted); hint.Margin = new Thickness(0, 0, 0, 8); dialog.Body.Children.Add(hint);
        dialog.AddSave(() =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException(L.Text("Введите название вкладки."));
            if (color.Text.Trim().Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(color.Text.Trim(), "^#[0-9A-Fa-f]{6}$")) throw new InvalidOperationException(L.Text("Укажите цвет в формате #8FDCC0."));
            var folder = type.SelectedIndex == 1 ? Path.GetFullPath(path.Text.Trim()) : null;
            if (folder != null && !Directory.Exists(folder)) throw new InvalidOperationException(L.Text("Выбранная папка недоступна."));
            if (existing != null && existing.Items.Count > 0 && folder != null && string.IsNullOrEmpty(existing.PortalPath) && !Ui.Confirm(L.Text("Ссылки этой вкладки будут скрыты на время работы в режиме портала. Продолжить?"))) return;
            app.Mutate(() => { tab.Name = name.Text.Trim(); tab.Color = color.Text.Trim().Length > 0 ? color.Text.Trim() : null; tab.PortalPath = folder; if (existing == null) fence.Tabs.Add(tab); fence.ActiveTabId = tab.Id; });
        });
        dialog.ShowDialog();
    }
    internal static void NewPortal(AppController app)
    {
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive && window.IsVisible) ?? app.Manager;
        if (owner == null) { app.ShowManager(); owner = app.Manager!; }
        var selected = ContentPickers.SelectFolder(owner, L.Text("Выберите папку для новой группы"), KnownFolders.Downloads());
        if (selected == null) return;
        var name = Path.GetFileName(selected.TrimEnd(Path.DirectorySeparatorChar));
        app.Mutate(() =>
        {
            var fence = new FenceModel { Name = name.Length > 0 ? name : L.Text("Портал"), PageId = app.Workspace.ActivePageId, X = 120, Y = 120, Sort = SortOrder.Name, Accent = "#E7BD82" };
            fence.Tabs[0].PortalPath = selected; fence.Tabs[0].Name = fence.Name; app.Workspace.Fences.Add(fence);
        });
    }
    internal static void AddFiles(FenceWindow window, AppController app)
    {
        WorkspaceActions.RequireEditable(window.Fence);
        var fenceId = window.Fence.Id; var tabId = window.Fence.ActiveTab.Id;
        string[]? paths;
        try { paths = ContentPickers.SelectFiles(window); }
        catch (Exception ex)
        {
            app.Log(ex); Ui.Error(L.Text("Не удалось выбрать файлы. Перетащите их в группу или добавьте приложение через «Добавить программы».")); return;
        }
        if (paths == null) return;
        var current = app.Windows.FirstOrDefault(candidate => candidate.Fence.Id == fenceId);
        if (current == null || current.Fence.ActiveTab.Id != tabId) throw new InvalidOperationException(L.Text("Вкладка была изменена. Повторите добавление."));
        current.AddPaths(paths);
    }
    internal static void AddPrograms(FenceWindow window, AppController app)
    {
        WorkspaceActions.RequireEditable(window.Fence);
        var fenceId = window.Fence.Id; var tabId = window.Fence.ActiveTab.Id;
        if (window.Fence.ActiveTab.PortalPath is { Length: > 0 }) throw new InvalidOperationException(L.Text("В портал нельзя добавить ссылку: выберите обычную вкладку."));
        var picker = new ProgramPickerWindow(app) { Owner = window, WindowStartupLocation = WindowStartupLocation.CenterOwner, Tag = fenceId };
        if (picker.ShowDialog() != true) return;
        try
        {
            var added = 0;
            app.Mutate(() =>
            {
                var fence = app.Workspace.Fences.SingleOrDefault(candidate => candidate.Id == fenceId) ?? throw new InvalidOperationException(L.Text("Группа удалена"));
                WorkspaceActions.RequireEditable(fence);
                var tab = fence.Tabs.SingleOrDefault(candidate => candidate.Id == tabId) ?? throw new InvalidOperationException(L.Text("Вкладка удалена"));
                if (tab.PortalPath is { Length: > 0 }) throw new InvalidOperationException(L.Text("Вкладка была изменена. Повторите добавление."));
                added = FileCatalog.ImportReferences(tab, picker.References).Added;
            });
            app.SetStatus(L.Format("Добавлено программ: {0}", added));
        }
        catch (Exception ex) { app.Log(ex); Ui.Error(L.Text("Не удалось добавить программы.") + " " + ex.Message); }
    }
    internal static void DeleteFence(AppController app, FenceModel fence)
    {
        WorkspaceActions.RequireEditable(fence);
        if (!Ui.Confirm(L.Format("Удалить группу «{0}» и связанные правила? Файлы останутся на диске.", fence.Name))) return;
        app.Mutate(() => { app.Workspace.Fences.Remove(fence); app.Workspace.Rules.RemoveAll(r => r.FenceId == fence.Id); });
    }
    internal static void Rule(AppController app, SortingRule? existing = null)
    {
        var targets = app.Workspace.Fences.SelectMany(f => f.Tabs.Where(t => string.IsNullOrEmpty(t.PortalPath)).Select(t => (Fence: f, Tab: t))).ToList();
        if (targets.Count == 0) { Ui.Error(L.Text("Сначала создайте обычную группу для ссылок.")); return; }
        var draft = existing ?? new SortingRule { FenceId = targets[0].Fence.Id, TabId = targets[0].Tab.Id };
        var dialog = new FormWindow(existing == null ? L.Text("Новое правило") : L.Text("Настройки правила"));
        var name = Ui.Input(dialog.Body, L.Text("Название"), draft.Name);
        var kind = Ui.Combo(dialog.Body, L.Text("Условие"), RuleLabels, (int)draft.Kind);
        var pattern = Ui.Input(dialog.Body, L.Text("Значение · .pdf .docx / *отчёт* / число дней"), draft.Pattern);
        void UpdatePattern() { pattern.IsEnabled = kind.SelectedIndex is not (2 or 4); }
        kind.SelectionChanged += (_, _) => UpdatePattern(); UpdatePattern();
        var destination = Ui.Combo(dialog.Body, L.Text("Группа / вкладка"), targets.Select(t => t.Fence.Name + " / " + t.Tab.Name), Math.Max(0, targets.FindIndex(t => t.Tab.Id == draft.TabId)));
        var enabled = Ui.Check(dialog.Body, L.Text("Правило включено"), draft.Enabled);
        dialog.Body.Children.Add(Ui.Text(L.Text("Правила применяются к новым элементам рабочего стола сверху вниз. Уже распределённые ссылки сохраняют назначенную группу."), 12, Ui.Muted));
        dialog.Body.Children.Add(Ui.Button(L.Text("Предпросмотр"), () =>
        {
            var preview = new SortingRule { Id = draft.Id, Name = name.Text.Trim(), Kind = (RuleKind)kind.SelectedIndex, Pattern = pattern.Text.Trim(), Enabled = enabled.IsChecked == true, DesktopCategory = null, FenceId = targets[destination.SelectedIndex].Fence.Id, TabId = targets[destination.SelectedIndex].Tab.Id };
            SortingPreviewWindow.Open(app, dialog, preview);
        }, id: "rule-preview"));
        dialog.AddSave(() =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException(L.Text("Введите название правила."));
            if (kind.SelectedIndex is 0 or 1 && string.IsNullOrWhiteSpace(pattern.Text)) throw new InvalidOperationException(L.Text("Введите расширения или маску имени."));
            if (kind.SelectedIndex == 3 && (!int.TryParse(pattern.Text, out var days) || days < 0 || days > 36500)) throw new InvalidOperationException(L.Text("Введите число дней от 0 до 36500."));
            app.Mutate(() => { draft.Name = name.Text.Trim(); draft.Kind = (RuleKind)kind.SelectedIndex; draft.Pattern = pattern.Text.Trim(); draft.Enabled = enabled.IsChecked == true; draft.DesktopCategory = null; draft.FenceId = targets[destination.SelectedIndex].Fence.Id; draft.TabId = targets[destination.SelectedIndex].Tab.Id; if (existing == null) app.Workspace.Rules.Add(draft); }, false);
        }); dialog.ShowDialog();
    }
}
