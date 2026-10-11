using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private const int GuidedTourVersion = 1;
    private sealed record TourStep(string Id, string Section, string[] Targets, string Title, string Description, bool RequiresGroup = false);
    private static readonly TourStep[] TourSteps =
    [
        new("welcome", "groups", ["desktop-monitor-previews", "group-toolbar"], "Ваш рабочий стол — под рукой", "Пройдём по настоящим кнопкам программы. Подсвеченные элементы можно нажимать; подсказки подождут, пока вы закончите."),
        new("sets", "groups", ["set-controls", "set-tab-scroll"], "Наборы для разных задач", "Переключайте наборы здесь. Для работы, игр и виджетов можно хранить отдельные группы. Кнопка рядом создаёт новый набор."),
        new("groups", "groups", ["new-fence"], "Создайте свою группу", "Нажмите «Группа», задайте название и сохраните. Уже созданную группу можно выбрать в списке ниже."),
        new("contents", "groups", ["inspector-add-content", "inspector-tab-content"], "Добавьте файлы или программы", "Здесь добавляются файлы и установленные программы. Название вкладки показывает её содержимое, а ПКМ открывает действия с вкладкой или ссылкой.", true),
        new("reveal", "groups", ["inspector-reveal"], "Как группа раскрывается", "Выберите приближение, наведение, щелчок, горячую клавишу или постоянное раскрытие. Ниже появляются параметры выбранного режима.", true),
        new("layout", "groups", ["inspector-edge-span", "inspector-edge-Top"], "Полоска или блок у края", "Выберите край экрана. Затем можно занять весь край или один из равных блоков — например, пятую часть ширины.", true),
        new("widgets", "widgets", ["add-widget", "widget-empty-add", "widget-library"], "Виджеты по одному", "Нажмите «Добавить виджет», выберите карточку и добавьте отдельную панель."),
        new("appearance", "appearance", ["appearance-group-choice", "interface-custom-accent"], "Оформление каждой группы", "Выберите группу перед настройкой профиля. Её фон, подсветка, размеры и поведение сохраняются отдельно. Свой цвет можно взять пипеткой с экрана."),
        new("quick-access", "groups", ["peek"], "Быстрый доступ к группам", "Эта кнопка раскрывает группы поверх окон. Сочетание для быстрого доступа можно изменить в настройках."),
        new("finish", "settings", ["settings-onboarding"], "Можно возвращаться к подсказкам", "Обучение запускается этой кнопкой. Если прервёте его, текущий шаг сохранится. Язык и режим настроек доступны здесь же.")
    ];
    private readonly CoachMarkOverlay _coachOverlay = new();
    private bool _guidedTourActive, _guideTargetQueued, _guideAnimateQueued, _guidePointerNavigation;
    private int _guidedTourIndex, _guideGeneration;
    private string _guideReturnSection = "groups";
    private FrameworkElement? _guideLastTarget;
    private string? _guideDisplayKey;

    private void InitializeGuidedTour(Grid frame)
    {
        Grid.SetRowSpan(_coachOverlay, 2); Panel.SetZIndex(_coachOverlay, 1000); frame.Children.Add(_coachOverlay);
        _coachOverlay.BackRequested += () => MoveGuidedTour(-1);
        _coachOverlay.NextRequested += AdvanceGuidedTour;
        _coachOverlay.SkipRequested += () => EndGuidedTour(completed: false);
        _coachOverlay.CloseRequested += () => EndGuidedTour(completed: false);
        PreviewMouseLeftButtonDown += (_, _) => _guidePointerNavigation = true;
        PreviewTouchDown += (_, _) => _guidePointerNavigation = true;
        PreviewKeyDown += (_, _) => _guidePointerNavigation = false;
        Closed += (_, _) => EndGuidedTour(completed: false, restoreSection: false);
    }

    private UIElement? OnboardingInvitation()
    {
        if (_guidedTourActive || _app.Workspace.Preferences.OnboardingDismissed) return null;
        var row = new Grid();
        row.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(Brand.Icon("play", Ui.FocusAccent, 18));
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        copy.Children.Add(Ui.Text(L.Text("Освойтесь за минуту"), 12, Ui.Ink, FontWeights.SemiBold));
        var detail = Ui.Text(L.Text("Подсказки покажут нужные кнопки прямо в интерфейсе."), 10.5, Ui.Muted);
        detail.TextWrapping = TextWrapping.NoWrap; detail.TextTrimming = TextTrimming.CharacterEllipsis;
        detail.ToolTip = detail.Text; copy.Children.Add(detail); Grid.SetColumn(copy, 1); row.Children.Add(copy);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var start = Ui.Button(L.Text("Показать"), StartOnboarding, id: "onboarding-start");
        start.Margin = new Thickness(0, 0, 4, 0); start.Padding = new Thickness(10, 5, 10, 5); start.MinHeight = 30;
        AutomationProperties.SetName(start, L.Text("Показать обучение")); actions.Children.Add(start);
        var dismiss = Ui.IconButton("close", DismissOnboardingInvitation, L.Text("Скрыть приглашение"), "onboarding-dismiss");
        dismiss.Margin = new Thickness(0); dismiss.Width = 28; dismiss.Height = 30; dismiss.MinWidth = 0; dismiss.MinHeight = 0;
        actions.Children.Add(dismiss); Grid.SetColumn(actions, 2); row.Children.Add(actions);
        var host = new Border { Child = row, Background = Ui.AccentWash, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 8, 8), Height = 64, Margin = new Thickness(0, 0, 0, 14) };
        AutomationProperties.SetAutomationId(host, "onboarding-invitation"); return host;
    }

    internal void StartOnboarding() => Ui.Guard(() =>
    {
        if (_guidedTourActive) { Activate(); QueueGuidedTourTarget(); return; }
        var prefs = _app.Workspace.Preferences;
        var resume = prefs.GuidedTourVersion == GuidedTourVersion && !prefs.GuidedTourCompleted
            ? Array.FindIndex(TourSteps, step => step.Id == prefs.GuidedTourStepId) : -1;
        _guidedTourIndex = Math.Max(0, resume); _guideReturnSection = CurrentSection;
        _guidedTourActive = true; _guideDisplayKey = null;
        SaveGuidedTourProgress(completed: false);
        var animate = _guidePointerNavigation; _guidePointerNavigation = false;
        ShowGuidedTourStep(animate);
    });

    private void RestartOnboarding()
    {
        if (_guidedTourActive) EndGuidedTour(completed: false, restoreSection: false);
        var prefs = _app.Workspace.Preferences;
        prefs.GuidedTourVersion = GuidedTourVersion; prefs.GuidedTourStepId = ""; prefs.GuidedTourCompleted = false;
        StartOnboarding();
    }

    private void SaveGuidedTourProgress(bool completed)
    {
        var prefs = _app.Workspace.Preferences;
        prefs.OnboardingDismissed = true; prefs.GuidedTourVersion = GuidedTourVersion;
        prefs.GuidedTourCompleted = completed;
        prefs.GuidedTourStepId = completed ? "" : TourSteps[_guidedTourIndex].Id;
        // Help progress is a preference, rather than a change to the desktop's Undo history.
        _app.SaveNow();
    }

    private void ShowGuidedTourStep(bool animate)
    {
        if (!_guidedTourActive) return;
        _guideDisplayKey = null; _guideLastTarget = null;
        var step = TourSteps[_guidedTourIndex];
        if (CurrentSection != step.Section) Navigate(step.Section);
        else if (step.Section == "groups") Refresh();
        if (step.RequiresGroup && SelectedFence == null && _app.Workspace.Fences.FirstOrDefault(fence => fence.PageId == _app.Workspace.ActivePageId) is { } group) SelectGroup(group.Id);
        QueueGuidedTourTarget(animate);
    }

    private void QueueGuidedTourTarget(bool animate = false)
    {
        if (!_guidedTourActive || Dispatcher.HasShutdownStarted) return;
        _guideAnimateQueued |= animate;
        if (_guideTargetQueued) return;
        _guideTargetQueued = true;
        var generation = _guideGeneration;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (generation != _guideGeneration) return;
            var animateStep = _guideAnimateQueued;
            _guideTargetQueued = false; _guideAnimateQueued = false;
            if (_guidedTourActive && IsVisible) UpdateGuidedTourTarget(animateStep);
        }));
    }

    private void UpdateGuidedTourTarget(bool animate)
    {
        var step = TourSteps[_guidedTourIndex];
        var missingGroup = step.RequiresGroup && SelectedFence == null;
        var targets = missingGroup ? new[] { "new-fence" } : step.Targets;
        var target = FindTourTarget(targets);
        var title = missingGroup ? L.Text("Сначала создайте группу") : L.Text(step.Title);
        var description = missingGroup ? L.Text("Создайте свою первую группу, чтобы попробовать этот шаг. Название и содержимое выбираете вы.") : L.Text(step.Description);
        if (step.Id == "quick-access") description += " " + L.Format("Сочетание сейчас: {0}.", _app.Workspace.Preferences.PeekHotkey);
        var next = missingGroup || step.Id == "groups" && !_app.Workspace.Fences.Any(fence => fence.PageId == _app.Workspace.ActivePageId)
            ? L.Text("Создать группу") : L.Text(_guidedTourIndex == TourSteps.Length - 1 ? "Готово" : "Далее");
        var key = step.Id + "|" + title + "|" + description + "|" + next;
        if (ReferenceEquals(target, _guideLastTarget) && key == _guideDisplayKey && _coachOverlay.IsShowing) return;
        _guideLastTarget = target; _guideDisplayKey = key;
        _coachOverlay.ShowStep(target, title, description, L.Format("Шаг {0} из {1}", _guidedTourIndex + 1, TourSteps.Length), _guidedTourIndex > 0, next, animate);
    }

    private FrameworkElement? FindTourTarget(string[] ids)
    {
        var controls = TourControls(Content as DependencyObject).ToArray();
        foreach (var id in ids)
            if (controls.FirstOrDefault(element => element.IsVisible && AutomationProperties.GetAutomationId(element) == id) is { } found) return found;
        return null;
    }

    private IEnumerable<FrameworkElement> TourControls(DependencyObject? parent)
    {
        if (parent == null || ReferenceEquals(parent, _coachOverlay)) yield break;
        if (parent is FrameworkElement element) yield return element;
        if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in TourControls(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }

    private void AdvanceGuidedTour() => Ui.Guard(() =>
    {
        if (!_guidedTourActive) return;
        var step = TourSteps[_guidedTourIndex];
        if ((step.RequiresGroup || step.Id == "groups") && !_app.Workspace.Fences.Any(fence => fence.PageId == _app.Workspace.ActivePageId))
        {
            Editors.Fence(_app);
            var group = _app.Workspace.Fences.FirstOrDefault(fence => fence.PageId == _app.Workspace.ActivePageId);
            if (group == null) { QueueGuidedTourTarget(); return; }
            SelectGroup(group.Id);
            if (step.RequiresGroup) { ShowGuidedTourStep(animate: _guidePointerNavigation); return; }
        }
        if (_guidedTourIndex == TourSteps.Length - 1) EndGuidedTour(completed: true);
        else MoveGuidedTour(1);
    });

    private void MoveGuidedTour(int direction)
    {
        if (!_guidedTourActive) return;
        _guidedTourIndex = Math.Clamp(_guidedTourIndex + direction, 0, TourSteps.Length - 1);
        _guideGeneration++; _guideTargetQueued = false; _guideAnimateQueued = false;
        SaveGuidedTourProgress(completed: false);
        var animate = _guidePointerNavigation; _guidePointerNavigation = false;
        ShowGuidedTourStep(animate);
    }

    private void EndGuidedTour(bool completed, bool restoreSection = true)
    {
        if (!_guidedTourActive) return;
        SaveGuidedTourProgress(completed);
        _guidedTourActive = false; _guideGeneration++; _guideTargetQueued = false; _guideAnimateQueued = false;
        _guideDisplayKey = null; _guideLastTarget = null; _coachOverlay.HideGuide();
        if (restoreSection && IsVisible && !Dispatcher.HasShutdownStarted) Navigate(_guideReturnSection);
    }

    private void DismissOnboardingInvitation()
    {
        _app.Workspace.Preferences.OnboardingDismissed = true; _app.SaveNow();
        if (IsLoaded && !Dispatcher.HasShutdownStarted) Refresh();
    }
}
