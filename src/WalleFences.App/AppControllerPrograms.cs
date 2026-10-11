namespace WalleFences.App;

internal sealed partial class AppController
{
    private ProgramLibrary? _programLibrary;
    private ProgramLibraryStore? _programLibraryStore;
    private bool _programLibraryReadFailed, _programHistoryAttached, _programLibraryDirty;
    private readonly DispatcherTimer _programSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private sealed record ShortcutProgramCache(DateTime Modified, long Size, ImportedReference? Reference);
    private static readonly Dictionary<string, ShortcutProgramCache> ProgramShortcutCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ProgramShortcutCacheLock = new();
    internal event Action? ProgramLibraryChanged;
    internal ProgramLibrary Programs { get { EnsureProgramLibrary(); return _programLibrary!; } }

    private void EnsureProgramLibrary()
    {
        if (_programLibrary != null) return;
        _programLibraryStore = new(Store.Root);
        try { _programLibrary = _programLibraryStore.Load(); }
        catch (Exception ex)
        {
            _programLibraryReadFailed = true; _programLibrary = new(); Log(ex);
            SetStatus(L.Text("Не удалось прочитать библиотеку программ. Исходный файл сохранён."), refreshViews: false);
        }
    }

    internal void AttachProgramHistory()
    {
        if (_programHistoryAttached) return;
        EnsureProgramLibrary(); _programHistoryAttached = true;
        _programSaveTimer.Tick += SaveProgramLibraryTick;
        Native.OpenSucceeded += RecordSuccessfulProgramLaunch;
    }

    internal void DetachProgramHistory()
    {
        if (_programHistoryAttached)
        {
            Native.OpenSucceeded -= RecordSuccessfulProgramLaunch;
            _programSaveTimer.Tick -= SaveProgramLibraryTick;
            _programHistoryAttached = false;
        }
        _programSaveTimer.Stop(); FlushProgramLibrary();
    }

    internal static bool CanSaveProgram(string path, ReferenceLaunch? launch = null)
        => ProgramReference(path, launch) != null;

    private static bool IsExecutableReference(string path)
        => new[] { ".exe", ".com", ".bat", ".cmd", ".appref-ms" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static ImportedReference? ProgramReference(string path, ReferenceLaunch? launch)
    {
        if (Native.IsWebAddress(path)) return null;
        if (IsExecutableReference(path)) return Directory.Exists(path) ? null : new(path, path, launch);
        if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var key = PathExtensions.Key(path); var info = new FileInfo(key);
            if (!info.Exists || key.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            ImportedReference? reference; ShortcutProgramCache? cached;
            lock (ProgramShortcutCacheLock) ProgramShortcutCache.TryGetValue(key, out cached);
            if (cached != null && cached.Modified == info.LastWriteTimeUtc && cached.Size == info.Length) reference = cached.Reference;
            else
            {
                var resolved = ShortcutReferences.Read(key);
                reference = IsExecutableReference(resolved.Path) ? resolved
                    : ShortcutReferences.ApplicationIdentity(key) != null ? new(key, key, new(Path.GetFileNameWithoutExtension(key))) : null;
                lock (ProgramShortcutCacheLock)
                {
                    if (ProgramShortcutCache.Count >= 512) ProgramShortcutCache.Clear();
                    ProgramShortcutCache[key] = new(info.LastWriteTimeUtc, info.Length, reference);
                }
            }
            if (reference == null || !File.Exists(reference.Path)) return null;
            var stored = reference.Launch ?? new ReferenceLaunch();
            // Ambiguous extra commands are excluded rather than inventing a launch
            // that could differ from the Shell invocation which actually ran.
            if (launch?.Arguments.Length > 0 && stored.Arguments.Length > 0 && launch.Arguments != stored.Arguments) return null;
            var canonical = new ReferenceLaunch(!string.IsNullOrWhiteSpace(launch?.Name) ? launch.Name : stored.Name,
                launch?.Arguments.Length > 0 ? launch.Arguments : stored.Arguments,
                launch?.WorkingDirectory.Length > 0 ? launch.WorkingDirectory : stored.WorkingDirectory,
                launch?.RunAsAdministrator == true || stored.RunAsAdministrator);
            return reference with { Launch = canonical };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    internal bool IsFavoriteProgram(string path, ReferenceLaunch? launch = null)
        => ProgramReference(path, launch) is { } reference && Programs.Find(reference.Path, reference.Launch)?.IsFavorite == true;

    internal void SetProgramFavorite(string path, ReferenceLaunch? launch, bool favorite, string? catalogId = null)
    {
        if (ProgramReference(path, launch) is not { } reference) return;
        Programs.SetFavorite(reference.Path, reference.Launch, favorite, catalogId);
        QueueProgramSave(); ProgramLibraryChanged?.Invoke();
    }

    internal void ToggleProgramFavorite(string path, ReferenceLaunch? launch = null)
    {
        if (ProgramReference(path, launch) is not { } reference) return;
        Programs.SetFavorite(reference.Path, reference.Launch, Programs.Find(reference.Path, reference.Launch)?.IsFavorite != true);
        QueueProgramSave(); ProgramLibraryChanged?.Invoke();
    }

    internal void ClearRecentPrograms()
    {
        Programs.ClearRecent(); QueueProgramSave(); ProgramLibraryChanged?.Invoke();
    }

    internal void RecordSuccessfulProgramLaunch(string path, ReferenceLaunch? launch)
    {
        if (_disposed) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(new Action(() => RecordSuccessfulProgramLaunch(path, launch))); return; }
        // A history failure must never turn a successful Shell launch into an error.
        try
        {
            if (ProgramReference(path, launch) is not { } reference) return;
            Programs.RecordSuccess(reference.Path, reference.Launch, DateTimeOffset.UtcNow); QueueProgramSave(); ProgramLibraryChanged?.Invoke();
        }
        catch (Exception ex) { Log(ex); }
    }

    private void QueueProgramSave() { _programLibraryDirty = true; _programSaveTimer.Stop(); _programSaveTimer.Start(); }
    private void SaveProgramLibraryTick(object? sender, EventArgs e) { _programSaveTimer.Stop(); FlushProgramLibrary(); }
    internal void FlushProgramLibrary()
    {
        if (!_programLibraryDirty || _programLibrary == null) return;
        if (_programLibraryReadFailed)
        {
            SetStatus(L.Text("Библиотека программ повреждена: новые записи не сохраняются, исходный файл сохранён."), refreshViews: false);
            return;
        }
        try { _programLibraryStore!.Save(_programLibrary); _programLibraryDirty = false; }
        catch (Exception ex) { Log(ex); SetStatus(L.Text("Не удалось сохранить библиотеку программ."), refreshViews: false); }
    }
}
