using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WalleFences.App;

// Hosts the real Explorer context menu, including file-type and installed shell extension verbs.
internal static class ShellMenu
{
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(nint hwnd, nint bind, [MarshalAs(UnmanagedType.LPWStr)] string name, ref uint eaten, out nint pidl, ref uint attributes);
        [PreserveSig] int EnumObjects(nint hwnd, uint flags, out nint enumerator);
        [PreserveSig] int BindToObject(nint pidl, nint bind, in Guid iid, out nint result);
        [PreserveSig] int BindToStorage(nint pidl, nint bind, in Guid iid, out nint result);
        [PreserveSig] int CompareIDs(nint lparam, nint first, nint second);
        [PreserveSig] int CreateViewObject(nint hwnd, in Guid iid, out nint result);
        [PreserveSig] int GetAttributesOf(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] items, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(nint hwnd, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] items, in Guid iid, nint reserved, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int GetDisplayNameOf(nint pidl, uint flags, nint result);
        [PreserveSig] int SetNameOf(nint hwnd, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint result);
    }
    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(nuint id, uint flags, nint reserved, nint text, uint count);
    }
    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(nuint id, uint flags, nint reserved, nint text, uint count);
        [PreserveSig] int HandleMenuMsg(uint message, nint wparam, nint lparam);
    }
    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(nuint id, uint flags, nint reserved, nint text, uint count);
        [PreserveSig] int HandleMenuMsg(uint message, nint wparam, nint lparam);
        [PreserveSig] int HandleMenuMsg2(uint message, nint wparam, nint lparam, out nint result);
    }
    [StructLayout(LayoutKind.Sequential)] private struct InvokeInfo
    {
        public uint Size, Mask; public nint Window, Verb, Parameters, Directory; public int Show; public uint Hotkey; public nint Icon, Title, VerbW, ParametersW, DirectoryW, TitleW; public Native.Point Point;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint attributes, out uint resultAttributes);
    [DllImport("shell32.dll")] private static extern int SHBindToParent(nint pidl, in Guid iid, out IShellFolder folder, out nint child);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Native.Point point);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);

    private sealed class Context : IDisposable
    {
        internal readonly IContextMenu Menu;
        internal readonly nint Handle;
        private readonly IShellFolder _folder;
        private readonly nint _pidl;
        internal Context(string path, nint owner)
        {
            var folderId = typeof(IShellFolder).GUID; var contextId = typeof(IContextMenu).GUID;
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out _pidl, 0, out _));
            try
            {
                Marshal.ThrowExceptionForHR(SHBindToParent(_pidl, folderId, out _folder, out var child));
                try
                {
                    Marshal.ThrowExceptionForHR(_folder.GetUIObjectOf(owner, 1, [child], contextId, 0, out var menu));
                    Menu = (IContextMenu)menu; Handle = CreatePopupMenu();
                    if (Handle == 0) throw new System.ComponentModel.Win32Exception();
                    try { Marshal.ThrowExceptionForHR(Menu.QueryContextMenu(Handle, 0, 1, 0x7FFF, 4 | (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0x100u : 0u))); }
                    catch { DestroyMenu(Handle); Marshal.FinalReleaseComObject(Menu); throw; }
                }
                catch { Marshal.FinalReleaseComObject(_folder); throw; }
            }
            catch { Marshal.FreeCoTaskMem(_pidl); throw; }
        }
        public void Dispose() { DestroyMenu(Handle); Marshal.FinalReleaseComObject(Menu); Marshal.FinalReleaseComObject(_folder); Marshal.FreeCoTaskMem(_pidl); }
    }
    internal static int Inspect(string path, Window owner)
    {
        using var context = new Context(path, new WindowInteropHelper(owner).Handle); return GetMenuItemCount(context.Handle);
    }
    internal static void Show(string path, Window owner)
    {
        var hwnd = new WindowInteropHelper(owner).Handle; using var context = new Context(path, hwnd);
        var source = HwndSource.FromHwnd(hwnd); var menu3 = context.Menu as IContextMenu3; var menu2 = context.Menu as IContextMenu2;
        HwndSourceHook hook = (nint window, int msg, nint wp, nint lp, ref bool handled) =>
        {
            if (msg is 0x117 or 0x2B or 0x2C or 0x120)
            {
                if (menu3 != null && menu3.HandleMenuMsg2((uint)msg, wp, lp, out var result) >= 0) { handled = true; return result; }
                if (menu2 != null && msg != 0x120 && menu2.HandleMenuMsg((uint)msg, wp, lp) >= 0) handled = true;
            }
            return 0;
        };
        source?.AddHook(hook);
        try
        {
            GetCursorPos(out var point); Native.SetForegroundWindow(hwnd);
            var command = TrackPopupMenuEx(context.Handle, 0x100 | 2, point.X, point.Y, hwnd, 0);
            if (command == 0) return;
            var info = new InvokeInfo { Size = (uint)Marshal.SizeOf<InvokeInfo>(), Mask = 0x4000 | 0x20000000, Window = hwnd, Verb = (nint)(command - 1), VerbW = (nint)(command - 1), Show = 1, Point = point };
            Marshal.ThrowExceptionForHR(context.Menu.InvokeCommand(ref info));
        }
        finally { source?.RemoveHook(hook); }
    }
}
