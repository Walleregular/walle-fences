using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private const int InspectorContentLimit = 40;
    private sealed record InspectorContentFile(FileEntry Entry, ImageSource? Icon);
    private sealed record InspectorContentResult(IReadOnlyList<InspectorContentFile> Files, int FileCount, bool PortalUnavailable);

    private UIElement InspectorTabContent(Fence fence, FenceTab tab)
    {
        var fenceId = fence.Id; var tabId = tab.Id;
        var body = new StackPanel();
        var host = new Border
        {
            Child = body, Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7), Padding = new Thickness(9), Margin = new Thickness(0, 7, 0, 8)
        };
        AutomationProperties.SetAutomationId(host, "inspector-tab-content-" + tabId);
        AutomationProperties.SetName(host, L.Text("Содержимое вкладки") + " · " + tab.Name);
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var headingText = Ui.Text(L.Text("Содержимое"), 12, Ui.Ink, FontWeights.SemiBold);
        heading.Children.Add(headingText);
        var caption = Ui.Text(tab.Name, 10, Ui.Muted);
        caption.TextWrapping = TextWrapping.NoWrap; caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.ToolTip = tab.Name; caption.Margin = new Thickness(0, 3, 0, 7);
        var contents = new StackPanel();
        var loading = Ui.Text(L.Text("Загрузка…"), 11, Ui.Muted);
        loading.Margin = new Thickness(0, 7, 0, 5); contents.Children.Add(loading);
        body.Children.Add(heading); body.Children.Add(caption); body.Children.Add(contents);
        CancellationTokenSource? pending = null; var generation = 0;

        (Fence Fence, FenceTab Tab)? Current()
        {
            var liveFence = _app.Workspace.Fences.FirstOrDefault(candidate => candidate.Id == fenceId);
            var liveTab = liveFence?.Tabs.FirstOrDefault(candidate => candidate.Id == tabId);
            return liveFence != null && liveTab != null ? (liveFence, liveTab) : null;
        }
        bool IsCurrent(int version, CancellationToken token)
            => !token.IsCancellationRequested && version == generation && host.IsLoaded && CurrentSection == "groups"
                && SelectedFence?.Id == fenceId && Current() is { } current && InspectorContentTab(current.Fence).Id == tabId;
        void OpenGroup()
        {
            if (Current() is { } current) _app.OpenGroup(current.Fence, current.Tab);
        }

        async Task LoadContent()
        {
            if (!host.IsLoaded || CurrentSection != "groups" || SelectedFence?.Id != fenceId || Current() is not { } current
                || InspectorContentTab(current.Fence).Id != tabId) return;
            pending?.Cancel(); pending?.Dispose();
            var cancellation = pending = new CancellationTokenSource();
            var token = cancellation.Token; var version = ++generation;
            // Workers enumerate a detached snapshot, never lists being edited by the UI.
            var snapshotTab = new FenceTab
            {
                Id = tabId, Name = current.Tab.Name, PortalPath = current.Tab.PortalPath,
                Items = current.Tab.Items.ToList(),
                ReferenceLaunches = new Dictionary<string, ReferenceLaunch>(current.Tab.ReferenceLaunches, StringComparer.OrdinalIgnoreCase),
                Widgets = current.Tab.Widgets.Select(WidgetCatalog.Copy).ToList()
            };
            var snapshotFence = new Fence { Id = fenceId, Sort = current.Fence.Sort, Tabs = [snapshotTab], ActiveTabId = tabId };
            var showHidden = _app.Workspace.Preferences.ShowHiddenFiles;
            caption.Text = snapshotTab.Name + " · " + L.Text("Загрузка…");
            contents.Children.Clear(); contents.Children.Add(loading);
            try
            {
                var result = await Task.Run(() => ReadInspectorContent(snapshotFence, snapshotTab, showHidden, token), token);
                if (!IsCurrent(version, token)) return;
                var total = result.FileCount + snapshotTab.Widgets.Count;
                caption.Text = snapshotTab.Name + " · " + string.Format(L.Culture, L.Text("Объектов: {0}"), total);
                contents.Children.Clear();
                if (snapshotTab.PortalPath is { Length: > 0 } portal)
                {
                    var path = Ui.Text(portal, 10, Ui.Muted);
                    path.TextWrapping = TextWrapping.NoWrap; path.TextTrimming = TextTrimming.CharacterEllipsis;
                    path.ToolTip = portal; path.Margin = new Thickness(0, 0, 0, 7);
                    AutomationProperties.SetName(path, L.Text("Папка-портал") + " · " + portal);
                    var portalRow = new Grid();
                    portalRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                    portalRow.ColumnDefinitions.Add(new() { Width = new GridLength(25) });
                    portalRow.Children.Add(path);
                    var browse = Ui.IconButton("folder", () =>
                    {
                        var latest = InspectorContentTarget(fenceId, tabId);
                        if (!string.IsNullOrEmpty(latest.Tab.PortalPath)) Native.Open(latest.Tab.PortalPath);
                    }, L.Text("Открыть папку"), "inspector-content-portal-open-" + tabId);
                    browse.Width = browse.Height = 24; browse.MinHeight = 0; browse.Padding = new Thickness(3); browse.Margin = new Thickness(0);
                    Grid.SetColumn(browse, 1); portalRow.Children.Add(browse);
                    contents.Children.Add(portalRow);
                }
                if (result.PortalUnavailable)
                {
                    var unavailable = Ui.Text(L.Text("Папка недоступна."), 11, Ui.Muted);
                    unavailable.Margin = new Thickness(0, 4, 0, 8); contents.Children.Add(unavailable);
                }
                if (total == 0 && !result.PortalUnavailable)
                {
                    var empty = Ui.Text(L.Text("В этой вкладке пока ничего нет."), 11, Ui.Muted);
                    empty.Margin = new Thickness(0, 4, 0, 3); contents.Children.Add(empty);
                    var hint = Ui.Text(L.Text(snapshotTab.PortalPath is { Length: > 0 }
                        ? "В папке нет отображаемых файлов." : "Добавьте файлы или программы кнопкой ниже."), 10, Ui.Muted);
                    hint.Margin = new Thickness(0, 0, 0, 5); contents.Children.Add(hint);
                    return;
                }
                var list = new ListBox
                {
                    MaxHeight = 210, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(0), Margin = new Thickness(-2, 0, -2, 0)
                };
                ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
                ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
                AutomationProperties.SetAutomationId(list, "inspector-content-items-" + tabId);
                var shown = 0;
                foreach (var widget in snapshotTab.Widgets.Take(InspectorContentLimit))
                {
                    list.Items.Add(InspectorWidgetContentRow(fenceId, tabId, widget, () => _ = LoadContent())); shown++;
                }
                foreach (var file in result.Files.Take(Math.Max(0, InspectorContentLimit - shown)))
                {
                    list.Items.Add(InspectorFileContentRow(fenceId, tabId, file, () => _ = LoadContent())); shown++;
                }
                if (shown > 0) contents.Children.Add(list);
                if (total > shown)
                {
                    var more = Ui.Text(string.Format(L.Culture, L.Text("Показано {0} из {1}. Остальное доступно в группе."), shown, total), 10, Ui.Muted);
                    more.Margin = new Thickness(0, 7, 0, 3); contents.Children.Add(more);
                }
                var open = Ui.Button(L.Text("Открыть группу"), OpenGroup, id: "inspector-content-open-" + tabId);
                open.HorizontalAlignment = HorizontalAlignment.Stretch; open.Margin = new Thickness(0, 6, 0, 0);
                open.Padding = new Thickness(6, 5, 6, 5); open.MinHeight = 28; open.FontSize = 10;
                contents.Children.Add(open);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsCurrent(version, token)) return;
                _app.Log(ex); caption.Text = snapshotTab.Name;
                contents.Children.Clear();
                contents.Children.Add(Ui.Text(L.Text("Не удалось загрузить содержимое. Обновите список."), 11, Ui.Muted));
            }
        }

        var refresh = Ui.IconButton(Brand.Icon("refresh", Ui.Muted, 14), () => _ = LoadContent(), L.Text("Обновить содержимое"), "inspector-content-refresh-" + tabId);
        refresh.Width = refresh.Height = 24; refresh.MinHeight = 0; refresh.Padding = new Thickness(3); refresh.Margin = new Thickness(0);
        Grid.SetColumn(refresh, 1); heading.Children.Add(refresh);
        host.Loaded += async (_, _) => await LoadContent();
        host.Unloaded += (_, _) => { generation++; pending?.Cancel(); pending?.Dispose(); pending = null; };
        return host;
    }

    private static InspectorContentResult ReadInspectorContent(Fence fence, FenceTab tab, bool showHidden, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (tab.PortalPath is { Length: > 0 } portal)
        {
            try
            {
                if (!Directory.Exists(portal)) return new([], 0, true);
                // FileCatalog skips inaccessible paths. Probe the root separately so an
                // unavailable folder is distinguished from an empty one in the inspector.
                using var probe = Directory.EnumerateFileSystemEntries(portal).GetEnumerator();
                probe.MoveNext();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return new([], 0, true); }
        }
        var files = FileCatalog.Items(fence, tab, showHidden);
        var visible = new List<InspectorContentFile>();
        foreach (var entry in files.Take(Math.Max(0, InspectorContentLimit - tab.Widgets.Count)))
        {
            token.ThrowIfCancellationRequested();
            visible.Add(new(entry, Native.Icon(entry.Path)));
        }
        token.ThrowIfCancellationRequested();
        return new(visible, files.Count, false);
    }

    private (Fence Fence, FenceTab Tab) InspectorContentTarget(string fenceId, string tabId)
    {
        var fence = _app.Workspace.Fences.FirstOrDefault(candidate => candidate.Id == fenceId)
            ?? throw new InvalidOperationException(L.Text("Группа удалена"));
        var tab = fence.Tabs.FirstOrDefault(candidate => candidate.Id == tabId)
            ?? throw new InvalidOperationException(L.Text("Вкладка больше не существует."));
        return (fence, tab);
    }

    private string InspectorReferencePath(FenceTab tab, string path)
    {
        if (tab.PortalPath is { Length: > 0 } portal)
        {
            if (Path.GetDirectoryName(path) is { } parent && PathExtensions.Same(parent, portal)) return path;
        }
        else if (tab.Items.FirstOrDefault(candidate => PathExtensions.Same(candidate, path)) is { } currentPath) return currentPath;
        throw new InvalidOperationException(L.Text("Ссылка больше не существует во вкладке."));
    }

    private ListBoxItem InspectorFileContentRow(string fenceId, string tabId, InspectorContentFile file, Action reload)
    {
        var entry = file.Entry;
        void Open()
        {
            var current = InspectorContentTarget(fenceId, tabId);
            var path = InspectorReferencePath(current.Tab, entry.Path);
            Native.Open(path, string.IsNullOrEmpty(current.Tab.PortalPath) ? FileCatalog.Launch(current.Tab, path) : null);
        }
        ContextMenu Menu()
        {
            var current = InspectorContentTarget(fenceId, tabId);
            var menu = new ContextMenu();
            menu.Items.Add(InspectorContentAction("Открыть", "external-link", Open));
            var launch = string.IsNullOrEmpty(current.Tab.PortalPath) ? FileCatalog.Launch(current.Tab, entry.Path) : null;
            if (AppController.CanSaveProgram(entry.Path, launch))
            {
                var favorite = _app.IsFavoriteProgram(entry.Path, launch);
                menu.Items.Add(InspectorContentAction(favorite ? "Убрать из избранного" : "Добавить в избранное", favorite ? "star-filled" : "star", () =>
                {
                    var latest = InspectorContentTarget(fenceId, tabId);
                    var path = InspectorReferencePath(latest.Tab, entry.Path);
                    _app.ToggleProgramFavorite(path, string.IsNullOrEmpty(latest.Tab.PortalPath) ? FileCatalog.Launch(latest.Tab, path) : null);
                }));
            }
            menu.Items.Add(InspectorContentAction("Показать в Проводнике", "folder", () =>
            {
                var latest = InspectorContentTarget(fenceId, tabId);
                Native.Reveal(InspectorReferencePath(latest.Tab, entry.Path));
            }));
            menu.Items.Add(InspectorContentAction("Скопировать путь", "copy", () =>
            {
                var latest = InspectorContentTarget(fenceId, tabId);
                Clipboard.SetText(InspectorReferencePath(latest.Tab, entry.Path));
            }));
            if (!current.Fence.ContentLocked && string.IsNullOrEmpty(current.Tab.PortalPath))
            {
                menu.Items.Add(new Separator());
                var remove = InspectorContentAction("Убрать ссылку из группы", "close", () =>
                {
                    var latest = InspectorContentTarget(fenceId, tabId);
                    WorkspaceActions.RequireEditable(latest.Fence);
                    if (!string.IsNullOrEmpty(latest.Tab.PortalPath)) return;
                    var path = InspectorReferencePath(latest.Tab, entry.Path);
                    _app.Mutate(() => FileCatalog.RemoveReferences(latest.Tab, [path])); _app.SaveNow(); reload();
                });
                remove.ToolTip = L.Text("Исходный файл останется на компьютере."); menu.Items.Add(remove);
            }
            FenceMenuTheme.Apply(menu, current.Fence, current.Tab); return menu;
        }
        var icon = file.Icon == null ? Brand.Icon(entry.IsDirectory ? "folder" : "file", Ui.Muted, 20)
            : new Image { Source = file.Icon, Width = 20, Height = 20, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        var detail = entry.Exists ? entry.Path : L.Text("Файл не найден") + " · " + entry.Path;
        return InspectorContentRow(entry.Name, detail, icon, entry.Path, Open, Menu, "inspector-content-file-" + tabId + "-" + entry.Path);
    }

    private ListBoxItem InspectorWidgetContentRow(string fenceId, string tabId, DesktopWidget widget, Action reload)
    {
        var label = (int)widget.Kind >= 0 && (int)widget.Kind < WidgetEditor.Labels.Length ? WidgetEditor.Labels[(int)widget.Kind] : L.Text("Виджет");
        var name = string.IsNullOrWhiteSpace(widget.Title) ? label : widget.Title;
        var preview = widget.Kind == WidgetKind.Note && !string.IsNullOrWhiteSpace(widget.Text)
            ? string.Join(" ", widget.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim() : label;
        if (preview.Length > 120) preview = preview[..120] + "…";
        void Edit()
        {
            var current = InspectorContentTarget(fenceId, tabId);
            WorkspaceActions.RequireEditable(current.Fence);
            var latest = current.Tab.Widgets.FirstOrDefault(candidate => candidate.Id == widget.Id)
                ?? throw new InvalidOperationException(L.Text("Виджет больше не существует."));
            WidgetEditor.Edit(_app, current.Fence, current.Tab, latest); reload();
        }
        ContextMenu Menu()
        {
            var current = InspectorContentTarget(fenceId, tabId);
            var menu = new ContextMenu();
            var edit = InspectorContentAction("Настроить виджет", "settings", Edit);
            edit.IsEnabled = !current.Fence.ContentLocked; menu.Items.Add(edit);
            menu.Items.Add(InspectorContentAction("Открыть группу", "external-link", () =>
            {
                var latest = InspectorContentTarget(fenceId, tabId); _app.OpenGroup(latest.Fence, latest.Tab);
            }));
            return menu;
        }
        return InspectorContentRow(name, preview, Brand.Icon(WidgetCatalog.Icon(widget.Kind), Ui.Accent, 20), name + " · " + preview,
            () => { if (!InspectorContentTarget(fenceId, tabId).Fence.ContentLocked) Edit(); }, Menu, "inspector-content-widget-" + widget.Id);
    }

    private static MenuItem InspectorContentAction(string label, string icon, Action action)
    {
        var item = new MenuItem { Header = L.Text(label), Icon = Brand.Icon(icon, Ui.Ink, 16) };
        item.Click += (_, _) => Ui.Guard(action); return item;
    }

    private static ListBoxItem InspectorContentRow(string name, string detail, UIElement icon, string tooltip, Action open, Func<ContextMenu> menu, string id)
    {
        var row = new Grid { MinHeight = 40, Margin = new Thickness(2, 1, 0, 1) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(27) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(25) });
        row.Children.Add(icon);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Ui.Text(name, 11, Ui.Ink);
        title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var subtitle = Ui.Text(detail, 9, Ui.Muted);
        subtitle.TextWrapping = TextWrapping.NoWrap; subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        subtitle.Margin = new Thickness(0, 2, 0, 0); labels.Children.Add(title); labels.Children.Add(subtitle);
        Grid.SetColumn(labels, 1); row.Children.Add(labels);
        Button? more = null;
        more = Ui.IconButton(Brand.Icon("more", Ui.Muted, 13), () =>
        {
            var actions = menu(); actions.PlacementTarget = more; actions.Placement = PlacementMode.Bottom; actions.IsOpen = true;
        }, L.Text("Действия с содержимым"), id + "-actions");
        more.Width = 24; more.Height = 28; more.MinHeight = 0; more.Padding = new Thickness(2); more.Margin = new Thickness(0);
        Grid.SetColumn(more, 2); row.Children.Add(more);
        var item = new ListBoxItem { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(1), ToolTip = tooltip };
        AutomationProperties.SetName(item, name); AutomationProperties.SetHelpText(item, detail);
        AutomationProperties.SetAutomationId(item, id);
        item.ContextMenu = menu();
        item.ContextMenuOpening += (_, _) => Ui.Guard(() => item.ContextMenu = menu());
        item.MouseDoubleClick += (_, args) => { if (args.OriginalSource is DependencyObject source && FindContentButton(source) != null) return; Ui.Guard(open); args.Handled = true; };
        item.KeyDown += (_, args) => { if (args.Key == Key.Enter && !more.IsKeyboardFocusWithin) { Ui.Guard(open); args.Handled = true; } };
        return item;
    }

    private static Button? FindContentButton(DependencyObject source)
    {
        for (var current = source; current != null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is Button button) return button;
        return null;
    }
}
