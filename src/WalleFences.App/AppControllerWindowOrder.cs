using System.Windows.Interop;

namespace WalleFences.App;

internal sealed partial class AppController
{
    internal bool HasVisibleManager => Manager is { IsVisible: true } manager && manager.WindowState != WindowState.Minimized;

    internal void SyncManagerLayer()
    {
        if (_disposed || Manager is not { } manager) return;
        var visible = HasVisibleManager;
        if (manager.Topmost != visible) manager.Topmost = visible;
        if (!visible) return;
        foreach (var group in Windows) KeepGroupBelowManager(group);
    }

    internal void KeepGroupBelowManager(FenceWindow group)
    {
        if (_disposed || !HasVisibleManager || !group.IsVisible) return;
        var managerHandle = new WindowInteropHelper(Manager!).Handle;
        var groupHandle = new WindowInteropHelper(group).Handle;
        if (managerHandle == 0 || groupHandle == 0 || Native.ChildParent(groupHandle) != 0) return;
        // Keep the group's existing layer. Only reorder windows already in the topmost band.
        if (((long)Native.GetWindowLong(managerHandle, Native.GwlExStyle) & 8) == 0
            || ((long)Native.GetWindowLong(groupHandle, Native.GwlExStyle) & 8) == 0) return;
        Native.SetWindowPos(groupHandle, managerHandle, 0, 0, 0, 0,
            Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate);
    }
}
