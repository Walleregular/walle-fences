using System.IO.Enumeration;

namespace WalleFences.Core;

public static class FileCatalog
{
    public static FolderReadResult ReadFolderDetailed(string path, bool showHidden = false)
    {
        var entries = new List<FileEntry>();
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = false, RecurseSubdirectories = false, AttributesToSkip = showHidden ? FileAttributes.System : FileAttributes.Hidden | FileAttributes.System };
            foreach (var item in Directory.EnumerateFileSystemEntries(path, "*", options)) entries.Add(FileEntry.FromPath(item));
            return new(entries, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(entries, ex); }
    }

    public static IReadOnlyList<FileEntry> ReadFolder(string path, bool showHidden = false)
    {
        if (!Directory.Exists(path)) return [];
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = showHidden ? FileAttributes.System : FileAttributes.Hidden | FileAttributes.System };
            return Directory.EnumerateFileSystemEntries(path, "*", options).Select(FileEntry.FromPath).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    public static IReadOnlyList<FileEntry> Items(Fence fence, FenceTab tab, bool hidden = false)
    {
        var items = tab.PortalPath is { Length: > 0 } portal ? ReadFolder(portal, hidden) : References(tab).Select(Entry).ToList();
        return Sort(items, fence.Sort);
    }

    public static IReadOnlyList<FileEntry> Sort(IEnumerable<FileEntry> entries, SortOrder order) => (order switch
    {
        SortOrder.Name => entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
        SortOrder.Type => entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Extension).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
        SortOrder.Newest => entries.OrderByDescending(e => e.Modified),
        SortOrder.Oldest => entries.OrderBy(e => e.Modified),
        SortOrder.Size => entries.OrderByDescending(e => e.Size),
        _ => entries
    }).ToList();

    public static int AddReferences(FenceTab tab, IEnumerable<string> paths)
        => ImportReferences(tab, paths.Select(path => new ImportedReference(path, path))).Added;

    public static IReadOnlyList<ImportedReference> References(FenceTab tab, IEnumerable<string>? paths = null)
        => (paths ?? tab.Items).Select(path => new ImportedReference(path, path, Launch(tab, path))).ToArray();

    public static ReferenceLaunch? Launch(FenceTab tab, string path)
        => tab.ReferenceLaunches.GetValueOrDefault(PathExtensions.Key(path));

    public static FileEntry Entry(ImportedReference reference)
    {
        var entry = FileEntry.FromPath(reference.Path);
        return reference.Launch is { Name.Length: > 0 } launch ? entry with { Name = launch.Name } : entry;
    }

    public static ReferenceImportResult ImportReferences(FenceTab tab, IEnumerable<ImportedReference> references)
    {
        if (tab.PortalPath is { Length: > 0 }) throw new InvalidOperationException(L.Text("В портал нельзя добавить ссылку: выберите обычную вкладку."));
        var existing = tab.Items.Select(PathExtensions.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var count = 0; var accepted = new List<string>();
        foreach (var reference in references)
        {
            var full = PathExtensions.Key(reference.Path);
            if (!File.Exists(full) && !Directory.Exists(full)) continue;
            if (existing.Add(full))
            {
                tab.Items.Add(full);
                if (reference.Launch != null) tab.ReferenceLaunches[full] = reference.Launch;
                count++; accepted.Add(reference.SourcePath);
            }
            // A move may remove an equivalent duplicate from its source, but
            // never discard a different command for the same target executable.
            else if (SameInvocation(Launch(tab, full), reference.Launch)) accepted.Add(reference.SourcePath);
        }
        return new(count, accepted);
    }

    private static bool SameInvocation(ReferenceLaunch? a, ReferenceLaunch? b)
        => (a?.Arguments ?? "") == (b?.Arguments ?? "")
            && string.Equals(a?.WorkingDirectory ?? "", b?.WorkingDirectory ?? "", StringComparison.OrdinalIgnoreCase)
            && (a?.RunAsAdministrator ?? false) == (b?.RunAsAdministrator ?? false);

    public static int RemoveReferences(FenceTab tab, IEnumerable<string> paths)
    {
        var removed = paths.Select(PathExtensions.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var count = tab.Items.RemoveAll(path => removed.Contains(PathExtensions.Key(path)));
        foreach (var path in removed) tab.ReferenceLaunches.Remove(path);
        return count;
    }

    public static ReferenceImportResult TransferReferences(FenceTab source, FenceTab target, IEnumerable<ImportedReference> references, bool move)
    {
        if (source == target) return new(0, []);
        var selected = references.Where(r => source.Items.Any(path => PathExtensions.Same(path, r.SourcePath))).ToArray();
        var result = ImportReferences(target, selected);
        if (move) RemoveReferences(source, result.AcceptedSources);
        return result;
    }

    public static int RenameReferences(FenceTab tab, string oldPath, string newPath)
    {
        var oldKey = PathExtensions.Key(oldPath); var newKey = PathExtensions.Key(newPath);
        var launch = Launch(tab, oldKey); var changed = 0;
        for (var i = 0; i < tab.Items.Count; i++)
            if (PathExtensions.Same(tab.Items[i], oldKey)) { tab.Items[i] = newKey; changed++; }
        if (changed == 0) return 0;
        tab.ReferenceLaunches.Remove(oldKey);
        if (launch != null) tab.ReferenceLaunches.TryAdd(newKey, launch);
        return changed;
    }
}

public sealed record FolderReadResult(IReadOnlyList<FileEntry> Entries, Exception? Error);

public static partial class RuleEngine
{
    public static bool Matches(SortingRule rule, FileEntry entry, DateTime utcNow)
    {
        if (rule.DesktopCategory != null) return DesktopOrganizer.Category(entry) == rule.DesktopCategory;
        var pattern = rule.Pattern.Trim();
        return rule.Kind switch
        {
            RuleKind.Extension => !entry.IsDirectory && pattern.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Any(x => string.Equals(entry.Extension, "." + x.TrimStart('*', '.'), StringComparison.OrdinalIgnoreCase)),
            RuleKind.Name => pattern.Length > 0 && FileSystemName.MatchesSimpleExpression(pattern, entry.Name, true),
            RuleKind.Folder => entry.IsDirectory,
            RuleKind.ModifiedWithinDays => int.TryParse(pattern, out var days) && days >= 0 && entry.Modified >= utcNow.AddDays(-days) && entry.Modified <= utcNow,
            RuleKind.Everything => true,
            _ => false
        };
    }

    // First matching valid rule wins; manually assigned references remain where the user placed them.
    public static int Organize(Workspace workspace, IEnumerable<FileEntry> entries, DateTime? now = null, Func<FileEntry, ImportedReference>? resolve = null)
    {
        var changed = 0;
        foreach (var row in Preview(workspace, entries, now, resolve).Where(row => row.Status == SortingPreviewStatus.Ready))
        {
            var target = workspace.Fences.First(f => f.Id == row.Destination!.FenceId).Tabs.First(t => t.Id == row.Destination!.TabId);
            changed += FileCatalog.ImportReferences(target, [row.Reference!]).Added;
        }
        return changed;
    }
}
