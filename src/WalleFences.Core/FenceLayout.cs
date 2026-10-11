namespace WalleFences.Core;

public static class FenceLayout
{
    public static double HeaderHeight(FenceDisplayMode mode) => mode == FenceDisplayMode.Standard ? 34 : 28;
    public static double TabsHeight(FenceDisplayMode mode) => mode switch { FenceDisplayMode.Standard => 28, FenceDisplayMode.Compact => 30, _ => 0 };
    public static bool ShowTabs(Fence fence) => fence.TabStyle != TabStyle.MenuOnly && (fence.TabVisibility switch { TabVisibility.Hidden => false, TabVisibility.Always => true, _ => fence.Tabs.Count > 1 });
    public static double TabsHeight(Fence fence) => ShowTabs(fence) ? TabsHeight(fence.DisplayMode) : 0;
    public static double CollapsedHeight(Fence fence) => fence.Edge is DockEdge.Top or DockEdge.Bottom ? fence.EdgeThickness : fence.Edge is DockEdge.Left or DockEdge.Right ? fence.EdgeLength : HeaderHeight(fence.DisplayMode) + TabsHeight(fence) + 2;
    public static bool OnTop(Fence fence, bool temporarilyExpanded = false) => fence.Edge != DockEdge.None || fence.Layer == FenceLayer.AlwaysOnTop || fence.Layer == FenceLayer.ExpandedOnTop && (!fence.ContentCollapsed || temporarilyExpanded);
}
