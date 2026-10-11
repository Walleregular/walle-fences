using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WalleFences.Core;

// Program history belongs to the profile, independently of layout snapshots and Undo.
public sealed class ProgramLibrary
{
    public List<ProgramLibraryEntry> Entries { get; set; } = [];
    public const int RecentLimit = 80, FavoriteLimit = 500;

    public static string InvocationId(string path, ReferenceLaunch? launch = null)
    {
        var target = PathExtensions.Key(path).ToUpperInvariant();
        var directory = launch?.WorkingDirectory ?? "";
        if (directory.Length > 0) directory = PathExtensions.Key(directory).ToUpperInvariant();
        var invocation = JsonSerializer.Serialize(new[] { target, launch?.Arguments ?? "", directory, launch?.RunAsAdministrator == true ? "1" : "0" });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invocation)));
    }

    public ProgramLibraryEntry? Find(string path, ReferenceLaunch? launch = null)
    {
        var id = InvocationId(path, launch);
        return Entries.FirstOrDefault(entry => entry.Id == id);
    }

    public ProgramLibraryEntry SetFavorite(string path, ReferenceLaunch? launch, bool favorite, string? catalogId = null)
    {
        var existing = Find(path, launch);
        if (favorite && existing?.IsFavorite != true && Entries.Count(entry => entry.IsFavorite) >= FavoriteLimit)
            throw new InvalidOperationException(L.Format("Можно сохранить не более {0} избранных программ.", FavoriteLimit));
        var entry = existing ?? Add(path, launch);
        Update(entry, path, launch, catalogId); entry.IsFavorite = favorite;
        Trim(); return entry;
    }

    public void RecordSuccess(string path, ReferenceLaunch? launch, DateTimeOffset now)
    {
        var entry = Find(path, launch) ?? Add(path, launch);
        Update(entry, path, launch, null); entry.LastLaunchUtc = now;
        entry.LaunchCount = (int)Math.Min(int.MaxValue, (long)entry.LaunchCount + 1);
        Trim();
    }

    public void ClearRecent()
    {
        foreach (var entry in Entries) { entry.LastLaunchUtc = null; entry.LaunchCount = 0; }
        Trim();
    }

    private ProgramLibraryEntry Add(string path, ReferenceLaunch? launch)
    {
        var entry = new ProgramLibraryEntry { Path = PathExtensions.Key(path), Launch = launch };
        Entries.Add(entry); return entry;
    }

    private static void Update(ProgramLibraryEntry entry, string path, ReferenceLaunch? launch, string? catalogId)
    {
        entry.Path = PathExtensions.Key(path);
        if (launch != null && string.IsNullOrWhiteSpace(launch.Name) && entry.Launch is { Name.Length: > 0 } named) launch = launch with { Name = named.Name };
        // Anonymous launches must not discard the display name of an existing favorite.
        entry.Launch = launch is null && entry.Launch is { Arguments.Length: 0, WorkingDirectory.Length: 0, RunAsAdministrator: false }
            ? entry.Launch : launch;
        if (!string.IsNullOrWhiteSpace(catalogId) && !entry.CatalogIds.Contains(catalogId, StringComparer.OrdinalIgnoreCase)) entry.CatalogIds.Add(catalogId);
    }

    public void Trim()
    {
        var recent = Entries.Where(entry => entry.LastLaunchUtc != null).OrderByDescending(entry => entry.LastLaunchUtc).Take(RecentLimit).Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in Entries.Where(entry => entry.LastLaunchUtc != null && !recent.Contains(entry.Id))) { entry.LastLaunchUtc = null; entry.LaunchCount = 0; }
        Entries.RemoveAll(entry => !entry.IsFavorite && entry.LastLaunchUtc == null);
    }
}

public sealed class ProgramLibraryEntry
{
    private (string Path, string Arguments, string Directory, bool Administrator)? _idKey;
    private string? _id;
    public string Path { get; set; } = "";
    public ReferenceLaunch? Launch { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset? LastLaunchUtc { get; set; }
    public int LaunchCount { get; set; }
    public List<string> CatalogIds { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public string Id
    {
        get
        {
            var key = (Path, Launch?.Arguments ?? "", Launch?.WorkingDirectory ?? "", Launch?.RunAsAdministrator ?? false);
            if (_id == null || _idKey is not { } cached || cached != key) { _idKey = key; _id = ProgramLibrary.InvocationId(Path, Launch); }
            return _id;
        }
    }
    [System.Text.Json.Serialization.JsonIgnore] public string Name => !string.IsNullOrWhiteSpace(Launch?.Name) ? Launch.Name : System.IO.Path.GetFileNameWithoutExtension(Path);
}

public sealed class ProgramLibraryStore
{
    private readonly string _path;
    public bool BackupLoaded { get; private set; }
    public ProgramLibraryStore(string root) { _path = System.IO.Path.Combine(root, "programs.json"); }

    public ProgramLibrary Load()
    {
        if (!File.Exists(_path)) return new();
        try { return Read(_path); }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or ArgumentException)
        {
            if (File.Exists(_path + ".bak")) { var library = Read(_path + ".bak"); BackupLoaded = true; return library; }
            throw;
        }
    }

    private static ProgramLibrary Read(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException(L.Text("Библиотека программ слишком велика."));
        var library = JsonSerializer.Deserialize<ProgramLibrary>(File.ReadAllText(path), WorkspaceStore.JsonOptions) ?? throw new InvalidDataException(L.Text("Библиотека программ повреждена."));
        if (library.Entries == null || library.Entries.Count > 2000) throw new InvalidDataException(L.Text("Библиотека программ повреждена."));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        library.Entries.RemoveAll(entry => entry == null || string.IsNullOrWhiteSpace(entry.Path));
        foreach (var entry in library.Entries)
        {
            entry.Path = PathExtensions.Key(entry.Path);
            if (entry.Launch is { } launch && ((launch.Name?.Length ?? 0) > 256 || (launch.Arguments?.Length ?? 0) > 32768 || (launch.WorkingDirectory?.Length ?? 0) > 4096))
                throw new InvalidDataException(L.Text("Библиотека программ повреждена."));
            entry.Launch = entry.Launch is { } invocation ? invocation with { Name = invocation.Name ?? "", Arguments = invocation.Arguments ?? "", WorkingDirectory = invocation.WorkingDirectory ?? "" } : null;
            entry.CatalogIds ??= []; entry.CatalogIds = entry.CatalogIds.Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 4096).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
            entry.LaunchCount = Math.Max(0, entry.LaunchCount);
        }
        library.Entries.RemoveAll(entry => !ids.Add(entry.Id));
        if (library.Entries.Count(entry => entry.IsFavorite) > ProgramLibrary.FavoriteLimit) throw new InvalidDataException(L.Text("Библиотека программ повреждена."));
        library.Trim(); return library;
    }

    public void Save(ProgramLibrary library)
    {
        library.Trim();
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream)) { writer.Write(JsonSerializer.Serialize(library, WorkspaceStore.JsonOptions)); writer.Flush(); stream.Flush(true); }
            if (File.Exists(_path)) File.Replace(temporary, _path, BackupLoaded ? null : _path + ".bak", true);
            else File.Move(temporary, _path);
            BackupLoaded = false;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
