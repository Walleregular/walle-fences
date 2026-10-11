using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WalleFences.Core;

public sealed partial class WorkspaceStore
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Root { get; }
    public string ConfigPath => Path.Combine(Root, "workspace.json");
    public string SnapshotDirectory => Path.Combine(Root, "snapshots");
    private bool _backupLoaded;
    public string? LoadWarning => _backupLoaded ? L.Text("Основная раскладка повреждена. ") + L.Text("Загружена резервная копия.") : null;
    public WorkspaceStore(string root) { Root = Path.GetFullPath(root); Directory.CreateDirectory(Root); }

    public Workspace Load(Func<Workspace> initial)
    {
        _backupLoaded = false;
        if (!File.Exists(ConfigPath)) return Validate(initial());
        try { return Parse(File.ReadAllText(ConfigPath)); }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or ArgumentException)
        {
            var backup = ConfigPath + ".bak";
            if (File.Exists(backup))
            {
                try { var result = Parse(File.ReadAllText(backup)); _backupLoaded = true; return result; }
                catch (Exception backupEx) when (backupEx is JsonException or IOException or InvalidDataException or ArgumentException) { }
            }
            throw new InvalidDataException(L.Text("Не удалось прочитать раскладку и резервную копию. Исходные файлы сохранены. Восстановите снимок или укажите другой --data-dir."), ex);
        }
    }

    public void Save(Workspace workspace)
    {
        Validate(workspace);
        AtomicWrite(ConfigPath, JsonSerializer.Serialize(workspace, JsonOptions));
    }

    private static void AtomicWrite(string path, string text)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream)) { writer.Write(text); writer.Flush(); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public string Snapshot(Workspace workspace, string name)
    {
        Directory.CreateDirectory(SnapshotDirectory);
        var clean = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        if (clean.Length == 0) clean = L.Text("Раскладка");
        if (clean.Length > 64) clean = clean[..64];
        var path = Path.Combine(SnapshotDirectory, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{clean}_{Guid.NewGuid().ToString("N")[..6]}.json");
        AtomicWrite(path, JsonSerializer.Serialize(Validate(workspace), JsonOptions));
        return path;
    }

    public IEnumerable<string> Snapshots() => Directory.Exists(SnapshotDirectory) ? Directory.GetFiles(SnapshotDirectory, "*.json").OrderByDescending(Path.GetFileName) : [];
    public static Workspace Parse(string json)
    {
        if (json.Length > 16 * 1024 * 1024) throw new InvalidDataException(L.Text("Файл раскладки превышает 16 МБ."));
        var workspace = JsonSerializer.Deserialize<Workspace>(json, JsonOptions) ?? throw new InvalidDataException(L.Text("Пустая раскладка."));
        return Validate(workspace);
    }
    public static Workspace Clone(Workspace workspace) => Parse(JsonSerializer.Serialize(workspace, JsonOptions));

    public static Workspace Validate(Workspace workspace)
    {
        if (workspace.SchemaVersion != 1) throw new InvalidDataException(L.Text("Неподдерживаемая версия раскладки."));
        if (workspace.Pages == null || workspace.Fences == null || workspace.Rules == null || workspace.Preferences == null) throw new InvalidDataException(L.Text("В раскладке отсутствуют обязательные разделы."));
        if (workspace.Pages.Count == 0) workspace.Pages.Add(new() { Id = "home", Name = L.Text("Основной") });
        if (workspace.Pages.Count > 100 || workspace.Fences.Count > 500 || workspace.Rules.Count > 1000) throw new InvalidDataException(L.Text("Слишком много объектов в раскладке."));
        if (workspace.Pages.Any(p => p is null) || workspace.Fences.Any(f => f is null) || workspace.Rules.Any(r => r is null)) throw new InvalidDataException(L.Text("Некорректный объект в раскладке."));
        EnsureUnique(workspace.Pages.Select(p => p.Id)); EnsureUnique(workspace.Fences.Select(f => f.Id)); EnsureUnique(workspace.Rules.Select(r => r.Id));
        foreach (var page in workspace.Pages) page.Name = Label(page.Name, L.Text("Страница"));
        if (!workspace.Pages.Any(p => p.Id == workspace.ActivePageId)) workspace.ActivePageId = workspace.Pages[0].Id;
        var tabIds = new HashSet<string>();
        foreach (var fence in workspace.Fences)
        {
            fence.Name = Label(fence.Name, L.Text("Группа"));
            if (!Enum.IsDefined(fence.Icon)) fence.Icon = GroupIcon.Automatic;
            if (!workspace.Pages.Any(p => p.Id == fence.PageId)) fence.PageId = workspace.Pages[0].Id;
            fence.Width = Clamp(fence.Width, 200, 4000, 330); fence.Height = Clamp(fence.Height, 130, 4000, 300);
            fence.X = Clamp(fence.X, -32000, 32000, 40); fence.Y = Clamp(fence.Y, -32000, 32000, 60);
            if (fence.Placement is { } placement)
            {
                fence.Placement = string.IsNullOrWhiteSpace(placement.MonitorId) || placement.MonitorId.Length > 1024 ? null : placement with
                {
                    OffsetX = Clamp(placement.OffsetX, -32000, 32000, 0), OffsetY = Clamp(placement.OffsetY, -32000, 32000, 0),
                    Width = Clamp(placement.Width, 200, 4000, 330), Height = Clamp(placement.Height, 130, 4000, 300)
                };
            }
            fence.Opacity = Clamp(fence.Opacity, .15, 1, .84); fence.IconSize = Math.Clamp(fence.IconSize, 24, 96);
            if (!HexColor().IsMatch(fence.Color ?? "")) fence.Color = "#1D2021";
            if (!HexColor().IsMatch(fence.Accent ?? "")) fence.Accent = "#A0E9C0";
            if (!Enum.IsDefined(fence.Sort)) fence.Sort = SortOrder.Manual;
            if (!Enum.IsDefined(fence.DisplayMode)) fence.DisplayMode = FenceDisplayMode.Standard;
            if (!Enum.IsDefined(fence.Layer)) fence.Layer = FenceLayer.Desktop;
            if (!Enum.IsDefined(fence.Edge)) fence.Edge = DockEdge.None;
            if (!Enum.IsDefined(fence.EdgeSpanMode)) fence.EdgeSpanMode = EdgeSpanMode.Custom;
            fence.EdgeBlockCount = Math.Clamp(fence.EdgeBlockCount, 1, 12);
            fence.EdgeBlockIndex = Math.Clamp(fence.EdgeBlockIndex, 0, fence.EdgeBlockCount - 1);
            if (!Enum.IsDefined(fence.RevealTrigger)) fence.RevealTrigger = RevealTrigger.Proximity;
            if (!Enum.IsDefined(fence.TabStyle)) fence.TabStyle = TabStyle.Rounded;
            if (!Enum.IsDefined(fence.TabVisibility)) fence.TabVisibility = TabVisibility.Auto;
            fence.EdgeThickness = Clamp(fence.EdgeThickness, 2, 12, 4); fence.EdgeLength = Clamp(fence.EdgeLength, 32, 8000, 160);
            fence.EdgeOffset = Clamp(fence.EdgeOffset, 0, 8000, 16); fence.ActivationDistance = Clamp(fence.ActivationDistance, 0, 80, 12);
            fence.OpenDelayMs = Math.Clamp(fence.OpenDelayMs, 0, 2000); fence.CloseDelayMs = Math.Clamp(fence.CloseDelayMs, 100, 5000);
            fence.IconSpacing = Math.Clamp(fence.IconSpacing, 0, 24); fence.LabelFontSize = Math.Clamp(fence.LabelFontSize, 9, 18);
            if (!Enum.IsDefined(fence.OpeningSize)) fence.OpeningSize = OpeningSize.Custom;
            if (!Enum.IsDefined(fence.FileView)) fence.FileView = FileView.Grid;
            if (!Enum.IsDefined(fence.SurfaceStyle)) fence.SurfaceStyle = SurfaceStyle.Solid;
            fence.CornerRadius = Clamp(fence.CornerRadius, 0, 24, 12);
            fence.Hotkey = HotkeyGesture.Normalize(fence.Hotkey);
            if (fence.Tabs == null) throw new InvalidDataException(L.Text("Некорректные вкладки."));
            if (fence.Tabs.Count == 0) fence.Tabs.Add(new());
            if (fence.Tabs.Count > 100) throw new InvalidDataException(L.Text("Слишком много вкладок."));
            if (fence.Tabs.Any(t => t is null)) throw new InvalidDataException(L.Text("Некорректная вкладка."));
            foreach (var tab in fence.Tabs)
            {
                if (string.IsNullOrWhiteSpace(tab.Id) || !tabIds.Add(tab.Id)) throw new InvalidDataException(L.Text("Повторяющийся идентификатор вкладки."));
                tab.Name = Label(tab.Name, L.Text("Файлы"));
                if (tab.Color != null && !HexColor().IsMatch(tab.Color)) tab.Color = null;
                if (tab.Items == null || tab.Items.Count > 100000) throw new InvalidDataException(L.Text("Некорректный список файлов."));
                if (!string.IsNullOrEmpty(tab.PortalPath)) tab.PortalPath = AbsolutePath(tab.PortalPath);
                tab.Items = tab.Items.Where(p => !string.IsNullOrWhiteSpace(p)).Select(AbsolutePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (tab.ReferenceLaunches == null || tab.ReferenceLaunches.Count > 100000) throw new InvalidDataException(L.Text("Некорректный список файлов."));
                var itemPaths = tab.Items.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var launches = new Dictionary<string, ReferenceLaunch>(StringComparer.OrdinalIgnoreCase);
                foreach (var (path, launch) in tab.ReferenceLaunches)
                {
                    var key = AbsolutePath(path);
                    if (!itemPaths.Contains(key)) continue;
                    if (launch == null || launch.Arguments == null || launch.Arguments.Length > 32767 || launch.Arguments.Contains('\0') || launch.WorkingDirectory == null || launch.WorkingDirectory.Length > 32767) throw new InvalidDataException(L.Text("Некорректный список файлов."));
                    var workingDirectory = string.IsNullOrEmpty(launch.WorkingDirectory) ? "" : AbsolutePath(launch.WorkingDirectory);
                    launches.TryAdd(key, launch with { Name = Label(launch.Name, ""), WorkingDirectory = workingDirectory });
                }
                tab.ReferenceLaunches = launches;
                if (tab.Widgets == null || tab.Widgets.Count > 20 || tab.Widgets.Any(w => w == null)) throw new InvalidDataException(L.Text("Некорректный список виджетов."));
                EnsureUnique(tab.Widgets.Select(w => w.Id));
                foreach (var widget in tab.Widgets)
                {
                    if (!Enum.IsDefined(widget.Kind)) throw new InvalidDataException(L.Text("Неизвестный виджет."));
                    widget.Title = Label(widget.Title, ""); widget.Text ??= "";
                    if (widget.Text.Length > 20000) widget.Text = widget.Text[..20000];
                    widget.FocusMinutes = Math.Clamp(widget.FocusMinutes, 1, 180);
                    widget.RemainingSeconds = Math.Clamp(widget.RemainingSeconds, 0, 180 * 60);
                    if (!Enum.IsDefined(widget.ProcessSort)) widget.ProcessSort = ProcessSortMode.Cpu;
                    widget.ProcessCount = Math.Clamp(widget.ProcessCount, 3, 10);
                    widget.GpuAdapterId = CleanDeviceId(widget.GpuAdapterId);
                    widget.NetworkAdapterId = CleanDeviceId(widget.NetworkAdapterId);
                }
            }
            if (!fence.Tabs.Any(t => t.Id == fence.ActiveTabId)) fence.ActiveTabId = fence.Tabs[0].Id;
        }
        foreach (var rule in workspace.Rules)
        {
            rule.Name = Label(rule.Name, L.Text("Правило")); rule.Pattern ??= "";
            if (!DesktopOrganizer.Categories.Any(c => c.Id == rule.DesktopCategory)) rule.DesktopCategory = null;
            if (!Enum.IsDefined(rule.Kind)) throw new InvalidDataException(L.Text("Неизвестный тип правила."));
        }
        workspace.Preferences.Language = L.Normalize(workspace.Preferences.Language);
        workspace.Preferences.GridSize = Math.Clamp(workspace.Preferences.GridSize, 4, 64);
        workspace.Preferences.FenceGap = Math.Clamp(workspace.Preferences.FenceGap, 0, 48);
        workspace.Preferences.ChameleonOpacity = Clamp(workspace.Preferences.ChameleonOpacity, .1, .8, .28);
        var prefs = workspace.Preferences;
        prefs.GuidedTourVersion = Math.Clamp(prefs.GuidedTourVersion, 0, 1000);
        var tourStep = string.Concat((prefs.GuidedTourStepId ?? "").Where(c => !char.IsControl(c))).Trim();
        prefs.GuidedTourStepId = tourStep[..Math.Min(tourStep.Length, 80)];
        prefs.PeekHotkey = HotkeyGesture.Normalize(prefs.PeekHotkey, "Ctrl+Alt+Space");
        prefs.HideHotkey = HotkeyGesture.Normalize(prefs.HideHotkey, "Ctrl+Alt+H");
        prefs.PreviousPageHotkey = HotkeyGesture.Normalize(prefs.PreviousPageHotkey, "Ctrl+Alt+Left");
        prefs.NextPageHotkey = HotkeyGesture.Normalize(prefs.NextPageHotkey, "Ctrl+Alt+Right");
        prefs.ManagerHotkey = HotkeyGesture.Normalize(prefs.ManagerHotkey, "Ctrl+Alt+M");
        prefs.SearchHotkey = HotkeyGesture.Normalize(prefs.SearchHotkey, "Ctrl+Alt+K");
        if (!Enum.IsDefined(prefs.Theme)) prefs.Theme = InterfaceTheme.Graphite;
        if (!HexColor().IsMatch(prefs.InterfaceAccent ?? "")) prefs.InterfaceAccent = "#A0E9C0";
        return workspace;
    }

    private static string CleanDeviceId(string? id)
    {
        var value = string.Concat((id ?? "").Where(character => !char.IsControl(character))).Trim();
        return value[..Math.Min(value.Length, 512)];
    }

    private static string Label(string? text, string fallback) => string.IsNullOrWhiteSpace(text) ? fallback : text.Trim()[..Math.Min(text.Trim().Length, 100)];
    private static double Clamp(double number, double min, double max, double fallback) => double.IsFinite(number) ? Math.Clamp(number, min, max) : fallback;
    private static string AbsolutePath(string path) => Path.IsPathFullyQualified(path) ? PathExtensions.Key(path) : throw new InvalidDataException(L.Text("Путь к файлу должен быть абсолютным."));
    private static void EnsureUnique(IEnumerable<string> ids)
    {
        var known = new HashSet<string>();
        foreach (var id in ids) if (string.IsNullOrWhiteSpace(id) || !known.Add(id)) throw new InvalidDataException(L.Text("Некорректные идентификаторы объектов."));
    }
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)] private static partial Regex HexColor();
}
