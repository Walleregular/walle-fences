namespace WalleFences.App;

internal sealed partial class AppController
{
    internal Fence? AddWidgetPanel(Window? owner = null, Fence? styleSource = null, DisplayArea? display = null)
    {
        if (_disposed) return null;
        var pageId = Workspace.ActivePageId;
        var source = styleSource == null ? null : Workspace.Fences.FirstOrDefault(fence => fence.Id == styleSource.Id) ?? styleSource;
        var palette = new Fence
        {
            Color = source?.Color ?? "#1D2021", Accent = source?.ActiveTab.Color ?? source?.Accent ?? Workspace.Preferences.InterfaceAccent,
            Opacity = source?.Opacity ?? .97, SurfaceStyle = source?.SurfaceStyle ?? SurfaceStyle.Solid,
            CornerRadius = source?.CornerRadius ?? 12, Layer = source?.Layer ?? FenceLayer.AlwaysOnTop,
            Edge = DockEdge.None, HoverExpand = false, RolledUp = false, RevealTrigger = RevealTrigger.Click
        };
        display ??= source == null ? null : DisplayLayout.Select(source, Displays());
        if (HasVisibleManager && (owner is null or FenceWindow)) owner = Manager;
        var widget = WidgetPickerWindow.Pick(this, owner, palette);
        if (widget == null || _disposed) return null;
        if (!Workspace.Pages.Any(page => page.Id == pageId)) throw new InvalidOperationException(L.Text("Набор удалён"));
        Fence? added = null;
        Mutate(() =>
        {
            added = SingleWidgetPanels.Create(this, widget.Kind, palette, display);
            added.PageId = pageId;
            added.Name = string.IsNullOrWhiteSpace(widget.Title) ? WidgetEditor.Labels[(int)widget.Kind] : widget.Title.Trim();
            added.ActiveTab.Name = added.Name;
            added.ActiveTab.Widgets.Add(widget);
            // Inherit the source colours, while giving the new panel its own position and behaviour.
            SingleWidgetPanels.Place(this, added, display);
            Workspace.Fences.Add(added);
        });
        if (Workspace.ActivePageId != pageId) SwitchPage(pageId);
        if (Hidden) ToggleHidden();
        if (HasVisibleManager) Manager!.SelectAddedWidget(added!);
        else OpenGroup(added!);
        SaveNow(); SetStatus(L.Format("Добавлено: {0}", added!.Name));
        return added;
    }
}
