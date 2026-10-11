using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WalleFences.App;

// Read stored Shell Link data only: never call Resolve, launch a target, search
// the network, or rewrite a user's desktop shortcut while importing it.
internal static class ShortcutReferences
{
    private const int MaximumChain = 16;
    private const uint RawPath = 4, HasDarwinId = 0x1000, RunAsUser = 0x2000;

    internal static ImportedReference[] Read(IEnumerable<string> paths) => paths.Select(Read).ToArray();

    internal static ImportedReference Read(string path)
    {
        var original = PathExtensions.Key(path);
        var fallback = new ImportedReference(original, original);
        if (!IsLink(original) || IsNetworkPath(original) || !File.Exists(original)) return fallback;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = original; var arguments = ""; var workingDirectory = ""; var runAs = false;
        try
        {
            for (var depth = 0; depth < MaximumChain; depth++)
            {
                if (!seen.Add(current) || IsNetworkPath(current) || !File.Exists(current)) return fallback;
                var data = ReadLink(current);
                // Installer-advertised and virtual Shell targets need the .lnk
                // itself. Keeping it is safer than inventing a filesystem path.
                if (data == null || (data.Flags & HasDarwinId) != 0) return fallback;
                var target = Environment.ExpandEnvironmentVariables(data.Path);
                if (!Path.IsPathFullyQualified(target) || IsNetworkPath(target)) return fallback;
                target = PathExtensions.Key(target);
                var directory = Environment.ExpandEnvironmentVariables(data.WorkingDirectory);
                if (directory.Length > 0)
                {
                    if (!Path.IsPathFullyQualified(directory) || IsNetworkPath(directory)) return fallback;
                    directory = PathExtensions.Key(directory);
                }
                // Ambiguous commands in a chain retain their original shortcut.
                if (arguments.Length > 0 && data.Arguments.Length > 0 && arguments != data.Arguments) return fallback;
                if (workingDirectory.Length > 0 && directory.Length > 0 && !PathExtensions.Same(workingDirectory, directory)) return fallback;
                if (data.Arguments.Length > 0) arguments = data.Arguments;
                if (directory.Length > 0) workingDirectory = directory;
                runAs |= (data.Flags & RunAsUser) != 0;
                if (IsLink(target)) { current = target; continue; }
                // .url and ClickOnce references are already useful launch files.
                if (!File.Exists(target) && !Directory.Exists(target)) return fallback;
                return new(original, target, new(Path.GetFileNameWithoutExtension(original), arguments, workingDirectory, runAs));
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return fallback;
    }

    internal static ImportedReference[] Read(FenceTab tab, IEnumerable<string> paths)
        => FileCatalog.References(tab, paths.Where(path => tab.Items.Any(item => PathExtensions.Same(item, path))))
            .Select(reference => reference.Launch == null ? Read(reference.Path) : reference).ToArray();

    private static bool IsLink(string path) => Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
    private static bool IsNetworkPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);
    private sealed record LinkData(string Path, string Arguments, string WorkingDirectory, uint Flags);

    private static LinkData? ReadLink(string path)
    {
        object? instance = null;
        try
        {
            instance = new ShellLink();
            ((IPersistFile)instance).Load(path, 0);
            var link = (IShellLinkW)instance;
            var target = new StringBuilder(32768); var arguments = new StringBuilder(32768); var directory = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, 0, RawPath);
            if (target.Length == 0) return null;
            link.GetArguments(arguments, arguments.Capacity); link.GetWorkingDirectory(directory, directory.Capacity);
            var flags = 0u;
            if (instance is IShellLinkDataList list) list.GetFlags(out flags);
            return new(target.ToString(), arguments.ToString(), directory.ToString(), flags);
        }
        finally { if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint idList, uint requestedAttributes, out uint attributes);
    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(nint idList, uint nameKind, out nint name);

    // Virtual Store-app links have no filesystem target. Inspect their stored PIDL
    // instead of treating every unresolved .lnk as a program.
    internal static string? ApplicationIdentity(string path)
    {
        if (!IsLink(path) || IsNetworkPath(path) || !File.Exists(path)) return null;
        object? instance = null; nint idList = 0, namePointer = 0;
        try
        {
            instance = new ShellLink(); ((IPersistFile)instance).Load(path, 0);
            ((IShellLinkW)instance).GetIDList(out idList);
            if (idList == 0 || SHGetNameFromIDList(idList, 0x80028000, out namePointer) < 0 || namePointer == 0) return null; // SIGDN_DESKTOPABSOLUTEPARSING
            var name = Marshal.PtrToStringUni(namePointer) ?? "";
            foreach (var prefix in new[] { @"shell:AppsFolder\", @"::{4234D49B-0245-4DF3-B780-3893943456E1}\" })
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length) return name[prefix.Length..];
            return null;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
        finally
        {
            if (namePointer != 0) Marshal.FreeCoTaskMem(namePointer);
            if (idList != 0) Marshal.FreeCoTaskMem(idList);
            if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
        }
    }

    internal static void CreateApplicationLink(string path, string appUserModelId, string name)
    {
        nint idList = 0; object? instance = null; string? temporary = null;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName("shell:AppsFolder\\" + appUserModelId, 0, out idList, 0, out _));
            if (idList == 0) throw new COMException(L.Text("Программа больше недоступна."));
            instance = new ShellLink();
            var link = (IShellLinkW)instance;
            link.SetIDList(idList); link.SetDescription(name);
            // Never overwrite an existing launch file. Publish a complete link
            // atomically so cancellation or an interrupted save leaves no half-link.
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            ((IPersistFile)instance).Save(temporary, true);
            try { File.Move(temporary, path, false); temporary = null; }
            catch (IOException) when (File.Exists(path)) { }
        }
        finally
        {
            if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
            if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
            if (idList != 0) Marshal.FreeCoTaskMem(idList);
        }
    }


    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private sealed class ShellLink { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, nint findData, uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int length);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int length);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int length);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int length, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
    [ComImport, Guid("45E2B4AE-B1C3-11D0-B92F-00A0C90312E1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkDataList
    {
        void AddDataBlock(nint data);
        void CopyDataBlock(uint signature, out nint data);
        void RemoveDataBlock(uint signature);
        void GetFlags(out uint flags);
        void SetFlags(uint flags);
    }
}
