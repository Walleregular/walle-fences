using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Interop;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace WalleFences.App;

internal sealed partial class AppController : IDisposable
{
    internal Workspace Workspace { get; private set; }
    internal WorkspaceStore Store { get; }
    internal CpuTemperatureMonitor CpuTemperature { get; }
    internal HardwareSensorSetup HardwareSensors { get; }
    internal SystemMetricsMonitor SystemMetrics { get; }
    internal GpuMonitor Gpu { get; }
    internal readonly List<FenceWindow> Windows = [];
    internal ManagerWindow? Manager { get; private set; }
    internal bool IsPeek { get; private set; }
    internal bool Hidden { get; private set; }
    internal string Status { get; private set; } = L.Text("Готово");
    internal string HotkeyStatus { get; private set; } = "";
    internal string DesktopStatus => Native.DesktopHost() != 0 ? L.Text("Подключено к рабочему столу Explorer") : L.Text("Explorer недоступен: панели работают отдельными окнами");
    internal event Action? Changed;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly List<Workspace> _undo = [];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _shellTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private static DisplayArea[]? _displays;
    private readonly DispatcherTimer _pointerTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly HoverReveal _cornerReveal = new();
    private Forms.NotifyIcon? _tray;
    private HwndSource? _messages;
    private nint _mouseHook;
    private readonly Native.HookProc _mouseProcedure;
    private uint _lastClickTime;
    private Native.Point _lastClick;
    private nint _hiddenIconHandle;
    private bool _disposed, _organizing, _cornerNeedsLeave;
    private nint _lastDesktopHost;
    internal string DesktopPath => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    internal string PublicDesktopPath => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    internal AppController(string root)
    {
        Store = new(root); HardwareSensors = new(); CpuTemperature = new(HardwareSensors); SystemMetrics = new(); Gpu = new();
        HardwareSensors.Changed += HardwareSensorsChanged;
        Workspace = Store.Load(CreateInitialWorkspace);
        MigrateFirstRunPreferences();
        L.SetLanguage(Workspace.Preferences.Language); Status = L.Text("Готово");
        AttachProgramHistory();
        Ui.ApplyTheme(Workspace.Preferences);
        Displays(true);
        _mouseProcedure = MouseHook;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _refreshTimer.Tick += async (_, _) => { _refreshTimer.Stop(); await RefreshAsync(); };
        _shellTimer.Tick += (_, _) => CheckShell();
        _displayTimer.Tick += (_, _) => { _displayTimer.Stop(); RefreshDisplays(); };
        _pointerTimer.Tick += (_, _) => TrackPointer();
        _focusTimer.Tick += (_, _) => TickFocusTimers(DateTimeOffset.UtcNow);
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
    }

    internal void Start(bool background = false)
    {
        CreateMessageWindow();
        CreateTray();
        if (NeedsFirstRun)
        {
            SaveNow();
            ShowFirstRun();
            return;
        }
        if (Workspace.Preferences.StartCollapsed) foreach (var fence in Workspace.Fences) fence.RolledUp = true;
        Rebuild();
        UpdateIntegration(); _shellTimer.Start(); _pointerTimer.Start(); _focusTimer.Start();
        SaveNow();
        if (Store.LoadWarning != null) SetStatus(Store.LoadWarning);
        if (!background && Workspace.Preferences.ShowManagerOnStart) ShowManager();
    }
    internal void ShowManager()
    {
        if (NeedsFirstRun) { ShowFirstRun(); return; }
        if (Manager == null) { Manager = new(this); Manager.Closed += (_, _) => Manager = null; }
        Manager.Show(); if (Manager.WindowState == WindowState.Minimized) Manager.WindowState = WindowState.Normal; Manager.Activate(); SyncManagerLayer();
    }
    internal void SetStatus(string message, bool refreshViews = true)
    {
        Status = message;
        if (refreshViews) Changed?.Invoke();
        else Manager?.RefreshStatusOnly();
    }
    internal void Mutate(Action action, bool rebuild = true)
    {
        var previous = WorkspaceStore.Clone(Workspace);
        try
        {
            action(); WorkspaceStore.Validate(Workspace); WorkspaceActions.ValidateContentChanges(previous, Workspace);
            foreach (var fence in Workspace.Fences)
            {
                var before = previous.Fences.FirstOrDefault(f => f.Id == fence.Id);
                if (before != null && before.Placement == fence.Placement && (before.X != fence.X || before.Y != fence.Y || before.Width != fence.Width || before.Height != fence.Height || before.Edge != fence.Edge || before.EdgeOffset != fence.EdgeOffset || before.EdgeLength != fence.EdgeLength || before.EdgeSpanMode != fence.EdgeSpanMode || before.EdgeBlockCount != fence.EdgeBlockCount || before.EdgeBlockIndex != fence.EdgeBlockIndex)) DisplayLayout.Capture(fence, Displays());
            }
            ResolveEdgeCollisions(previous);
        }
        catch { Workspace = previous; Rebuild(); throw; }
        Remember(previous); if (rebuild) Rebuild(); ScheduleSave(); Changed?.Invoke();
    }
    private void Remember(Workspace previous) { _undo.Add(previous); if (_undo.Count > 30) _undo.RemoveAt(0); }
    internal bool CanUndo => _undo.Count > 0;
    internal void Undo()
    {
        if (_undo.Count == 0) { SetStatus(L.Text("Нет изменений для отмены")); return; }
        var previous = _undo[^1]; _undo.RemoveAt(_undo.Count - 1);
        Workspace = previous; Rebuild(); UpdateIntegration(); ScheduleSave(); SetStatus(L.Text("Изменение отменено"));
    }
    internal void ScheduleSave() { _saveTimer.Stop(); _saveTimer.Start(); }
    internal void SaveNow()
    {
        try { Store.Save(Workspace); }
        catch (Exception ex) { Log(ex); SetStatus(L.Text("Не удалось сохранить раскладку: ") + ex.Message); }
    }
    internal void Log(Exception ex)
    {
        try { File.AppendAllText(Path.Combine(Store.Root, "errors.log"), $"{DateTime.Now:O} {ex}\n"); } catch { }
    }
    internal void Rebuild()
    {
        Ui.ApplyTheme(Workspace.Preferences);
        RefreshLanguage();
        if (NeedsFirstRun) return;
        var previous = Windows.ToDictionary(w => w.Fence.Id);
        Windows.Clear();
        foreach (var fence in Workspace.Fences) DisplayLayout.Reconcile(fence, Displays(), Workspace.Preferences.RestoreMonitorPositions);
        EdgeCollisionLayout.Resolve(Workspace.Fences, Displays());
        foreach (var fence in Workspace.Fences.Where(f => f.PageId == Workspace.ActivePageId))
        {
            if (previous.TryGetValue(fence.Id, out var existing) && existing.CanReuse(fence)) { Windows.Add(existing); previous.Remove(fence.Id); existing.RefreshState(); if (Hidden && !IsPeek && !fence.KeepVisibleOnHide) existing.Hide(); else existing.Show(); continue; }
            var window = new FenceWindow(this, fence); Windows.Add(window);
            window.Show(); if (Hidden && !IsPeek && !fence.KeepVisibleOnHide) window.Hide();
            else window.ApplyLayer();
        }
        foreach (var obsolete in previous.Values) obsolete.Close();
        RebuildWatchers();
        RefreshFocusWidgets();
        ConfigureHotkeys();
        SyncManagerLayer();
    }
    internal void SetLanguage(string code)
    {
        var language = L.Normalize(code);
        if (Workspace.Preferences.Language == language && L.Language == language) return;
        Remember(WorkspaceStore.Clone(Workspace)); Workspace.Preferences.Language = language;
        Rebuild(); SaveNow(); SetStatus(L.Text("Язык интерфейса изменён"));
    }
    private void RefreshLanguage()
    {
        if (L.Language == Workspace.Preferences.Language) return;
        L.SetLanguage(Workspace.Preferences.Language); Status = L.Text("Готово");
        ConfigureHotkeys();
        if (_tray != null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); _tray = null; CreateTray(); }
        Manager?.RefreshLanguage();
    }
    internal static void ClampToScreen(Fence fence)
    {
        DisplayLayout.Clamp(fence, DisplayLayout.Select(fence, Displays()));
    }
    internal void RememberPlacement(Fence fence)
    {
        DisplayLayout.Capture(fence, Displays());
        if (fence.Edge == DockEdge.None) { ScheduleSave(); return; }
        var displays = Displays(); var display = DisplayLayout.Select(fence, displays);
        var neighbours = Workspace.Fences.Where(other => other.PageId == fence.PageId && other.Edge == fence.Edge
            && DisplayLayout.Select(other, displays).Id == display.Id).ToArray();
        var changed = EdgeCollisionLayout.Resolve(Workspace.Fences, Displays(), fence.Id);
        PersistEdgeCollisionPlacements(fence.Id, neighbours);
        ApplyEdgeCollisionWindows(neighbours.Concat(changed)); ScheduleSave();
    }
    internal static double GlobalScale => Forms.Screen.PrimaryScreen is { } primary ? SystemParameters.PrimaryScreenWidth / primary.Bounds.Width : 1;
    internal static DisplayArea[] Displays(bool refresh = false)
    {
        if (!refresh && _displays != null) return _displays;
        var scale = GlobalScale;
        var result = Forms.Screen.AllScreens.OrderByDescending(s => s.Primary).ThenBy(s => s.DeviceName, StringComparer.Ordinal).Select(screen =>
        {
            var (id, name) = Native.DisplayIdentity(screen.DeviceName);
            var area = new Rect(screen.WorkingArea.X * scale, screen.WorkingArea.Y * scale, screen.WorkingArea.Width * scale, screen.WorkingArea.Height * scale);
            return new DisplayArea(id, name, area.X, area.Y, area.Width, area.Height, Native.MonitorScale(area) * scale, screen.Primary);
        }).ToArray();
        if (result.Length == 0) { var area = SystemParameters.WorkArea; result = [new("primary", "Display", area.X, area.Y, area.Width, area.Height, 1, true)]; }
        return _displays = result;
    }
    internal static Rect[] MonitorAreas() => Displays().Select(d => new Rect(d.X, d.Y, d.Width, d.Height)).ToArray();
    internal void MoveToMonitor(Fence fence, DisplayArea display) => Mutate(() => DisplayLayout.MoveToDisplay(fence, display, Displays()));
    private void QueueDisplayRefresh() => Application.Current.Dispatcher.BeginInvoke(() => { if (_disposed) return; _displayTimer.Stop(); _displayTimer.Start(); });
    private void TrackPointer()
    {
        if (_disposed || !Native.GetCursorPos(out var pointer)) return;
        TrackPointer(pointer);
    }
    internal void TrackPointer(Native.Point pointer)
    {
        UpdateGameModeState();
        var target = Windows.FirstOrDefault(w => !AutomaticRevealBlocked(w.Fence) && w.IsVisible && w.RevealIsOpen && w.ContainsPointer(pointer, 6));
        target ??= Windows.Where(w => !AutomaticRevealBlocked(w.Fence)).Select(w => (Window: w, Distance: w.ProximityDistance(pointer))).Where(w => double.IsFinite(w.Distance)).OrderBy(w => w.Distance).Select(w => w.Window).FirstOrDefault();
        foreach (var window in Windows)
        {
            var blocked = AutomaticRevealBlocked(window.Fence);
            window.ApplyGameModeGuard(blocked); window.TrackPointer(pointer, window == target && !blocked);
        }
        var inCorner = Forms.Screen.AllScreens.Any(d => pointer.X >= d.Bounds.Right - 5 && pointer.X <= d.Bounds.Right && pointer.Y >= d.Bounds.Bottom - 5 && pointer.Y <= d.Bounds.Bottom);
        if (!inCorner) _cornerNeedsLeave = false;
        if (!Workspace.Preferences.PeekHotCorner || IsPeek || Hidden || _cornerNeedsLeave || AutomaticRevealBlocked(pointer)) { _cornerReveal.Reset(); return; }
        if (_cornerReveal.Tick(Environment.TickCount64, inCorner, inCorner, false, 600, 500) && _cornerReveal.IsOpen) { _cornerNeedsLeave = true; Peek(); }
    }
    private void RefreshDisplays()
    {
        var before = Displays(); var next = Displays(true);
        if (!before.SequenceEqual(next)) { Rebuild(); ScheduleSave(); Changed?.Invoke(); }
    }
    internal void Peek()
    {
        IsPeek = !IsPeek;
        ConfigureMouseHook();
        foreach (var window in Windows)
        {
            if (IsPeek) { if (!window.IsVisible) window.Show(); window.RefreshPeekReveal(); window.ApplyChameleon(); }
            else { window.RefreshPeekReveal(); if (Hidden && !window.Fence.KeepVisibleOnHide) window.Hide(); window.ApplyChameleon(); }
        }
        if (IsPeek && Windows.Count > 0 && !HasVisibleManager) Windows[0].Activate();
        SyncManagerLayer();
        SetStatus(IsPeek ? L.Text("Быстрый доступ открыт · Esc — закрыть") : L.Text("Быстрый доступ закрыт"), refreshViews: false);
    }
    internal void ToggleHidden()
    {
        if (IsPeek) Peek(); Hidden = !Hidden;
        foreach (var window in Windows) { if (Hidden && !window.Fence.KeepVisibleOnHide) window.Hide(); else { window.Show(); window.ApplyLayer(); } }
        SetStatus(Hidden ? L.Text("Группы скрыты") : L.Text("Группы показаны"));
    }
    internal void CollapseAll(bool collapsed)
    {
        Mutate(() =>
        {
            foreach (var fence in Workspace.Fences)
            {
                fence.RolledUp = collapsed;
                if (!collapsed) foreach (var tab in fence.Tabs) tab.Collapsed = false;
            }
        });
        SetStatus(collapsed ? L.Text("Все группы свёрнуты до верхних вкладок") : L.Text("Все группы развёрнуты"));
    }
    internal void TopShelf()
    {
        Mutate(() =>
        {
            foreach (var fence in Workspace.Fences.Where(f => f.PageId == Workspace.ActivePageId)) { fence.Edge = DockEdge.None; fence.DisplayMode = FenceDisplayMode.Minimal; fence.RolledUp = true; fence.HoverExpand = true; }
            DesktopOrganizer.ArrangeTop(Workspace, Displays());
        });
        SetStatus(L.Text("Группы собраны в тонкую полосу сверху"));
    }
    internal void DockGroups(DockEdge edge, double thickness = 4, double proximity = 12, int openDelay = 180, int closeDelay = 500, bool preserveActivationSettings = false)
    {
        Mutate(() =>
        {
            var existing = preserveActivationSettings
                ? Workspace.Fences.Where(f => f.PageId == Workspace.ActivePageId)
                    .Select(f => (Fence: f, Distance: f.ActivationDistance, Open: f.OpenDelayMs, Close: f.CloseDelayMs)).ToArray()
                : Array.Empty<(Fence Fence, double Distance, int Open, int Close)>();
            DesktopOrganizer.ArrangeEdge(Workspace, Displays(), edge, thickness, proximity, openDelay, closeDelay);
            foreach (var previous in existing)
            {
                previous.Fence.ActivationDistance = previous.Distance;
                previous.Fence.OpenDelayMs = previous.Open;
                previous.Fence.CloseDelayMs = previous.Close;
            }
        });
        SetStatus(L.Text("Полоски у края настроены"));
    }
    internal void ArrangeEdgeBlocks(DockEdge edge, int blockCount, DisplayArea display, IReadOnlyList<string> ids, bool createMissing = false)
    {
        if (edge == DockEdge.None || !Enum.IsDefined(edge)) throw new ArgumentException(L.Text("Выберите край экрана."), nameof(edge));
        blockCount = Math.Clamp(blockCount, 1, 12);
        var displays = Displays();
        var target = displays.FirstOrDefault(d => d.Id.Equals(display.Id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(L.Text("Выбранный монитор недоступен."));
        var groups = ids.Distinct(StringComparer.Ordinal).Select(id => Workspace.Fences.FirstOrDefault(f => f.Id == id && f.PageId == Workspace.ActivePageId))
            .Where(f => f != null).Cast<Fence>().ToList();
        if (groups.Count > blockCount) throw new InvalidOperationException(L.Text("Выберите не больше групп, чем блоков."));
        if (groups.Count == 0 && !createMissing) return;
        Mutate(() =>
        {
            if (createMissing)
            {
                while (groups.Count < blockCount)
                {
                    var group = new Fence { Name = L.Format("Блок {0}", groups.Count + 1), PageId = Workspace.ActivePageId,
                        EdgeThickness = 3, HoverExpand = true, Layer = FenceLayer.AlwaysOnTop, DisplayMode = FenceDisplayMode.Compact };
                    group.ActiveTabId = group.Tabs[0].Id;
                    Workspace.Fences.Add(group); groups.Add(group);
                }
            }
            for (var index = 0; index < groups.Count; index++)
            {
                var group = groups[index];
                group.Edge = edge; group.EdgeSpanMode = EdgeSpanMode.EqualBlocks;
                group.EdgeBlockCount = blockCount; group.EdgeBlockIndex = index;
                group.OpeningSize = OpeningSize.Custom;
                DisplayLayout.Capture(group, displays, target);
            }
        });
        SaveNow(); SetStatus(L.Format("Край разделён на {0} блоков", blockCount));
    }
    internal async Task RedistributeDesktopAsync(bool topShelf = false, bool hideDesktopIcons = false)
    {
        if (_organizing) throw new InvalidOperationException(L.Text("Дождитесь завершения сортировки."));
        _organizing = true;
        SetStatus(L.Text("Распределение значков рабочего стола…"));
        try
        {
            var hidden = Workspace.Preferences.ShowHiddenFiles;
            var entries = await Task.Run(() => FileCatalog.ReadFolder(DesktopPath, hidden).Concat(FileCatalog.ReadFolder(PublicDesktopPath, hidden)).ToList());
            Store.Snapshot(Workspace, L.Text("До распределения рабочего стола"));
            DesktopOrganizationResult result = new(0, 0);
            Mutate(() =>
            {
                result = DesktopOrganizer.Redistribute(Workspace, entries, entry => ShortcutReferences.Read(entry.Path));
                if (hideDesktopIcons) Workspace.Preferences.HideDesktopIcons = true;
                if (topShelf)
                {
                    foreach (var fence in Workspace.Fences.Where(f => f.PageId == Workspace.ActivePageId)) { fence.DisplayMode = FenceDisplayMode.Minimal; fence.Layer = FenceLayer.AlwaysOnTop; fence.RolledUp = true; fence.HoverExpand = true; }
                    DesktopOrganizer.ArrangeTop(Workspace, Displays());
                }
            });
            SaveNow(); UpdateIntegration(); SetStatus(L.Format("Распределено значков: {0} · групп: {1}", result.Items, result.Groups));
        }
        finally { _organizing = false; }
    }
    internal void SwitchPage(string id)
    {
        if (id == Workspace.ActivePageId || !Workspace.Pages.Any(p => p.Id == id)) return;
        if (IsPeek) Peek();
        Workspace.ActivePageId = id; Rebuild(); ScheduleSave(); SetStatus(L.Text("Набор") + " · " + Workspace.Pages.First(p => p.Id == id).Name);
    }
    internal void NextPage(int direction)
    {
        var index = Workspace.Pages.FindIndex(p => p.Id == Workspace.ActivePageId);
        SwitchPage(Workspace.Pages[(index + direction + Workspace.Pages.Count) % Workspace.Pages.Count].Id);
    }
    internal void Snapshot(string name) { Store.Snapshot(Workspace, name); SetStatus(L.Text("Снимок раскладки сохранён")); }
    internal void Merge(Fence source, Fence target)
    {
        if (source == target || !Workspace.Fences.Contains(source) || !Workspace.Fences.Contains(target)) return;
        WorkspaceActions.RequireEditable(source); WorkspaceActions.RequireEditable(target);
        Mutate(() =>
        {
            if (source.Tabs.Count == 1 && L.Codes.Any(code => source.Tabs[0].Name == L.Catalog(code)["Файлы"])) source.Tabs[0].Name = source.Name;
            foreach (var tab in source.Tabs) { tab.Color ??= source.Accent; target.Tabs.Add(tab); }
            foreach (var rule in Workspace.Rules.Where(r => r.FenceId == source.Id)) rule.FenceId = target.Id;
            Workspace.Fences.Remove(source);
        });
    }
    internal void DetachTab(Fence fence, FenceTab tab, Point? position = null)
    {
        if (fence.Tabs.Count < 2) return;
        WorkspaceActions.RequireEditable(fence);
        Mutate(() =>
        {
            fence.Tabs.Remove(tab);
            var group = new Fence { Name = tab.Name, Icon = fence.Icon, PageId = fence.PageId, Color = fence.Color, Accent = tab.Color ?? fence.Accent, X = fence.X + 35, Y = fence.Y + 35, Width = fence.Width, Height = fence.Height, Opacity = fence.Opacity, IconSize = fence.IconSize, Sort = fence.Sort, TintIcons = fence.TintIcons, ShowLabels = fence.ShowLabels, DisplayMode = fence.DisplayMode, Layer = fence.Layer, HoverExpand = fence.HoverExpand, Tabs = [tab], ActiveTabId = tab.Id };
            group.TabStyle = fence.TabStyle; group.TabVisibility = fence.TabVisibility; group.IconSpacing = fence.IconSpacing; group.LabelFontSize = fence.LabelFontSize; group.RevealTrigger = fence.RevealTrigger; group.OpenDelayMs = fence.OpenDelayMs; group.CloseDelayMs = fence.CloseDelayMs; group.CloseAfterLaunch = fence.CloseAfterLaunch;
            group.FileView = fence.FileView; group.SurfaceStyle = fence.SurfaceStyle; group.CornerRadius = fence.CornerRadius; group.OpeningSize = fence.OpeningSize;
            group.PortalFoldersInExplorer = fence.PortalFoldersInExplorer;
            if (position is { } point) { group.X = point.X; group.Y = point.Y; }
            Workspace.Fences.Add(group); foreach (var rule in Workspace.Rules.Where(r => r.TabId == tab.Id)) rule.FenceId = group.Id;
        });
    }
    internal void Restore(string path)
    {
        var next = WorkspaceStore.Parse(File.ReadAllText(path));
        Store.Snapshot(Workspace, L.Text("Перед восстановлением")); Remember(WorkspaceStore.Clone(Workspace));
        Workspace = next; Rebuild(); UpdateIntegration(); SaveNow(); SetStatus(L.Text("Раскладка восстановлена"));
    }
    internal async Task<int> OrganizeAsync(bool force = false)
    {
        if (_organizing || AutomaticSortingPaused || (!force && !Workspace.Preferences.AutoOrganize)) return 0;
        _organizing = true;
        try
        {
            var hidden = Workspace.Preferences.ShowHiddenFiles;
            var entries = await Task.Run(() => FileCatalog.ReadFolder(DesktopPath, hidden).Concat(FileCatalog.ReadFolder(PublicDesktopPath, hidden)).ToList());
            if (AutomaticSortingPaused) return 0;
            var count = RuleEngine.Organize(Workspace, entries, resolve: entry => ShortcutReferences.Read(entry.Path));
            if (count > 0) { ScheduleSave(); foreach (var window in Windows) window.Reload(); }
            if (force) SetStatus(L.Format("Добавлено ссылок: {0}", count)); return count;
        }
        catch (Exception ex) { Log(ex); SetStatus(L.Text("Ошибка сортировки: ") + ex.Message); return 0; }
        finally { _organizing = false; }
    }
    private async Task RefreshAsync()
    {
        await OrganizeAsync(); foreach (var window in Windows.ToArray()) window.Reload(); Changed?.Invoke();
    }
    private void RebuildWatchers()
    {
        foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear();
        var paths = Workspace.Fences.SelectMany(f => f.Tabs).Select(t => t.PortalPath).Concat(Workspace.Fences.SelectMany(f => f.Tabs).Where(t => string.IsNullOrEmpty(t.PortalPath)).SelectMany(t => t.Items).Select(Path.GetDirectoryName)).Concat([DesktopPath, PublicDesktopPath]).Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            try
            {
                var watcher = new FileSystemWatcher(path!) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size, IncludeSubdirectories = false };
                FileSystemEventHandler changed = (_, _) => QueueRefresh();
                watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed;
                watcher.Renamed += (_, args) => Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    foreach (var tab in Workspace.Fences.SelectMany(f => f.Tabs).Where(t => string.IsNullOrEmpty(t.PortalPath)))
                        if (FileCatalog.RenameReferences(tab, args.OldFullPath, args.FullPath) > 0) ScheduleSave();
                    QueueRefresh();
                });
                watcher.Error += (_, _) => QueueRefresh(); watcher.EnableRaisingEvents = true; _watchers.Add(watcher);
            }
            catch (Exception ex) { Log(ex); }
        }
    }
    internal void QueueRefresh()
    {
        Application.Current.Dispatcher.BeginInvoke(() => { if (_disposed) return; _refreshTimer.Stop(); _refreshTimer.Start(); });
    }
    internal void UpdateIntegration()
    {
        if (NeedsFirstRun) return;
        ConfigureHotkeys();
        ConfigureMouseHook();
        UpdateDesktopIcons(); foreach (var window in Windows) window.ApplyChameleon();
    }
    private void CheckShell()
    {
        RefreshDisplays();
        var host = Native.DesktopHost();
        if (host != _lastDesktopHost) { _lastDesktopHost = host; foreach (var window in Windows.Where(w => w.IsVisible)) window.ApplyLayer(); Changed?.Invoke(); }
        UpdateDesktopIcons();
        if (host == 0 && !IsPeek) foreach (var window in Windows.Where(w => w.IsVisible && !w.ShouldFloat)) Native.SetWindowPos(new WindowInteropHelper(window).Handle, Native.HwndBottom, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate);
    }
    private void DisplayChanged(object? sender, EventArgs e) => QueueDisplayRefresh();
    private void PreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => QueueDisplayRefresh();
    private void UpdateDesktopIcons()
    {
        var icons = Native.DesktopIcons();
        if (Workspace.Preferences.HideDesktopIcons)
        {
            if (icons != 0 && icons != _hiddenIconHandle && Native.IsWindowVisible(icons)) { Native.ShowWindow(icons, 0); _hiddenIconHandle = icons; }
        }
        else RestoreDesktopIcons();
    }
    private void RestoreDesktopIcons() { if (_hiddenIconHandle != 0 && Native.IsWindow(_hiddenIconHandle)) Native.ShowWindow(_hiddenIconHandle, 5); _hiddenIconHandle = 0; }
    private void ConfigureMouseHook()
    {
        if (NeedsFirstRun) return;
        var needed = Workspace.Preferences.DoubleClickHide || Workspace.Preferences.PageWheel || IsPeek;
        if (needed && _mouseHook == 0) _mouseHook = Native.SetWindowsHookEx(14, _mouseProcedure, Native.GetModuleHandle(null), 0);
        if (!needed && _mouseHook != 0) { Native.UnhookWindowsHookEx(_mouseHook); _mouseHook = 0; }
    }
    private nint MouseHook(int code, nint wparam, nint lparam)
    {
        if (code >= 0 && wparam == 0x020A && Workspace.Preferences.PageWheel && Native.ControlPressed())
        {
            var wheel = Marshal.PtrToStructure<Native.MouseData>(lparam);
            if (Native.IsDesktopPoint(wheel.Point)) { var delta = unchecked((short)(wheel.Mouse >> 16)); Application.Current.Dispatcher.BeginInvoke(() => { if (!_disposed) NextPage(delta > 0 ? -1 : 1); }); return 1; }
        }
        if (code >= 0 && wparam is 0x201 or 0x204 && IsPeek)
        {
            var click = Marshal.PtrToStructure<Native.MouseData>(lparam);
            Native.GetWindowThreadProcessId(Native.WindowFromPoint(click.Point), out var targetProcess);
            if (targetProcess != Environment.ProcessId) Application.Current.Dispatcher.BeginInvoke(() => { if (!_disposed && IsPeek) Peek(); });
        }
        if (code >= 0 && wparam == 0x202)
        {
            var data = Marshal.PtrToStructure<Native.MouseData>(lparam);
            if (data.Time - _lastClickTime <= Native.GetDoubleClickTime() && Math.Abs(data.Point.X - _lastClick.X) < 5 && Math.Abs(data.Point.Y - _lastClick.Y) < 5)
            {
                _lastClickTime = 0;
                Application.Current.Dispatcher.BeginInvoke(() => { if (!_disposed && Workspace.Preferences.DoubleClickHide && Native.IsDesktopPoint(data.Point)) ToggleHidden(); });
            }
            else { _lastClickTime = data.Time; _lastClick = data.Point; }
        }
        return Native.CallNextHookEx(_mouseHook, code, wparam, lparam);
    }
    private void CreateMessageWindow()
    {
        _messages = new HwndSource(new HwndSourceParameters("Walle Fences messages") { Width = 0, Height = 0, WindowStyle = 0 });
        _messages.AddHook((nint hwnd, int msg, nint wp, nint lp, ref bool handled) =>
        {
            if (msg is 0x007E or 0x001A) QueueDisplayRefresh();
            if (msg == 0x0312)
            {
                handled = true;
                if (_hotkeyActions.TryGetValue((int)wp, out var action)) Ui.Guard(action);
            }
            return 0;
        });
        ConfigureHotkeys();
    }
    private void ConfigureHotkeys()
    {
        if (_messages == null || _capturingHotkey || NeedsFirstRun) return;
        var prefs = Workspace.Preferences;
        var bindings = new List<(string Name, string Key, Action Action)>
        {
            (L.Text("Быстрый доступ"), prefs.PeekHotkey, Peek),
            (L.Text("Показать / скрыть группы"), prefs.HideHotkey, ToggleHidden),
            (L.Text("Предыдущая страница"), prefs.PreviousPageHotkey, () => NextPage(-1)),
            (L.Text("Следующая страница"), prefs.NextPageHotkey, () => NextPage(1)),
            (L.Text("Настройки"), prefs.ManagerHotkey, ShowManager),
            (L.Text("Поиск и команды"), prefs.SearchHotkey, ShowSearch)
        };
        bindings.AddRange(Workspace.Fences.Where(f => !string.IsNullOrWhiteSpace(f.Hotkey)).Select(f => (f.Name, f.Hotkey, (Action)(() => ToggleGroupByHotkey(f)))));
        var signature = System.Text.Json.JsonSerializer.Serialize(new { Language = L.Language, Keys = bindings.Select(b => new { b.Name, b.Key }), Fences = Workspace.Fences.Select(f => f.Id) });
        // Refresh callbacks even when the gesture stays the same (undo/import replaces model objects).
        if (_hotkeySignature == signature)
        {
            foreach (var id in _hotkeyActions.Keys.ToArray()) _hotkeyActions[id] = bindings[id - 1].Action;
            return;
        }
        _hotkeySignature = signature;
        foreach (var id in _hotkeyActions.Keys) Native.UnregisterHotKey(_messages.Handle, id);
        _hotkeyActions.Clear();
        var conflicts = new List<string>(); var used = new HashSet<HotkeyGesture>();
        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i]; if (string.IsNullOrWhiteSpace(binding.Key)) continue;
            if (!HotkeyGesture.TryParse(binding.Key, out var gesture) || !used.Add(gesture) || !Native.RegisterHotKey(_messages.Handle, i + 1, gesture.Modifiers | 0x4000, gesture.Key))
            { conflicts.Add(binding.Name + " · " + binding.Key); continue; }
            _hotkeyActions[i + 1] = binding.Action;
        }
        HotkeyConflicts = conflicts;
        HotkeyStatus = conflicts.Count == 0 ? L.Text("Горячие клавиши зарегистрированы") : L.Text("Не удалось зарегистрировать: ") + string.Join("; ", conflicts);
    }
    private void CreateTray()
    {
        var menu = BuildTrayMenu();
        _tray = new Forms.NotifyIcon { Text = L.Text("Walle Fences · организация рабочего стола"), ContextMenuStrip = menu, Visible = true, Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application };
        _tray.DoubleClick += (_, _) => ShowManager();
    }
    internal Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip { ImageScalingSize = new System.Drawing.Size(20, 20), ShowCheckMargin = true, ShowImageMargin = true };
        var images = new List<System.Drawing.Image>();
        System.Drawing.Image Glyph(string id)
        {
            var color = System.Drawing.SystemColors.MenuText;
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawImage(Brand.Icon(id, brush, 20).Source, new Rect(0, 0, 20, 20));
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(40, 40, 192, 192, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
            using var original = System.Drawing.Image.FromStream(stream);
            var image = new System.Drawing.Bitmap(original); images.Add(image); return image;
        }
        void Add(string text, string id, Action action) => menu.Items.Add(text, Glyph(id), (_, _) => action());
        menu.Disposed += (_, _) => { foreach (var image in images) image.Dispose(); };
        if (NeedsFirstRun)
        {
            Add(L.Text("Продолжить настройку"), "settings", ShowFirstRun);
            menu.Items.Add(new Forms.ToolStripSeparator());
            Add(L.Text("Выход"), "exit", () => Application.Current.Shutdown());
            return menu;
        }
        Add(L.Text("Настройки Walle Fences"), "settings", ShowManager);
        Add(L.Text("Быстрый доступ"), "peek", Peek);
        var showIcon = Glyph("eye"); var hideIcon = Glyph("eye-off");
        var visibility = menu.Items.Add(L.Text("Показать / скрыть группы"), Hidden ? showIcon : hideIcon, (_, _) => ToggleHidden());
        menu.Opening += (_, _) => visibility.Image = Hidden ? showIcon : hideIcon;
        Add(L.Text("Свернуть все группы"), "collapse", () => CollapseAll(true));
        Add(L.Text("Развернуть все группы"), "expand", () => CollapseAll(false));
        var startup = new Forms.ToolStripMenuItem(L.Text("Автозагрузка с Windows")) { CheckOnClick = false, Image = Glyph("startup") };
        startup.Click += (_, _) => Ui.Guard(() => SetAutoStart(!AutoStart));
        menu.Opening += (_, _) => { try { startup.Enabled = true; startup.Checked = AutoStart; startup.Text = L.Text("Автозагрузка: ") + (startup.Checked ? L.Text("включена") : L.Text("выключена")); } catch { startup.Enabled = false; startup.Text = L.Text("Автозагрузка: недоступна"); } };
        menu.Items.Add(startup);
        Add(L.Text("Нарисовать новую группу"), "draw", () => DrawFenceWindow.Draw(this));
        var sets = new Forms.ToolStripMenuItem(L.Text("Наборы")) { Image = Glyph("pages") }; menu.Items.Add(sets);
        menu.Opening += (_, _) =>
        {
            foreach (Forms.ToolStripItem old in sets.DropDownItems.Cast<Forms.ToolStripItem>().ToArray()) old.Dispose();
            sets.DropDownItems.Clear();
            foreach (var page in Workspace.Pages)
            {
                var item = new Forms.ToolStripMenuItem(page.Name) { Checked = page.Id == Workspace.ActivePageId };
                item.Click += (_, _) => SwitchPage(page.Id); sets.DropDownItems.Add(item);
            }
        };
        Add(L.Text("Следующий набор"), "next", () => NextPage(1));
        Add(L.Text("Сохранить снимок"), "snapshots", () => Ui.Guard(() => Snapshot(L.Text("Быстрый снимок"))));
        menu.Items.Add(new Forms.ToolStripSeparator()); Add(L.Text("Выход"), "exit", () => Application.Current.Shutdown());
        return menu;
    }
    internal bool AutoStart
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("WalleFences") is string command && !string.IsNullOrWhiteSpace(command);
        }
        set
        {
            const string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            if (value)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrEmpty(executable) || !File.Exists(executable) || !Path.GetFileName(executable).Equals("WalleFences.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L.Text("Автозагрузку можно включить в собранном приложении WalleFences.exe."));
                var command = WindowsCommandLine.StartupCommand(executable, Store.Root);
                if (command.Length > 260) throw new InvalidOperationException(L.Text("Путь к приложению или данным слишком длинный для автозагрузки. Используйте более короткую папку."));
                using var key = Registry.CurrentUser.CreateSubKey(registryPath);
                key.SetValue("WalleFences", command, RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(registryPath, true);
                key?.DeleteValue("WalleFences", false);
            }
        }
    }
    internal void SetAutoStart(bool enabled)
    {
        AutoStart = enabled;
        if (AutoStart != enabled) throw new InvalidOperationException(L.Text("Windows не сохранила настройку автозагрузки."));
        SetStatus(enabled ? L.Text("Автозагрузка включена") : L.Text("Автозагрузка выключена"));
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _saveTimer.Stop(); _refreshTimer.Stop(); _shellTimer.Stop(); _displayTimer.Stop(); _pointerTimer.Stop(); _focusTimer.Stop();
        DetachProgramHistory();
        SystemEvents.DisplaySettingsChanged -= DisplayChanged; SystemEvents.UserPreferenceChanged -= PreferenceChanged; SaveNow(); RestoreDesktopIcons();
        if (_mouseHook != 0) Native.UnhookWindowsHookEx(_mouseHook);
        foreach (var watcher in _watchers) watcher.Dispose();
        if (_messages != null) { foreach (var id in _hotkeyActions.Keys) Native.UnregisterHotKey(_messages.Handle, id); _messages.Dispose(); }
        _searchWindow?.Close();
        _firstRunWindow?.Close();
        HardwareSensors.Changed -= HardwareSensorsChanged;
        CpuTemperature.Dispose(); HardwareSensors.Dispose();
        SystemMetrics.Dispose(); Gpu.Dispose();
        _tray?.Dispose();
        foreach (var window in Windows.ToArray()) window.Close(); Manager?.Close();
    }
}

internal static class KnownFolders
{
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
    internal static string Downloads()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(id, 0, 0, out var ptr) == 0) { try { return Marshal.PtrToStringUni(ptr)!; } finally { Marshal.FreeCoTaskMem(ptr); } }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
