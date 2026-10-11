namespace WalleFences.App;

// New widgets have their own group. Existing mixed groups remain unchanged until
// a person explicitly detaches a widget from them.
internal static class SingleWidgetPanels
{
    internal static Fence Create(AppController app, WidgetKind kind, Fence? styleSource = null, DisplayArea? display = null)
    {
        var tab = new FenceTab { Name = WidgetEditor.Labels[(int)kind] };
        var panel = new Fence
        {
            Name = tab.Name, PageId = app.Workspace.ActivePageId, Tabs = [tab], ActiveTabId = tab.Id,
            TabVisibility = TabVisibility.Hidden, DisplayMode = FenceDisplayMode.Compact,
            Layer = styleSource?.Layer ?? FenceLayer.AlwaysOnTop,
            Color = styleSource?.Color ?? "#1D2021", Accent = styleSource?.Accent ?? app.Workspace.Preferences.InterfaceAccent,
            Opacity = styleSource?.Opacity ?? .97, SurfaceStyle = styleSource?.SurfaceStyle ?? SurfaceStyle.Solid,
            CornerRadius = styleSource?.CornerRadius ?? 12, TabStyle = styleSource?.TabStyle ?? TabStyle.Flat,
            Edge = styleSource?.Edge ?? DockEdge.None, EdgeThickness = styleSource?.EdgeThickness ?? 3,
            ActivationDistance = styleSource?.ActivationDistance ?? 12,
            OpenDelayMs = styleSource?.OpenDelayMs ?? 180, CloseDelayMs = styleSource?.CloseDelayMs ?? 500,
            RevealTrigger = styleSource?.RevealTrigger ?? RevealTrigger.Proximity,
            HoverExpand = styleSource?.HoverExpand ?? false, RolledUp = false,
            KeepVisibleOnHide = styleSource?.KeepVisibleOnHide ?? false
        };
        ApplySize(panel, kind);
        Place(app, panel, SelectDisplay(styleSource, display));
        return panel;
    }

    internal static void ApplySize(Fence panel, WidgetKind kind)
    {
        (panel.Width, panel.Height) = kind switch
        {
            WidgetKind.Clock => (280, 206), WidgetKind.Note => (340, 306),
            WidgetKind.Focus => (292, 250), WidgetKind.System => (320, 284),
            WidgetKind.Drives => (340, 360), WidgetKind.CpuTemperature => (340, 340),
            WidgetKind.Processes => (420, 420), WidgetKind.Memory => (320, 260),
            WidgetKind.Gpu => (360, 340), WidgetKind.Network => (360, 280),
            WidgetKind.Battery => (300, 250), WidgetKind.Cpu => (320, 240), WidgetKind.Uptime => (320, 230),
            _ => (320, 280)
        };
        panel.OpeningSize = OpeningSize.Custom;
        panel.EdgeLength = kind is WidgetKind.Clock or WidgetKind.Focus ? 88 : 96;
        var label = WidgetEditor.Labels[(int)kind];
        if (string.IsNullOrWhiteSpace(panel.Name) || WidgetEditor.Labels.Contains(panel.Name)) panel.Name = label;
        if (panel.Tabs.Count == 1 && (string.IsNullOrWhiteSpace(panel.Tabs[0].Name) || WidgetEditor.Labels.Contains(panel.Tabs[0].Name))) panel.Tabs[0].Name = label;
        // Reconcile must not restore the dimensions of the originally selected
        // kind when the editor saves another kind into the same draft.
        if (panel.Placement is { } placement) panel.Placement = placement with { Width = panel.Width, Height = panel.Height };
    }

    internal static void Place(AppController app, Fence panel, DisplayArea? display = null)
    {
        display = SelectDisplay(panel, display);
        if (panel.Edge != DockEdge.None && !PlaceOnEdge(app, panel, display))
        {
            // A full edge must not cover another strip or rearrange the user's
            // groups. The independent widget can still be added on this screen.
            panel.Edge = DockEdge.None; panel.RolledUp = false; panel.HoverExpand = false;
        }
        if (panel.Edge == DockEdge.None) PlaceFree(app, panel, display);
        DisplayLayout.Capture(panel, AppController.Displays(), display);
    }

    internal static bool IsSingleWidgetPanel(Fence fence, string widgetId) => fence.Tabs.Count == 1
        && fence.Tabs[0].Widgets.Count == 1 && fence.Tabs[0].Widgets[0].Id == widgetId
        && fence.Tabs[0].Items.Count == 0 && string.IsNullOrWhiteSpace(fence.Tabs[0].PortalPath);

    internal static Fence Detach(AppController app, string fenceId, string tabId, string widgetId)
    {
        var current = WidgetEditor.Resolve(app, fenceId, tabId, widgetId);
        WorkspaceActions.RequireEditable(current.Fence);
        if (IsSingleWidgetPanel(current.Fence, widgetId))
        {
            app.OpenGroup(current.Fence, current.Tab);
            return current.Fence;
        }
        Fence? detached = null;
        app.Mutate(() =>
        {
            var latest = WidgetEditor.Resolve(app, fenceId, tabId, widgetId);
            WorkspaceActions.RequireEditable(latest.Fence);
            var widget = latest.Widget!;
            var display = DisplayLayout.Select(latest.Fence, AppController.Displays());
            detached = Create(app, widget.Kind, latest.Fence, display);
            detached.PageId = latest.Fence.PageId;
            detached.Name = string.IsNullOrWhiteSpace(widget.Title) ? WidgetEditor.Labels[(int)widget.Kind] : widget.Title;
            detached.ActiveTab.Name = detached.Name;
            detached.Accent = latest.Tab.Color ?? latest.Fence.Accent;
            // Preserve the actual object, including a running focus timer and
            // note text. Removing only this widget leaves files/portals intact.
            latest.Tab.Widgets.Remove(widget);
            detached.ActiveTab.Widgets.Add(widget);
            Place(app, detached, display);
            app.Workspace.Fences.Add(detached);
        });
        app.OpenGroup(detached!);
        return detached!;
    }

    private static DisplayArea SelectDisplay(Fence? source, DisplayArea? preferred)
    {
        var displays = AppController.Displays();
        if (preferred != null && displays.FirstOrDefault(d => d.Id.Equals(preferred.Id, StringComparison.OrdinalIgnoreCase)) is { } selected) return selected;
        if (source != null)
        {
            if (source.Placement is { } placement && displays.FirstOrDefault(d => d.Id.Equals(placement.MonitorId, StringComparison.OrdinalIgnoreCase)) is { } home) return home;
            return DisplayLayout.Select(source, displays);
        }
        return displays.FirstOrDefault(d => d.Primary) ?? displays[0];
    }

    private static bool PlaceOnEdge(AppController app, Fence panel, DisplayArea display)
    {
        const double margin = 16, gap = 8;
        var vertical = panel.Edge is DockEdge.Left or DockEdge.Right;
        var available = (vertical ? display.Height : display.Width) / display.Scale;
        var offset = margin;
        var strips = app.Workspace.Fences.Where(f => f.Id != panel.Id && f.PageId == panel.PageId && f.Edge == panel.Edge && DisplayLayout.Select(f, AppController.Displays()).Id == display.Id)
            .Select(f => EdgeLayout.Bounds(f, display, false)).OrderBy(r => vertical ? r.Y : r.X);
        foreach (var strip in strips)
        {
            var start = (vertical ? strip.Y - display.Y : strip.X - display.X) / display.Scale;
            var length = (vertical ? strip.Height : strip.Width) / display.Scale;
            if (offset + panel.EdgeLength + gap <= start) break;
            offset = Math.Max(offset, start + length + gap);
        }
        if (offset + panel.EdgeLength > available - margin) return false;
        panel.EdgeOffset = offset;
        return true;
    }

    private static void PlaceFree(AppController app, Fence panel, DisplayArea display)
    {
        var margin = 16 * display.Scale;
        var gap = Math.Max(8, app.Workspace.Preferences.FenceGap) * display.Scale;
        panel.X = display.X + margin; panel.Y = display.Y + margin;
        DisplayLayout.Clamp(panel, display);
        var width = panel.Width * display.Scale;
        var height = (panel.ContentCollapsed ? FenceLayout.CollapsedHeight(panel) : panel.Height) * display.Scale;
        var left = Math.Min(display.X + margin, display.Right - width);
        var top = Math.Min(display.Y + margin, display.Bottom - height);
        var right = Math.Max(left, display.Right - width - margin);
        var bottom = Math.Max(top, display.Bottom - height - margin);
        var occupied = app.Workspace.Fences.Where(f => f.Id != panel.Id && f.PageId == panel.PageId && DisplayLayout.Select(f, AppController.Displays()).Id == display.Id)
            .Select(f => f.Edge == DockEdge.None
                ? new LayoutRect(f.X, f.Y, f.Width * display.Scale, (f.ContentCollapsed ? FenceLayout.CollapsedHeight(f) : f.Height) * display.Scale)
                : EdgeLayout.Bounds(f, display, false)).OrderBy(r => r.X).ToArray();
        var rows = occupied.Select(r => r.Bottom + gap).Append(top).Append(bottom).Where(y => y >= top && y <= bottom).Distinct().OrderBy(y => y);
        var fallback = new LayoutRect(left, top, width, height);
        var leastOverlap = double.PositiveInfinity;
        foreach (var y in rows)
        {
            var x = left;
            while (x <= right)
            {
                var candidate = new LayoutRect(x, y, width, height);
                var overlap = occupied.Sum(r => Math.Max(0, Math.Min(candidate.Right, r.Right + gap) - Math.Max(candidate.X, r.X - gap))
                    * Math.Max(0, Math.Min(candidate.Bottom, r.Bottom + gap) - Math.Max(candidate.Y, r.Y - gap)));
                if (overlap < leastOverlap) { leastOverlap = overlap; fallback = candidate; }
                if (overlap == 0) { panel.X = x; panel.Y = y; return; }
                var blocker = occupied.FirstOrDefault(r => candidate.X < r.Right + gap && candidate.Right > r.X - gap && candidate.Y < r.Bottom + gap && candidate.Bottom > r.Y - gap);
                var next = blocker.Right + gap;
                if (next <= x) break;
                x = next;
            }
        }
        panel.X = fallback.X; panel.Y = fallback.Y;
    }
}
