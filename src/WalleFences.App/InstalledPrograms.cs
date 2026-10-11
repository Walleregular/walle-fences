using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WalleFences.App;

internal sealed record InstalledProgram(string Id, string Name, string? ShortcutPath, string? AppUserModelId, ImageSource? Icon)
{
    internal bool IsWindowsApplication => AppUserModelId != null;
    internal ImportedReference Reference(string profileRoot)
    {
        if (ShortcutPath != null)
        {
            if (!File.Exists(ShortcutPath)) throw new FileNotFoundException(L.Text("Программа больше недоступна."), ShortcutPath);
            return ShortcutReferences.Read(ShortcutPath);
        }
        var identity = AppUserModelId ?? throw new InvalidOperationException(L.Text("Программа больше недоступна."));
        // A distinct virtual Shell Link keeps each Store application's identity.
        // Saving explorer.exe for every app would collapse them into one file item.
        var directory = Path.Combine(profileRoot, "Applications");
        Directory.CreateDirectory(directory);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var path = Path.Combine(directory, "app-" + hash + ".lnk");
        if (!File.Exists(path)) ShortcutReferences.CreateApplicationLink(path, identity, Name);
        return new(path, path, new(Name));
    }
}

internal sealed record InstalledProgramCatalog(IReadOnlyList<InstalledProgram> Programs, IReadOnlyList<Exception> Errors);

internal static class InstalledPrograms
{
    internal static Task<ImportedReference[]> PrepareReferencesAsync(IEnumerable<InstalledProgram> selected, string profileRoot, CancellationToken cancellation)
    {
        var programs = selected.ToArray();
        return RunSta(() => programs.Select(program =>
        {
            cancellation.ThrowIfCancellationRequested();
            try { return program.Reference(profileRoot); }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { throw new InvalidOperationException(L.Format("Не удалось подготовить «{0}».", program.Name), ex); }
        }).ToArray(), cancellation);
    }

    internal static Task<InstalledProgramCatalog> LoadAsync(CancellationToken cancellation)
        => RunSta(() => Read(cancellation), cancellation);

    private static Task<T> RunSta<T>(Func<T> action, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Shell automation and Shell Link COM objects stay on one background STA.
        var worker = new Thread(() =>
        {
            try { cancellation.ThrowIfCancellationRequested(); completion.TrySetResult(action()); }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellation); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "Walle Fences application catalog" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
        return completion.Task;
    }

    private static InstalledProgramCatalog Read(CancellationToken cancellation)
    {
        var programs = new List<InstalledProgram>(); var errors = new List<Exception>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var special in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            var root = Environment.GetFolderPath(special);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested(); var directory = pending.Pop();
                try
                {
                    foreach (var path in Directory.EnumerateFiles(directory))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (!PathExtensions.IsShortcut(path) || !paths.Add(path)) continue;
                        var name = Path.GetFileNameWithoutExtension(path);
                        if (name.Length > 0) programs.Add(new("link:" + path, name, path, null, ReadIcon(path)));
                    }
                    foreach (var child in Directory.EnumerateDirectories(directory))
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { errors.Add(ex); }
            }
        }
        ReadWindowsApplications(programs, errors, cancellation);
        return new(programs.OrderBy(program => program.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), errors);
    }

    private static void ReadWindowsApplications(List<InstalledProgram> programs, List<Exception> errors, CancellationToken cancellation)
    {
        object? shell = null, folder = null, items = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new COMException("Shell.Application unavailable");
            shell = Activator.CreateInstance(type) ?? throw new COMException("Shell.Application unavailable");
            folder = ((dynamic)shell).NameSpace("shell:AppsFolder");
            if (folder == null) throw new COMException("AppsFolder unavailable");
            items = ((dynamic)folder).Items();
            var count = (int)((dynamic)items).Count;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // A Start Menu entry remains the preferred source for desktop apps.
            var names = programs.Select(program => program.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            for (var index = 0; index < count; index++)
            {
                cancellation.ThrowIfCancellationRequested(); object? item = null;
                try
                {
                    item = ((dynamic)items).Item(index);
                    if (item == null) continue;
                    var name = (string)((dynamic)item).Name;
                    var id = (string?)((dynamic)item).ExtendedProperty("System.AppUserModel.ID");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || !ids.Add(id) || names.Contains(name)) continue;
                    programs.Add(new("app:" + id, name, null, id, ReadIcon("shell:AppsFolder\\" + id)));
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { errors.Add(ex); }
                finally { Release(item); }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { errors.Add(ex); }
        finally { Release(items); Release(folder); Release(shell); }
    }

    private static void Release(object? instance)
    {
        if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint idList, uint requestedAttributes, out uint attributes);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(nint idList, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    private static ImageSource? ReadIcon(string parsingName)
    {
        nint idList = 0;
        try
        {
            if (SHParseDisplayName(parsingName, 0, out idList, 0, out _) < 0 || idList == 0) return null;
            _ = SHGetFileInfo(idList, 0, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x108); // SHGFI_ICON | SHGFI_PIDL
            if (info.Icon == 0) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze(); return source;
            }
            finally { DestroyIcon(info.Icon); }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException) { return null; }
        finally { if (idList != 0) Marshal.FreeCoTaskMem(idList); }
    }
}
