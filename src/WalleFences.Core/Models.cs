using System.Text.Json.Serialization;

namespace WalleFences.Core;

public sealed class Workspace
{
    public int SchemaVersion { get; set; } = 1;
    public string ActivePageId { get; set; } = "home";
    public List<DesktopPage> Pages { get; set; } = [new() { Id = "home", Name = L.Text("Основной") }];
    public List<Fence> Fences { get; set; } = [];
    public List<SortingRule> Rules { get; set; } = [];
    public Preferences Preferences { get; set; } = new();
}

public sealed class DesktopPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = L.Text("Новая страница");
}

public sealed class Fence
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = L.Text("Новая группа");
    public GroupIcon Icon { get; set; }
    public string PageId { get; set; } = "home";
    public double X { get; set; } = 40;
    public double Y { get; set; } = 60;
    public double Width { get; set; } = 330;
    public double Height { get; set; } = 300;
    public MonitorPlacement? Placement { get; set; }
    public FenceDisplayMode DisplayMode { get; set; }
    public FenceLayer Layer { get; set; }
    public string? DesktopCategory { get; set; }
    public DockEdge Edge { get; set; }
    public EdgeSpanMode EdgeSpanMode { get; set; }
    public int EdgeBlockCount { get; set; } = 5;
    public int EdgeBlockIndex { get; set; }
    public double EdgeThickness { get; set; } = 4;
    public double EdgeLength { get; set; } = 160;
    public double EdgeOffset { get; set; } = 16;
    public double ActivationDistance { get; set; } = 12;
    public int OpenDelayMs { get; set; } = 180;
    public int CloseDelayMs { get; set; } = 500;
    public RevealTrigger RevealTrigger { get; set; }
    public TabStyle TabStyle { get; set; } = TabStyle.Flat;
    public TabVisibility TabVisibility { get; set; } = TabVisibility.Auto;
    public int IconSpacing { get; set; } = 4;
    public int LabelFontSize { get; set; } = 11;
    public bool CloseAfterLaunch { get; set; }
    public bool PortalFoldersInExplorer { get; set; }
    public bool KeepVisibleOnHide { get; set; }
    public bool ContentLocked { get; set; }
    public OpeningSize OpeningSize { get; set; } = OpeningSize.Custom;
    public FileView FileView { get; set; }
    public SurfaceStyle SurfaceStyle { get; set; }
    public double CornerRadius { get; set; } = 12;
    public string Hotkey { get; set; } = "";
    public bool IsSidebar { get; set; }
    public string Color { get; set; } = "#1D2021";
    public string Accent { get; set; } = "#A0E9C0";
    public double Opacity { get; set; } = .97;
    public int IconSize { get; set; } = 40;
    public bool TintIcons { get; set; }
    public bool ShowLabels { get; set; } = true;
    public bool RolledUp { get; set; }
    public bool HoverExpand { get; set; }
    public bool Locked { get; set; }
    public SortOrder Sort { get; set; }
    public string ActiveTabId { get; set; } = "";
    public List<FenceTab> Tabs { get; set; } = [new()];

    [JsonIgnore] public FenceTab ActiveTab => Tabs.FirstOrDefault(t => t.Id == ActiveTabId) ?? Tabs[0];
    [JsonIgnore] public bool ContentCollapsed => Edge != DockEdge.None || RolledUp || ActiveTab.Collapsed;
}

public sealed class FenceTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = L.Text("Файлы");
    public string? PortalPath { get; set; }
    public string? Color { get; set; }
    public bool Collapsed { get; set; }
    public List<string> Items { get; set; } = [];
    public Dictionary<string, ReferenceLaunch> ReferenceLaunches { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DesktopWidget> Widgets { get; set; } = [];
}

// Items remain real file-system targets. Optional launch data makes imported
// shortcuts independent of the original .lnk without losing their invocation.
public sealed record ReferenceLaunch(string Name = "", string Arguments = "", string WorkingDirectory = "", bool RunAsAdministrator = false);
public sealed record ImportedReference(string SourcePath, string Path, ReferenceLaunch? Launch = null);
public sealed record ReferenceImportResult(int Added, IReadOnlyList<string> AcceptedSources);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OpeningSize { Custom, Small, Medium, Wide, Tall, Sidebar }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileView { Grid, List }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SurfaceStyle { Solid, Outline, Soft }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InterfaceTheme { Graphite, Paper, Midnight }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GroupIcon { Automatic, Folder, Briefcase, Desktop, Code, Terminal, Gamepad, Star, Heart, Music, Image, Video, Globe, Book, Mail, Calendar, Drive, Cloud, Download, Widgets, Processor, Network, Note, Layers }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WidgetKind { Clock, Note, Focus, System, Drives, CpuTemperature, Processes, Memory, Gpu, Network, Battery, Cpu, Uptime }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProcessSortMode { Cpu, Memory }

public sealed class DesktopWidget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public WidgetKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Use24HourClock { get; set; } = true;
    public int FocusMinutes { get; set; } = 25;
    public int RemainingSeconds { get; set; } = 25 * 60;
    public DateTimeOffset? FocusEndsAt { get; set; }
    public ProcessSortMode ProcessSort { get; set; }
    public int ProcessCount { get; set; } = 5;
    public string GpuAdapterId { get; set; } = "";
    public string NetworkAdapterId { get; set; } = "";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortOrder { Manual, Name, Type, Newest, Oldest, Size }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FenceDisplayMode { Standard, Compact, Minimal }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FenceLayer { Desktop, ExpandedOnTop, AlwaysOnTop }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DockEdge { None, Top, Left, Right, Bottom }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EdgeSpanMode { Custom, Full, EqualBlocks }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RevealTrigger { Proximity, HoverPause, Click, Hover, DoubleClick, Hotkey, AlwaysOpen }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TabStyle { Rounded, Flat, Segmented, MenuOnly }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TabVisibility { Auto, Always, Hidden }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleKind { Extension, Name, Folder, ModifiedWithinDays, Everything }

public sealed class SortingRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = L.Text("Правило");
    public bool Enabled { get; set; } = true;
    public RuleKind Kind { get; set; }
    public string Pattern { get; set; } = "";
    public string? DesktopCategory { get; set; }
    public string FenceId { get; set; } = "";
    public string TabId { get; set; } = "";
}

public sealed class Preferences
{
    public string Language { get; set; } = "ru";
    public bool AutoOrganize { get; set; } = true;
    public bool GameMode { get; set; } = true;
    public bool HideDesktopIcons { get; set; }
    public bool DoubleClickHide { get; set; } = true;
    public bool Chameleon { get; set; }
    public double ChameleonOpacity { get; set; } = .28;
    public string PeekHotkey { get; set; } = "Ctrl+Alt+Space";
    public string HideHotkey { get; set; } = "Ctrl+Alt+H";
    public string PreviousPageHotkey { get; set; } = "Ctrl+Alt+Left";
    public string NextPageHotkey { get; set; } = "Ctrl+Alt+Right";
    public string ManagerHotkey { get; set; } = "Ctrl+Alt+M";
    public string SearchHotkey { get; set; } = "Ctrl+Alt+K";
    public InterfaceTheme Theme { get; set; } = InterfaceTheme.Graphite;
    public string InterfaceAccent { get; set; } = "#A0E9C0";
    public bool CompactNavigation { get; set; }
    public bool AdvancedSettings { get; set; }
    public int SetupVersion { get; set; }
    public bool FirstRunCompleted { get; set; }
    public bool OnboardingDismissed { get; set; }
    public int GuidedTourVersion { get; set; }
    public string GuidedTourStepId { get; set; } = "";
    public bool GuidedTourCompleted { get; set; }
    public bool SnapToGrid { get; set; } = true;
    public int GridSize { get; set; } = 12;
    public bool ShowHiddenFiles { get; set; }
    public bool ShowManagerOnStart { get; set; } = true;
    public bool StartCollapsed { get; set; }
    public bool RestoreMonitorPositions { get; set; } = true;
    public bool SmartSpacing { get; set; } = true;
    public int FenceGap { get; set; } = 10;
    public bool PeekHotCorner { get; set; }
    public bool PageWheel { get; set; }
}

public sealed record FileEntry(string Path, string Name, bool IsDirectory, string Extension, long Size, DateTime Modified, bool Exists)
{
    public static FileEntry FromPath(string path)
    {
        try
        {
            var directory = Directory.Exists(path);
            var exists = directory || File.Exists(path);
            var info = directory ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
            var size = info is FileInfo file && exists ? file.Length : 0;
            var name = info.Name;
            if (PathExtensions.IsShortcut(path)) name = System.IO.Path.GetFileNameWithoutExtension(name);
            return new(path, name, directory, directory ? "" : System.IO.Path.GetExtension(path).ToLowerInvariant(), size, exists ? info.LastWriteTimeUtc : DateTime.MinValue, exists);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(path, System.IO.Path.GetFileName(path), false, "", 0, DateTime.MinValue, false);
        }
    }
}

public static class PathExtensions
{
    public static bool IsShortcut(string path) => new[] { ".lnk", ".url", ".appref-ms" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static string Key(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var root = System.IO.Path.GetPathRoot(full)!;
        return full.Length <= root.Length ? root : full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }
    public static bool Same(string a, string b) => string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);
}
