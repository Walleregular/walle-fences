using System.Windows.Automation;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private string? _appearanceFenceId;

    private Fence? IndividualGroupProfile()
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(L.Text("Профиль группы"), 18, Ui.Ink, FontWeights.SemiBold));
        var fences = _app.Workspace.Fences.ToArray();
        if (fences.Length == 0)
        {
            var empty = Ui.Text(L.Text("Добавьте группу, чтобы настроить её профиль."), 12, Ui.Muted);
            empty.Margin = new Thickness(0, 8, 0, 10); body.Children.Add(empty);
            body.Children.Add(LabelAction("Добавить группу", "plus", () => Editors.Fence(_app), id: "appearance-new-group"));
            _page.Children.Add(Ui.Card(body)); return null;
        }
        var fence = fences.FirstOrDefault(f => f.Id == _appearanceFenceId)
            ?? fences.FirstOrDefault(f => f.Id == _selectedFenceId)
            ?? fences.FirstOrDefault(f => f.PageId == _app.Workspace.ActivePageId) ?? fences[0];
        _appearanceFenceId = fence.Id;
        string ChoiceLabel(Fence item)
        {
            var page = _app.Workspace.Pages.FirstOrDefault(p => p.Id == item.PageId);
            return item.Name + " · " + (page?.Name ?? L.Text("Набор"));
        }
        var selected = Ui.Combo(body, L.Text("Настраиваемая группа"), fences.Select(ChoiceLabel), Array.FindIndex(fences, f => f.Id == fence.Id));
        AutomationProperties.SetAutomationId(selected, "appearance-group-choice");
        selected.SelectionChanged += (_, _) =>
        {
            if (selected.SelectedIndex < 0 || selected.SelectedIndex >= fences.Length) return;
            _appearanceFenceId = fences[selected.SelectedIndex].Id; Refresh();
        };
        var hint = Ui.Text(L.Text("Профиль, размеры и поведение сохраняются только для выбранной группы."), 12, Ui.Muted);
        hint.Margin = new Thickness(0, 0, 0, 8); body.Children.Add(hint);
        var colors = new GroupColorsControl(fence.Color, fence.Accent, fence.Name);
        colors.SetPreviewIcon(GroupIcons.Symbol(fence));
        colors.SetPreviewOpacity(fence.Opacity); colors.SetPreviewSurface(fence.SurfaceStyle);
        AutomationProperties.SetAutomationId(colors, "appearance-group-colors"); body.Children.Add(colors);
        var surface = Ui.Combo(body, L.Text("Стиль поверхности"), [L.Text("Сплошной"), L.Text("Контурный"), L.Text("Мягкий")], (int)fence.SurfaceStyle);
        surface.SelectionChanged += (_, _) => { if (surface.SelectedIndex >= 0) colors.SetPreviewSurface((SurfaceStyle)surface.SelectedIndex); };
        var radius = Ui.Slider(body, L.Text("Скругление углов, px"), 0, 24, fence.CornerRadius);
        var view = Ui.Combo(body, L.Text("Расположение файлов"), [L.Text("Сетка значков"), L.Text("Вертикальный список")], (int)fence.FileView);
        var size = Ui.Combo(body, L.Text("Размер раскрытия"), [L.Text("Сохранить текущие размеры"), L.Text("Маленький · 300 × 240"), L.Text("Средний · 440 × 380"), L.Text("Широкий · 720 × 380"), L.Text("Высокий · 380 × 640"), L.Text("Боковая панель · 320 × 760")], (int)fence.OpeningSize);
        body.Children.Add(Ui.Button(L.Text("Применить профиль группы"), () =>
        {
            if (!colors.IsValid) throw new InvalidOperationException(colors.ValidationMessage);
            ApplyIndividualProfile(fence.Id, current =>
            {
                current.Color = colors.BackgroundHex; current.Accent = colors.AccentHex;
                current.SurfaceStyle = (SurfaceStyle)surface.SelectedIndex;
                current.CornerRadius = radius.Value; current.FileView = (FileView)view.SelectedIndex;
                if (size.SelectedIndex != (int)current.OpeningSize)
                    OpeningSizes.Apply(current, (OpeningSize)size.SelectedIndex, DisplayLayout.Select(current, AppController.Displays()));
            });
        }, true, "apply-style-profile"));
        _page.Children.Add(Ui.Card(body)); return fence;
    }

    private void ApplyIndividualProfile(string fenceId, Action<Fence> change)
    {
        _app.Mutate(() =>
        {
            var current = _app.Workspace.Fences.FirstOrDefault(f => f.Id == fenceId)
                ?? throw new InvalidOperationException(L.Text("Группа больше не существует."));
            change(current);
        });
        _app.SaveNow(); _app.SetStatus(L.Text("Изменения сохранены"));
    }

    private void IndividualGroupModes(Fence fence)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(L.Text("Режим и положение группы"), 17, Ui.Ink, FontWeights.SemiBold));
        var name = Ui.Text(fence.Name, 12, Ui.Accent, FontWeights.Medium);
        name.Margin = new Thickness(0, 5, 0, 10); body.Children.Add(name);
        var mode = Ui.Combo(body, L.Text("Режим группы"), Editors.ModeLabels, (int)fence.DisplayMode);
        AutomationProperties.SetAutomationId(mode, "display-mode");
        var edge = Ui.Combo(body, L.Text("Край экрана"), Editors.EdgeLabels, (int)fence.Edge);
        AutomationProperties.SetAutomationId(edge, "appearance-group-edge");
        var span = new EdgeSpanControl(fence); body.Children.Add(span);
        var layer = Ui.Combo(body, L.Text("Положение поверх окон"), Editors.LayerLabels, (int)fence.Layer);
        AutomationProperties.SetAutomationId(layer, "display-layer");
        var hover = Ui.Check(body, L.Text("Разворачивать свёрнутую группу при наведении"), fence.HoverExpand);
        var layerHint = Ui.Text(L.Text("Группа у края раскрывается поверх окон. Свободной группе можно выбрать свой слой."), 12, Ui.Muted);
        layerHint.Margin = new Thickness(0, 3, 0, 10); body.Children.Add(layerHint);
        void UpdateModeControls()
        {
            layer.IsEnabled = edge.SelectedIndex == 0;
            span.UpdateEdge((DockEdge)edge.SelectedIndex);
            if (edge.SelectedIndex > 0) layer.SelectedIndex = (int)FenceLayer.AlwaysOnTop;
            hover.IsEnabled = edge.SelectedIndex == 0 && Editors.AutomaticReveal(fence.RevealTrigger);
        }
        edge.SelectionChanged += (_, _) => UpdateModeControls(); UpdateModeControls();
        var modeHint = Ui.Text(L.Text("В минимальном режиме название и видимые вкладки находятся в одной строке."), 12, Ui.Muted);
        modeHint.Margin = new Thickness(0, 4, 0, 14); body.Children.Add(modeHint);
        body.Children.Add(Ui.Button(L.Text("Применить режим группы"), () => ApplyIndividualProfile(fence.Id, current =>
        {
            current.DisplayMode = (FenceDisplayMode)mode.SelectedIndex;
            current.Edge = (DockEdge)edge.SelectedIndex;
            span.Apply(current);
            current.Layer = current.Edge == DockEdge.None ? (FenceLayer)layer.SelectedIndex : FenceLayer.AlwaysOnTop;
            current.HoverExpand = Editors.AutomaticReveal(current.RevealTrigger) && (current.Edge != DockEdge.None || hover.IsChecked == true);
        }), true, "apply-display-mode"));
        _page.Children.Add(Ui.Card(body));
    }

    private void IndividualGroupAppearance(Fence fence, List<FrameworkElement> advancedFields)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(L.Text("Значки и вкладки группы"), 17, Ui.Ink, FontWeights.SemiBold));
        var name = Ui.Text(fence.Name, 12, Ui.Accent, FontWeights.Medium);
        name.Margin = new Thickness(0, 5, 0, 10); body.Children.Add(name);
        var opacity = Ui.Slider(body, L.Text("Непрозрачность группы, %"), 15, 100, fence.Opacity * 100);
        var size = Ui.Slider(body, L.Text("Размер значков"), 24, 96, fence.IconSize);
        var tint = Ui.Check(body, L.Text("Тонировать значки цветом группы"), fence.TintIcons);
        var labels = Ui.Check(body, L.Text("Показывать названия файлов"), fence.ShowLabels);
        var visibility = Ui.Combo(body, L.Text("Показывать вкладки"), Editors.TabVisibilityLabels, (int)Editors.VisibleTabPolicy(fence));
        AutomationProperties.SetAutomationId(visibility, "appearance-tab-visibility");
        body.Children.Add(Ui.Text(L.Text("Скрытые вкладки доступны в меню группы и через Ctrl+Tab."), 12, Ui.Muted));
        var style = Ui.Combo(body, L.Text("Стиль вкладок"), Editors.TabStyleLabels, (int)Editors.VisibleTabStyle(fence));
        var spacing = Ui.Slider(body, L.Text("Расстояние между значками, px"), 0, 24, fence.IconSpacing);
        var font = Ui.Slider(body, L.Text("Размер подписей, px"), 9, 18, fence.LabelFontSize);
        advancedFields.Add(tint);
        AdvancedField(advancedFields, body, style); AdvancedField(advancedFields, body, spacing); AdvancedField(advancedFields, body, font);
        body.Children.Add(Ui.Button(L.Text("Применить оформление группы"), () => ApplyIndividualProfile(fence.Id, current =>
        {
            current.Opacity = opacity.Value / 100; current.IconSize = (int)size.Value; current.ShowLabels = labels.IsChecked == true;
            current.TabVisibility = (TabVisibility)visibility.SelectedIndex;
            if (current.TabStyle == TabStyle.MenuOnly) current.TabStyle = TabStyle.Flat;
            if (!_app.Workspace.Preferences.AdvancedSettings) return;
            current.TintIcons = tint.IsChecked == true; current.TabStyle = (TabStyle)style.SelectedIndex;
            current.IconSpacing = (int)spacing.Value; current.LabelFontSize = (int)font.Value;
        }), true, "apply-appearance"));
        body.Children.Add(LabelAction("Все настройки…", "settings", () =>
        {
            var current = _app.Workspace.Fences.FirstOrDefault(f => f.Id == fence.Id);
            if (current != null) Editors.Fence(_app, current);
        }, id: "appearance-group-settings"));
        _page.Children.Add(Ui.Card(body));
    }
}
