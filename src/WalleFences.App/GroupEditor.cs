using System.Globalization;
using FenceModel = WalleFences.Core.Fence;

namespace WalleFences.App;

internal static partial class Editors
{
    internal static void Fence(AppController app, FenceModel? existing = null)
    {
        var draft = existing ?? new FenceModel { PageId = app.Workspace.ActivePageId, X = 100, Y = 100 };
        var dialog = new FormWindow(existing == null ? L.Text("Новая группа") : L.Text("Настройки группы"), 680, resizable: true) { Tag = draft.Id };
        if (app.Manager is { IsVisible: true } manager) { dialog.Owner = manager; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        System.Windows.Automation.AutomationProperties.SetAutomationId(dialog, "fence-editor");
        var sections = new[] { new StackPanel(), new StackPanel(), new StackPanel(), new StackPanel() };
        var general = sections[0]; var tabs = sections[1]; var appearance = sections[2]; var reveal = sections[3];
        var advancedControls = new List<UIElement>();
        void AdvancedField(Panel parent, FrameworkElement field)
        {
            advancedControls.Add(field);
            var index = parent.Children.IndexOf(field);
            if (index > 0 && parent.Children[index - 1] is TextBlock label) advancedControls.Add(label);
        }
        foreach (var section in sections) dialog.Body.Children.Add(section);
        var navigation = new Grid(); var buttons = new List<Button>(); var currentSection = -1;
        var motion = new SurfaceMotion(dialog.Body); dialog.Closed += (_, _) => motion.Dispose();
        void SelectSection(int index, bool animate = false)
        {
            if (index == currentSection) return;
            currentSection = index;
            for (var i = 0; i < sections.Length; i++) { sections[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed; buttons[i].Tag = i == index ? "selected" : null; }
            dialog.ScrollBodyToTop(); motion.Enter(animate, offsetY: 4);
        }
        var navigationLabels = new[] { L.Text("Основное"), L.Text("Вкладки"), L.Text("Оформление"), L.Text("Раскрытие") };
        for (var i = 0; i < sections.Length; i++)
        {
            var index = i; var pointer = false;
            navigation.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var button = Ui.Segment(navigationLabels[i], () => { SelectSection(index, pointer); pointer = false; }, id: "editor-section-" + i);
            button.HorizontalContentAlignment = HorizontalAlignment.Center; button.Height = 34; button.Padding = new Thickness(5, 5, 5, 5); button.Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0);
            button.PreviewMouseLeftButtonDown += (_, _) => pointer = true; button.PreviewKeyDown += (_, _) => pointer = false; button.LostMouseCapture += (_, _) => dialog.Dispatcher.BeginInvoke(() => pointer = false, DispatcherPriority.ContextIdle);
            System.Windows.Automation.AutomationProperties.SetName(button, navigationLabels[i]); Grid.SetColumn(button, i); navigation.Children.Add(button); buttons.Add(button);
        }
        var navigationBody = new StackPanel();
        var settingsRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var settingsModes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(settingsModes, Dock.Right); settingsRow.Children.Add(settingsModes);
        settingsRow.Children.Add(Ui.Text(L.Text("Режим настроек"), 12, Ui.Muted));
        var modeHint = Ui.Text("", 11, Ui.Muted); modeHint.Margin = new Thickness(0, 8, 0, 0);
        Action<bool>? updateSettingsMode = null;
        void ChooseSettingsMode(bool advanced)
        {
            if (app.Workspace.Preferences.AdvancedSettings != advanced)
                app.Mutate(() => app.Workspace.Preferences.AdvancedSettings = advanced, rebuild: false);
            updateSettingsMode?.Invoke(advanced);
        }
        var simpleSettings = Ui.Segment(L.Text("Простые"), () => ChooseSettingsMode(false), id: "editor-settings-simple");
        var advancedSettings = Ui.Segment(L.Text("Продвинутые"), () => ChooseSettingsMode(true), id: "editor-settings-advanced");
        foreach (var button in new[] { simpleSettings, advancedSettings }) { button.Height = 30; button.Margin = new Thickness(3, 0, 0, 0); button.Padding = new Thickness(10, 4, 10, 4); settingsModes.Children.Add(button); }
        navigationBody.Children.Add(settingsRow); navigationBody.Children.Add(navigation); navigationBody.Children.Add(modeHint);
        dialog.SetNavigation(navigationBody); SelectSection(0);
        var name = Ui.Input(general, L.Text("Название"), draft.Name);
        var page = Ui.Combo(general, L.Text("Набор"), app.Workspace.Pages.Select(p => p.Name), app.Workspace.Pages.FindIndex(p => p.Id == draft.PageId));
        var groupIcon = new GroupIconControl(draft); general.Children.Add(groupIcon);
        var colors = new GroupColorsControl(draft.Color, draft.Accent, draft.Name); colors.SetPreviewOpacity(draft.Opacity); System.Windows.Automation.AutomationProperties.SetAutomationId(colors, "editor-group-colors"); appearance.Children.Add(colors);
        colors.SetPreviewIcon(GroupIcons.Symbol(draft, groupIcon.SelectedIcon));
        groupIcon.SelectionChanged += () => colors.SetPreviewIcon(GroupIcons.Symbol(draft, groupIcon.SelectedIcon));
        name.TextChanged += (_, _) => colors.SetPreviewName(name.Text);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new()); columns.ColumnDefinitions.Add(new());
        var left = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) }; Grid.SetColumn(right, 1); columns.Children.Add(left); columns.Children.Add(right);
        var openingSize = Ui.Combo(general, L.Text("Размер раскрытия"), [L.Text("Свой размер"), L.Text("Маленький · 300 × 240"), L.Text("Средний · 440 × 380"), L.Text("Широкий · 720 × 380"), L.Text("Высокий · 380 × 640"), L.Text("Боковая панель · 320 × 760")], (int)draft.OpeningSize);
        general.Children.Add(columns);
        var width = Ui.Input(left, L.Text("Ширина"), draft.Width.ToString("0", CultureInfo.InvariantCulture)); var height = Ui.Input(right, L.Text("Высота"), draft.Height.ToString("0", CultureInfo.InvariantCulture));
        EdgeSpanControl? span = null;
        var sizing = false;
        openingSize.SelectionChanged += (_, _) =>
        {
            if (openingSize.SelectedIndex <= 0) return;
            span?.UseCustom();
            var dimensions = OpeningSizes.Dimensions((OpeningSize)openingSize.SelectedIndex); sizing = true;
            width.Text = dimensions.Width.ToString("0", CultureInfo.InvariantCulture); height.Text = dimensions.Height.ToString("0", CultureInfo.InvariantCulture); sizing = false;
        };
        width.TextChanged += (_, _) => { if (!sizing) openingSize.SelectedIndex = 0; };
        height.TextChanged += (_, _) => { if (!sizing) openingSize.SelectedIndex = 0; };
        var x = Ui.Input(left, L.Text("Позиция X"), draft.X.ToString("0", CultureInfo.InvariantCulture)); var y = Ui.Input(right, L.Text("Позиция Y"), draft.Y.ToString("0", CultureInfo.InvariantCulture));
        AdvancedField(left, x); AdvancedField(right, y);
        var monitors = AppController.Displays();
        var monitor = Ui.Combo(general, L.Text("Перенести на монитор"), new[] { L.Text("Оставить текущую позицию") }.Concat(monitors.Select((m, i) => L.Format("Монитор {0} · {1} · {2:0}%", i + 1, m.Name, m.Scale / AppController.GlobalScale * 100))));
        general.Children.Add(Ui.Text(L.Text("Потяните край или угол, чтобы изменить размер"), 12, Ui.Muted));
        var opacity = Ui.Slider(appearance, L.Text("Непрозрачность, %"), 15, 100, draft.Opacity * 100);
        opacity.ValueChanged += (_, _) => colors.SetPreviewOpacity(opacity.Value / 100);
        var iconSize = Ui.Slider(appearance, L.Text("Размер значков"), 24, 96, draft.IconSize);
        var sort = Ui.Combo(general, L.Text("Сортировка"), SortLabels, (int)draft.Sort);
        AdvancedField(general, sort);
        var mode = Ui.Combo(general, L.Text("Режим группы"), ModeLabels, (int)draft.DisplayMode);
        var layer = Ui.Combo(general, L.Text("Положение поверх окон"), LayerLabels, (int)draft.Layer);
        var look = appearance;
        var tabVisibility = Ui.Combo(tabs, L.Text("Показывать вкладки"), TabVisibilityLabels, (int)VisibleTabPolicy(draft));
        System.Windows.Automation.AutomationProperties.SetAutomationId(tabVisibility, "editor-tab-visibility");
        tabs.Children.Add(Ui.Text(L.Text("Скрытые вкладки доступны в меню группы и через Ctrl+Tab."), 12, Ui.Muted));
        var tabStyle = Ui.Combo(tabs, L.Text("Стиль вкладок"), TabStyleLabels, (int)VisibleTabStyle(draft));
        AdvancedField(tabs, tabStyle);
        System.Windows.Automation.AutomationProperties.SetAutomationId(tabStyle, "editor-tab-style");
        tabStyle.IsEnabled = tabVisibility.SelectedIndex != (int)TabVisibility.Hidden;
        tabVisibility.SelectionChanged += (_, _) => tabStyle.IsEnabled = tabVisibility.SelectedIndex != (int)TabVisibility.Hidden;
        var tabSettings = new GroupTabSettings(draft) { Margin = new Thickness(0, 12, 0, 0) }; tabs.Children.Add(tabSettings);
        colors.ColorsChanged += (_, _) => { if (colors.IsValid) tabSettings.SetGroupAccent(colors.AccentHex); };
        var iconSpacing = Ui.Slider(look, L.Text("Расстояние между значками, px"), 0, 24, draft.IconSpacing);
        AdvancedField(look, iconSpacing);
        var labelFont = Ui.Slider(look, L.Text("Размер подписей, px"), 9, 18, draft.LabelFontSize);
        AdvancedField(look, labelFont);
        var fileView = Ui.Combo(look, L.Text("Расположение файлов"), [L.Text("Сетка значков"), L.Text("Вертикальный список")], (int)draft.FileView);
        AdvancedField(look, fileView);
        var surfaceStyle = Ui.Combo(look, L.Text("Стиль поверхности"), [L.Text("Сплошной"), L.Text("Контурный"), L.Text("Мягкий")], (int)draft.SurfaceStyle);
        AdvancedField(look, surfaceStyle);
        surfaceStyle.SelectionChanged += (_, _) => colors.SetPreviewSurface((SurfaceStyle)surfaceStyle.SelectedIndex);
        colors.SetPreviewSurface(draft.SurfaceStyle);
        var radius = Ui.Slider(look, L.Text("Скругление углов, px"), 0, 24, draft.CornerRadius);
        AdvancedField(look, radius);
        var tint = Ui.Check(appearance, L.Text("Тонировать значки цветом акцента"), draft.TintIcons);
        advancedControls.Add(tint);
        var labels = Ui.Check(appearance, L.Text("Показывать названия файлов"), draft.ShowLabels);
        var rolled = Ui.Check(reveal, L.Text("Сворачивать содержимое до заголовка группы"), draft.ContentCollapsed);
        var hover = Ui.Check(reveal, L.Text("Разворачивать свёрнутую группу при наведении"), draft.HoverExpand);
        var locked = Ui.Check(general, L.Text("Закрепить положение и размер"), draft.Locked);
        var keepVisible = Ui.Check(general, L.Text("Оставлять группу видимой при быстром скрытии"), draft.KeepVisibleOnHide);
        advancedControls.Add(keepVisible);
        var contentLocked = Ui.Check(tabs, L.Text("Защитить вкладки и ссылки от изменений"), draft.ContentLocked);
        advancedControls.Add(contentLocked);
        var revealPanel = reveal;
        var edge = Ui.Combo(revealPanel, L.Text("Край экрана"), EdgeLabels, (int)draft.Edge);
        Action? refreshRevealControls = null;
        void UpdateEdgeControls()
        {
            var docked = edge.SelectedIndex > 0;
            layer.IsEnabled = !docked;
            if (docked) layer.SelectedIndex = (int)FenceLayer.AlwaysOnTop;
            refreshRevealControls?.Invoke();
        }
        edge.SelectionChanged += (_, _) => UpdateEdgeControls(); UpdateEdgeControls();
        var edgeHint = Ui.Text(L.Text("Полоски у края всегда поверх окон."), 12, Ui.Muted);
        revealPanel.Children.Add(edgeHint);
        span = new EdgeSpanControl(draft);
        revealPanel.Children.Add(span);
        var thickness = Ui.Slider(revealPanel, L.Text("Толщина полоски, px"), 2, 12, draft.EdgeThickness);
        AdvancedField(revealPanel, thickness);
        var stripSizes = new[] { 2d, 3d, 6d, 10d };
        var stripPresetIndex = Array.IndexOf(stripSizes, draft.EdgeThickness) + 1;
        var stripPreset = Ui.Combo(revealPanel, L.Text("Толщина полоски, px"), new[] { L.Text("Своя толщина") }.Concat(stripSizes.Select(value => value.ToString("0", CultureInfo.InvariantCulture) + " px")), stripPresetIndex);
        var stripPresetLabel = revealPanel.Children[revealPanel.Children.IndexOf(stripPreset) - 1];
        var syncingStripSize = false;
        stripPreset.SelectionChanged += (_, _) =>
        {
            if (syncingStripSize || stripPreset.SelectedIndex <= 0) return;
            syncingStripSize = true; thickness.Value = stripSizes[stripPreset.SelectedIndex - 1]; syncingStripSize = false;
        };
        thickness.ValueChanged += (_, _) =>
        {
            if (syncingStripSize) return;
            syncingStripSize = true; stripPreset.SelectedIndex = Array.IndexOf(stripSizes, thickness.Value) + 1; syncingStripSize = false;
        };
        var length = Ui.Slider(revealPanel, L.Text("Длина полоски, px"), 32, 2000, draft.EdgeLength);
        AdvancedField(revealPanel, length);
        var offset = Ui.Slider(revealPanel, L.Text("Отступ вдоль края, px"), 0, 8000, draft.EdgeOffset);
        AdvancedField(revealPanel, offset);
        var trigger = Ui.Combo(revealPanel, L.Text("Способ раскрытия"), TriggerLabels, (int)draft.RevealTrigger);
        System.Windows.Automation.AutomationProperties.SetAutomationId(trigger, "editor-reveal-trigger");
        var triggerHint = Ui.Text("", 12, Ui.Muted); triggerHint.Margin = new Thickness(0, 0, 0, 10);
        revealPanel.Children.Add(triggerHint);
        var hotkey = Ui.HotkeyInput(revealPanel, L.Text("Сочетание для этой группы"), draft.Hotkey, app);
        AdvancedField(revealPanel, hotkey);
        System.Windows.Automation.AutomationProperties.SetAutomationId(hotkey, "editor-group-hotkey");
        var proximity = Ui.Slider(revealPanel, L.Text("Зона приближения, px"), 0, 80, draft.ActivationDistance);
        AdvancedField(revealPanel, proximity);
        var openDelay = Ui.Slider(revealPanel, L.Text("Задержка раскрытия, мс"), 0, 2000, draft.OpenDelayMs);
        AdvancedField(revealPanel, openDelay);
        var closeDelay = Ui.Slider(revealPanel, L.Text("Задержка закрытия, мс"), 100, 5000, draft.CloseDelayMs);
        AdvancedField(revealPanel, closeDelay);
        var closeAfterLaunch = Ui.Check(revealPanel, L.Text("Сворачивать после открытия файла"), draft.CloseAfterLaunch);
        advancedControls.Add(closeAfterLaunch);
        var portalExplorer = Ui.Check(look, L.Text("Открывать папки портала в Проводнике"), draft.PortalFoldersInExplorer);
        advancedControls.Add(portalExplorer);
        var stripHint = Ui.Text(L.Text("В закрытом виде видна только полоска. Группа раскрывается внутрь рабочей области и остаётся открытой, пока курсор внутри или используется меню."), 12, Ui.Muted);
        revealPanel.Children.Add(stripHint);
        void FieldVisibility(Panel parent, FrameworkElement field, bool visible)
        {
            field.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            var index = parent.Children.IndexOf(field);
            if (index > 0 && parent.Children[index - 1] is TextBlock label) label.Visibility = field.Visibility;
        }
        refreshRevealControls = () =>
        {
            var selectedTrigger = (RevealTrigger)trigger.SelectedIndex;
            var advanced = app.Workspace.Preferences.AdvancedSettings;
            var docked = edge.SelectedIndex > 0;
            var alwaysOpen = selectedTrigger == RevealTrigger.AlwaysOpen;
            var automatic = selectedTrigger is RevealTrigger.Proximity or RevealTrigger.HoverPause or RevealTrigger.Hover;
            var proximityMode = selectedTrigger is RevealTrigger.Proximity or RevealTrigger.HoverPause;
            span.UpdateEdge((DockEdge)edge.SelectedIndex);
            var automaticSpan = docked && span.Mode != EdgeSpanMode.Custom;
            var horizontalSpan = span.Edge is DockEdge.Top or DockEdge.Bottom;
            width.IsEnabled = !automaticSpan || !horizontalSpan;
            height.IsEnabled = !automaticSpan || horizontalSpan;
            width.ToolTip = width.IsEnabled ? null : L.Text("Ширина определяется выбранным краем и блоком.");
            height.ToolTip = height.IsEnabled ? null : L.Text("Высота определяется выбранным краем и блоком.");
            if (automaticSpan)
            {
                openingSize.SelectedIndex = (int)OpeningSize.Custom;
                var target = monitor.SelectedIndex > 0 ? monitors[monitor.SelectedIndex - 1] : DisplayLayout.Select(draft, monitors);
                var candidate = new FenceModel { Edge = span.Edge, EdgeSpanMode = span.Mode, EdgeBlockCount = span.BlockCount, EdgeBlockIndex = span.BlockIndex, Width = draft.Width, Height = draft.Height };
                var bounds = EdgeLayout.Bounds(candidate, target, true);
                sizing = true;
                if (horizontalSpan) width.Text = (bounds.Width / target.Scale).ToString("0.#", CultureInfo.InvariantCulture);
                else height.Text = (bounds.Height / target.Scale).ToString("0.#", CultureInfo.InvariantCulture);
                sizing = false;
            }
            triggerHint.Text = TriggerHint(selectedTrigger);
            rolled.IsEnabled = !docked && !alwaysOpen;
            rolled.Visibility = alwaysOpen ? Visibility.Collapsed : Visibility.Visible;
            hover.IsEnabled = !docked && automatic;
            hover.Visibility = advanced && !docked && automatic ? Visibility.Visible : Visibility.Collapsed;
            if (!automatic) hover.IsChecked = false;
            if (alwaysOpen) { rolled.IsChecked = false; closeAfterLaunch.IsChecked = false; }
            FieldVisibility(revealPanel, hotkey, advanced || selectedTrigger == RevealTrigger.Hotkey);
            FieldVisibility(revealPanel, thickness, advanced && docked && !alwaysOpen);
            stripPreset.Visibility = stripPresetLabel.Visibility = !advanced && docked && !alwaysOpen ? Visibility.Visible : Visibility.Collapsed;
            FieldVisibility(revealPanel, length, advanced && docked && !alwaysOpen && span.Mode == EdgeSpanMode.Custom);
            FieldVisibility(revealPanel, offset, advanced && docked && !alwaysOpen && span.Mode == EdgeSpanMode.Custom);
            FieldVisibility(revealPanel, proximity, advanced && docked && proximityMode);
            FieldVisibility(revealPanel, openDelay, selectedTrigger == RevealTrigger.HoverPause || advanced && selectedTrigger == RevealTrigger.Proximity);
            FieldVisibility(revealPanel, closeDelay, advanced && (automatic || selectedTrigger is RevealTrigger.Click or RevealTrigger.DoubleClick));
            closeAfterLaunch.Visibility = advanced && !alwaysOpen ? Visibility.Visible : Visibility.Collapsed;
            edgeHint.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
            stripHint.Visibility = docked && !alwaysOpen ? Visibility.Visible : Visibility.Collapsed;
        };
        span.Changed += (_, _) => refreshRevealControls?.Invoke();
        monitor.SelectionChanged += (_, _) => refreshRevealControls?.Invoke();
        trigger.SelectionChanged += (_, _) =>
        {
            var selectedTrigger = (RevealTrigger)trigger.SelectedIndex;
            hover.IsChecked = selectedTrigger is RevealTrigger.Proximity or RevealTrigger.HoverPause or RevealTrigger.Hover;
            rolled.IsChecked = selectedTrigger != RevealTrigger.AlwaysOpen;
            refreshRevealControls?.Invoke();
        };
        updateSettingsMode = advanced =>
        {
            foreach (var control in advancedControls) control.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
            refreshRevealControls?.Invoke();
            tabSettings.SetAdvancedMode(advanced);
            simpleSettings.Tag = advanced ? null : "selected"; advancedSettings.Tag = advanced ? "selected" : null;
            modeHint.Text = L.Text(advanced ? "Больше настроек: координаты, оформление, задержки и защита." : "Основные настройки группы. Остальные параметры сохраняются.");
        };
        updateSettingsMode(app.Workspace.Preferences.AdvancedSettings);
        var save = dialog.AddSave(() =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { SelectSection(0); name.Focus(); throw new InvalidOperationException(L.Text("Введите название группы.")); }
            if (!colors.IsValid) { SelectSection(2); throw new InvalidOperationException(colors.ValidationMessage); }
            double newWidth, newHeight, newX, newY;
            try { newWidth = Number(width.Text); newHeight = Number(height.Text); }
            catch { SelectSection(0); throw; }
            try { newX = Number(x.Text); newY = Number(y.Text); }
            catch { ChooseSettingsMode(true); SelectSection(0); throw; }
            try { tabSettings.Validate(existing == null ? draft : app.Workspace.Fences.SingleOrDefault(f => f.Id == existing.Id)); }
            catch (OperationCanceledException) { SelectSection(1); throw; }
            catch { ChooseSettingsMode(true); SelectSection(1); throw; }
            if (trigger.SelectedIndex == (int)RevealTrigger.Hotkey && string.IsNullOrWhiteSpace(hotkey.Text))
            {
                SelectSection(3); hotkey.Focus(); throw new InvalidOperationException(L.Text("Назначьте сочетание клавиш для раскрытия группы."));
            }
            if (hotkey.Text.Length > 0 && !HotkeyGesture.TryParse(hotkey.Text, out _)) { ChooseSettingsMode(true); SelectSection(3); hotkey.Focus(); throw new InvalidOperationException(L.Text("Некорректное сочетание клавиш.")); }
            app.Mutate(() =>
            {
                if (existing != null) draft = app.Workspace.Fences.SingleOrDefault(fence => fence.Id == existing.Id) ?? throw new InvalidOperationException(L.Text("Группа удалена"));
                tabSettings.Apply(draft, app.Workspace);
                draft.Name = name.Text.Trim(); draft.Icon = groupIcon.SelectedIcon; draft.PageId = app.Workspace.Pages[page.SelectedIndex].Id; draft.Color = colors.BackgroundHex; draft.Accent = colors.AccentHex;
                draft.Width = newWidth; draft.Height = newHeight; draft.X = newX; draft.Y = newY;
                draft.Opacity = opacity.Value / 100; draft.IconSize = (int)iconSize.Value; draft.Sort = (SortOrder)sort.SelectedIndex;
                draft.DisplayMode = (FenceDisplayMode)mode.SelectedIndex; draft.Layer = (FenceLayer)layer.SelectedIndex;
                draft.TabVisibility = (TabVisibility)tabVisibility.SelectedIndex; draft.TabStyle = (TabStyle)tabStyle.SelectedIndex; draft.IconSpacing = (int)iconSpacing.Value; draft.LabelFontSize = (int)labelFont.Value;
                draft.OpeningSize = (OpeningSize)openingSize.SelectedIndex; draft.FileView = (FileView)fileView.SelectedIndex; draft.SurfaceStyle = (SurfaceStyle)surfaceStyle.SelectedIndex; draft.CornerRadius = radius.Value; draft.Hotkey = hotkey.Text;
                draft.Edge = (DockEdge)edge.SelectedIndex; draft.EdgeThickness = thickness.Value; draft.EdgeLength = length.Value; draft.EdgeOffset = offset.Value;
                span.Apply(draft);
                draft.RevealTrigger = (RevealTrigger)trigger.SelectedIndex; draft.ActivationDistance = proximity.Value; draft.OpenDelayMs = (int)openDelay.Value; draft.CloseDelayMs = (int)closeDelay.Value; draft.CloseAfterLaunch = closeAfterLaunch.IsChecked == true;
                draft.PortalFoldersInExplorer = portalExplorer.IsChecked == true;
                draft.KeepVisibleOnHide = keepVisible.IsChecked == true; draft.ContentLocked = contentLocked.IsChecked == true;
                draft.TintIcons = tint.IsChecked == true; draft.ShowLabels = labels.IsChecked == true; draft.RolledUp = rolled.IsChecked == true; draft.HoverExpand = hover.IsChecked == true; draft.Locked = locked.IsChecked == true;
                if (draft.RevealTrigger == RevealTrigger.AlwaysOpen) ApplyRevealTrigger(draft, RevealTrigger.AlwaysOpen);
                if (!draft.RolledUp) draft.ActiveTab.Collapsed = false;
                if (monitor.SelectedIndex > 0) DisplayLayout.MoveToDisplay(draft, monitors[monitor.SelectedIndex - 1], AppController.Displays());
                if (existing == null) app.Workspace.Fences.Add(draft);
            });
        });
        save.IsEnabled = colors.IsValid; colors.ColorsChanged += (_, _) => save.IsEnabled = colors.IsValid;
        dialog.ShowDialog();
    }
}
