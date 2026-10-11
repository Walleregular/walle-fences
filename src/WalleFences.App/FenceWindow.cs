using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Text.Json;
using Forms = System.Windows.Forms;

namespace WalleFences.App;

internal sealed record ItemDrag(string FenceId, string TabId, string[] Paths);
internal sealed record TabDrag(string FenceId, string TabId);

internal sealed partial class FenceWindow : Window
{
    internal const double CollapsedHeight = DisplayLayout.CollapsedHeight;
    internal double CollapsedLogicalHeight => FenceLayout.CollapsedHeight(Fence);
    internal bool ShouldFloat => _app.IsPeek || _accessOnTop || FenceLayout.OnTop(Fence, _hoverExpanded) || KeepFloatingDuringReveal;
    internal Fence Fence { get; }
    private readonly AppController _app;
    private readonly SolidColorBrush _ink, _muted, _accent;
    private readonly Grid _layout = new();
    private readonly StackPanel _body = new();
    private readonly ListBox _files = new() { SelectionMode = SelectionMode.Extended };
    private readonly TextBlock _count = Ui.Text("", 10);
    private readonly TextBlock _empty = Ui.Text("", 12);
    private readonly TextBox _search = new() { Margin = new Thickness(10, 0, 10, 6), Padding = new Thickness(7, 4, 7, 4), FontSize = 11 };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer _tabStrip = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly Grid _tabSurface = new();
    private readonly Border _tabSelection = new();
    private readonly Border _tabIndicator = new() { Height = 2, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Dictionary<string, TabVisual> _tabViews = [];
    private readonly TabSelectionMotion _tabMotion;
    private string? _tabsSignature;
    private string? _tabConfigurationSignature;
    private readonly DockPanel _content = new();
    private readonly DockPanel _tabContent = new();
    private readonly SurfaceMotion _tabContentMotion;
    private readonly StackPanel _navigation = new() { Margin = new Thickness(8, 0, 8, 6) };
    private readonly Dictionary<string, string> _portalLocations = [];
    private readonly Dictionary<string, Stack<string>> _history = [];
    private IReadOnlyList<FileEntry> _entries = [];
    private string? _entriesTabId, _entriesPortal;
    private readonly HoverReveal _reveal = new();
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly Border _chrome;
    private readonly Grid _surface = new() { ClipToBounds = true };
    private readonly Border _edgeHandle;
    private readonly WidgetHost _widgets;
    private readonly ScrollViewer _widgetScroll;
    private string _visualSignature;
    private Native.Point _previousPointer;
    private Button? _collapseButton;
    private Button? _pinButton;
    private Button? _searchButton;
    private Image? _groupGlyph;
    private UIElement? _headerView;
    private FileSystemWatcher? _portalWatcher;
    private string? _watchedPath;
    private Point _dragStart;
    private bool _hoverExpanded, _closed;
    private int _loadVersion;
    private double _renderRatio = 1;
    private double _opacityTarget = double.NaN;
    private bool _suppressRevealUntilLeave;
    private bool _keyboardAccess;
    private bool _accessOnTop;
    private bool _searchExpanded;
    private string? _renderSignature;
    private string? _navigationSignature;
    internal bool IsEdgeDock => Fence.Edge != DockEdge.None;
    internal bool RevealIsOpen => _reveal.IsOpen;
    internal bool VisuallyCollapsed => !_app.IsPeek && !KeepsExpanded && (IsEdgeDock ? !_hoverExpanded : Fence.ContentCollapsed && !_hoverExpanded);
    internal IReadOnlyList<FileEntry> PreviewEntries => _entriesTabId == Fence.ActiveTab.Id && _entriesPortal == CurrentPortal ? _entries : [];
    internal bool IsContentVisible => _content.Visibility == Visibility.Visible;
    internal bool HasVisibleTabStrip => _tabStrip.Visibility == Visibility.Visible && _tabs.IsVisible;
    internal Rect ActiveTabBounds => _tabViews.TryGetValue(Fence.ActiveTab.Id, out var view) && view.Group.ActualWidth > 0 ? new(view.Group.TranslatePoint(new Point(), _tabSurface), view.Group.RenderSize) : Rect.Empty;
    internal string? CurrentPortal => Fence.ActiveTab.PortalPath is { Length: > 0 } root ? _portalLocations.GetValueOrDefault(Fence.ActiveTab.Id, root) : null;
    private static string VisualSignature(Fence fence) => JsonSerializer.Serialize(new { Language = L.Language, fence.Name, fence.Icon, fence.Width, fence.Height, fence.Color, fence.Accent, fence.Opacity, fence.IconSize, fence.ShowLabels, fence.TintIcons, fence.DisplayMode, fence.Edge, fence.TabStyle, fence.TabVisibility, fence.IconSpacing, fence.LabelFontSize, fence.ContentLocked, fence.FileView, fence.SurfaceStyle, fence.CornerRadius, Tabs = fence.Tabs.Select(t => new { t.Id, t.Name, t.Color, t.PortalPath, Widgets = t.Widgets.Select(w => new { w.Id, w.Kind, w.Title, w.Text, w.FocusMinutes, w.Use24HourClock }) }) });
    internal bool CanReuse(Fence fence) => ReferenceEquals(fence, Fence) && _visualSignature == VisualSignature(fence);
    internal void RefreshState() { SynchronizeRevealMode(); ContextMenu = FenceMenu(); RebuildTabs(); SetRollHeight(false); ApplyChameleon(); Reload(); }
    private LayoutRect TriggerBounds(DisplayArea display) => IsEdgeDock ? EdgeLayout.Bounds(Fence, display, false) : new(Fence.X, Fence.Y, Fence.Width * display.Scale, CollapsedLogicalHeight * display.Scale);
    internal double ProximityDistance(Native.Point pointer)
    {
        if (_closed || !IsVisible || _app.IsPeek || _suppressRevealUntilLeave || !OpensOnPointer || (!IsEdgeDock && (!Fence.ContentCollapsed || !Fence.HoverExpand))) return double.PositiveInfinity;
        var display = DisplayLayout.Select(Fence, AppController.Displays()); var x = pointer.X * AppController.GlobalScale; var y = pointer.Y * AppController.GlobalScale;
        var bounds = TriggerBounds(display);
        if (!bounds.Contains(x, y, RevealMargin(display)) || IsEdgeDock && !new LayoutRect(display.X, display.Y, display.Width, display.Height).Contains(x, y)) return double.PositiveInfinity;
        return bounds.DistanceSquared(x, y);
    }
    internal bool ContainsPointer(Native.Point pointer, double margin = 0)
    {
        if (!Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect)) return false;
        var marginPixels = margin * DisplayLayout.Select(Fence, AppController.Displays()).Scale / AppController.GlobalScale;
        return new LayoutRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top).Contains(pointer.X, pointer.Y, marginPixels);
    }
    internal void TrackPointer(Native.Point pointer, bool allowReveal = true)
    {
        if (_closed || !IsVisible || _app.IsPeek) return;
        if (_accessOnTop && !IsKeyboardFocusWithin && !ContainsPointer(pointer, 6) && !HasOpenTabContextMenu) { _accessOnTop = false; ApplyLayer(); }
        if (KeepsExpanded || Fence.RevealTrigger == RevealTrigger.Hotkey) return;
        if (!IsEdgeDock && !Fence.ContentCollapsed) { if (_hoverExpanded) { _hoverExpanded = false; _reveal.Reset(); SetRollHeight(); } return; }
        var display = DisplayLayout.Select(Fence, AppController.Displays()); var scale = AppController.GlobalScale;
        var x = pointer.X * scale; var y = pointer.Y * scale;
        var trigger = TriggerBounds(display);
        Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var actual);
        var openBounds = new LayoutRect(actual.Left * scale, actual.Top * scale, (actual.Right - actual.Left) * scale, (actual.Bottom - actual.Top) * scale);
        var paused = Math.Abs(pointer.X - _previousPointer.X) <= 2 && Math.Abs(pointer.Y - _previousPointer.Y) <= 2; _previousPointer = pointer;
        var near = trigger.Contains(x, y, RevealMargin(display));
        if (IsEdgeDock) near &= new LayoutRect(display.X, display.Y, display.Width, display.Height).Contains(x, y);
        if (_suppressRevealUntilLeave) { if (near) return; _suppressRevealUntilLeave = false; }
        if (Fence.RevealTrigger == RevealTrigger.HoverPause && !_reveal.IsOpen) near &= paused;
        var hold = IsResizing || !IsEnabled || IsMouseCaptureWithin || _keyboardAccess && IsKeyboardFocusWithin || Mouse.LeftButton == MouseButtonState.Pressed || ContextMenu?.IsOpen == true || HasOpenTabContextMenu || OwnedWindows.Count > 0 || Application.Current.Windows.OfType<FormWindow>().Any(w => w.IsVisible && Equals(w.Tag, Fence.Id)) || _tabs.Children.OfType<Border>().Any(t => t.ContextMenu?.IsOpen == true) || _files.Items.Cast<ListBoxItem>().Any(i => i.ContextMenu?.IsOpen == true);
        var inside = openBounds.Contains(x, y, 6 * display.Scale) || IsRevealAnimating && !_motionClosing && _revealMotion.Target.Contains(x, y, 6 * display.Scale);
        var automatic = allowReveal && OpensOnPointer && (IsEdgeDock || Fence.HoverExpand);
        var openDelay = Fence.RevealTrigger == RevealTrigger.Hover ? 0 : Fence.OpenDelayMs;
        if (_reveal.Tick(Environment.TickCount64, near, inside, hold, openDelay, Fence.CloseDelayMs, automatic))
        {
            _openedAutomatically = _reveal.IsOpen && automatic;
            _hoverExpanded = _reveal.IsOpen; if (IsEdgeDock) RebuildTabs(); SetRollHeight(); ApplyChameleon();
        }
    }

    internal FenceWindow(AppController app, Fence fence)
    {
        _app = app; Fence = fence; _appliedRevealTrigger = fence.RevealTrigger; SynchronizeRevealMode(); Title = "Walle · " + fence.Name; Language = System.Windows.Markup.XmlLanguage.GetLanguage(L.Culture.Name); FontFamily = Ui.AppFont;
        var background = (Color)ColorConverter.ConvertFromString(fence.Color);
        _ink = new(GroupColorAppearance.Text(background)); _muted = new(GroupColorAppearance.Muted(background)); _accent = Ui.Brush(fence.Accent);
        _count.Foreground = _empty.Foreground = _muted;
        _visualSignature = VisualSignature(fence);
        _tabMotion = new(_tabSelection);
        _tabContentMotion = new(_tabContent);
        Width = fence.Width; Height = VisuallyCollapsed ? CollapsedLogicalHeight : fence.Height; Left = fence.X; Top = fence.Y;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; AllowDrop = true;
        AutomationProperties.SetAutomationId(this, "fence-" + fence.Id);
        var color = Ui.Brush(fence.Color, fence.Opacity);
        _chrome = new Border { Background = color, BorderBrush = fence.SurfaceStyle == SurfaceStyle.Outline ? Ui.Brush(fence.Accent, .65) : new SolidColorBrush(_ink.Color) { Opacity = .18 }, BorderThickness = new Thickness(fence.SurfaceStyle == SurfaceStyle.Soft ? 0 : 1), CornerRadius = new CornerRadius(fence.CornerRadius), Child = _layout, SnapsToDevicePixels = true };
        _edgeHandle = new Border { Background = Ui.Brush(fence.Accent, .9), CornerRadius = new CornerRadius(2), Visibility = Visibility.Collapsed, ToolTip = fence.Name };
        _surface.Children.Add(_chrome); _surface.Children.Add(_edgeHandle); Content = _surface;
        _edgeHandle.MouseLeftButtonDown += (_, e) => { if (Fence.RevealTrigger == RevealTrigger.Click || Fence.RevealTrigger == RevealTrigger.DoubleClick && e.ClickCount == 2) { RevealFromHeader(); e.Handled = true; } };
        _layout.RowDefinitions.Add(new() { Height = new GridLength(FenceLayout.HeaderHeight(fence.DisplayMode)) });
        _layout.RowDefinitions.Add(new() { Height = new GridLength(FenceLayout.TabsHeight(fence)) });
        _layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var selectionLayer = new Canvas { IsHitTestVisible = false };
        _tabSelection.Child = _tabIndicator; selectionLayer.Children.Add(_tabSelection);
        _tabSurface.Children.Add(selectionLayer); _tabSurface.Children.Add(_tabs); InitializeTabDragMotion(); _tabStrip.Content = _tabSurface;
        AutomationProperties.SetAutomationId(_tabSelection, "tab-selection");
        AutomationProperties.SetAutomationId(_tabIndicator, "tab-indicator");
        _tabs.SizeChanged += (_, _) => SyncTabSelection(false);
        AutomationProperties.SetAutomationId(_tabStrip, "group-tab-strip-" + fence.Id);
        _tabStrip.PreviewMouseWheel += (_, e) => { _tabStrip.ScrollToHorizontalOffset(_tabStrip.HorizontalOffset - e.Delta / 3.0); e.Handled = true; };
        _headerView = Header(); _layout.Children.Add(_headerView);
        if (fence.DisplayMode != FenceDisplayMode.Minimal) { Grid.SetRow(_tabStrip, 1); _layout.Children.Add(_tabStrip); }
        Grid.SetRow(_content, 2); _layout.Children.Add(_content);
        _content.Children.Add(_tabContent);
        DockPanel.SetDock(_body, Dock.Top); _tabContent.Children.Add(_body);
        _body.Children.Add(_navigation);
        _widgets = new WidgetHost(app, fence);
        _widgetScroll = new ScrollViewer { Content = _widgets, MaxHeight = Math.Max(100, fence.Height - 150), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = true };
        AutomationProperties.SetAutomationId(_widgetScroll, "widget-viewport");
        DockPanel.SetDock(_widgetScroll, Dock.Top); _tabContent.Children.Add(_widgetScroll);
        _search.Margin = new Thickness(0); _search.Background = new SolidColorBrush(_ink.Color) { Opacity = .035 }; _search.BorderBrush = new SolidColorBrush(_ink.Color) { Opacity = .13 }; _search.Foreground = _ink; _search.CaretBrush = _ink;
        _search.SelectionBrush = _accent; _search.Resources["FocusAccent"] = _accent; _search.Resources["Muted"] = _muted;
        var searchGrid = new Grid { Margin = new Thickness(10, 4, 10, 6), Visibility = Visibility.Collapsed }; searchGrid.Children.Add(_search);
        var placeholder = Ui.Text(L.Text("Поиск файлов…"), 11, _muted); placeholder.Margin = new Thickness(8, 0, 0, 0); placeholder.IsHitTestVisible = false; searchGrid.Children.Add(placeholder); _body.Children.Add(searchGrid);
        _search.TextChanged += (_, _) => { placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; _searchTimer.Stop(); _searchTimer.Start(); };
        _search.ToolTip = L.Text("Поиск в текущей вкладке"); AutomationProperties.SetName(_search, L.Text("Поиск файлов"));
        var footer = new Grid { Margin = new Thickness(12, 2, 9, 4) }; DockPanel.SetDock(footer, Dock.Bottom); _tabContent.Children.Add(footer); footer.Children.Add(_count);
        footer.Children.Add(ResizeGrip());
        InitializeResizeHandles();
        var filesGrid = new Grid(); _tabContent.Children.Add(filesGrid); filesGrid.Children.Add(_files); filesGrid.Children.Add(_empty);
        _empty.HorizontalAlignment = HorizontalAlignment.Center; _empty.VerticalAlignment = VerticalAlignment.Center; _empty.TextAlignment = TextAlignment.Center; _empty.Margin = new Thickness(24);
        _files.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(fence.FileView == FileView.List ? typeof(StackPanel) : typeof(WrapPanel)));
        if (fence.FileView == FileView.List) _files.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _files.ItemContainerStyle = (Style)System.Windows.Markup.XamlReader.Parse($$"""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBoxItem">
          <Setter Property="Background" Value="Transparent"/><Setter Property="BorderBrush" Value="Transparent"/><Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="5"><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter>
          <Style.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="{{AccentHex(0x22)}}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="{{AccentHex(0x40)}}"/><Setter Property="BorderBrush" Value="{{AccentHex(0x70)}}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter Property="BorderBrush" Value="{{AccentHex(0xff)}}"/></Trigger></Style.Triggers>
        </Style>
        """);
        _files.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        _files.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _files.Margin = new Thickness(7, 4, 7, 0);
        _files.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(_files);
        _files.PreviewMouseMove += DragFiles;
        _files.MouseDoubleClick += (_, e) => { if (FindItem(e.OriginalSource as DependencyObject) is { Tag: FileEntry entry }) OpenEntry(entry); };
        _files.PreviewKeyDown += FileKeys;
        Drop += OnDrop; DragOver += OnDragOver;
        MouseEnter += (_, _) => ApplyChameleon();
        MouseLeave += (_, _) => ApplyChameleon();
        PreviewMouseLeftButtonDown += (_, _) => _keyboardAccess = false;
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() => { if (!IsKeyboardFocusWithin) _keyboardAccess = false; });
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RenderItems(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _app.IsPeek) { _app.Peek(); e.Handled = true; }
            else if (e.Key == Key.Escape && _accessOnTop) { _accessOnTop = false; _keyboardAccess = false; Keyboard.ClearFocus(); ToggleRoll(false); e.Handled = true; }
            if (e.Key == Key.F5) { Reload(); e.Handled = true; }
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { ShowFileSearch(); e.Handled = true; }
            if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var direction = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                CycleTab(direction); e.Handled = true;
            }
        };
        ContextMenu = FenceMenu();
        Activated += (_, _) => _app.KeepGroupBelowManager(this);
        Closed += (_, _) => { _closed = true; CloseTabContextMenu(); CancelResize(); _tabMotion.Hide(); DisposeTabDragMotion(); _tabContentMotion.Dispose(); StopRevealAnimation(); _loadVersion++; _portalWatcher?.Dispose(); _searchTimer.Stop(); _widgets.Dispose(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible || !IsLoaded || _closed) return;
            CloseTabContextMenu();
            CancelResize();
            CancelTabDropPreview(); StopTabReorderMotion(); _tabContentMotion.Reset();
            // Moving the final tab empties the model before WPF raises Closed.
            // Cancel the departing window's clocks without reading its removed active tab.
            if (Fence.Tabs.Count == 0) { _tabMotion.Hide(); StopRevealAnimation(); return; }
            SetRollHeight(false);
        };
        DpiChanged += (_, _) =>
        {
            // VisualBrush/bitmap previews can propagate a visual DPI change without moving the HWND.
            var nativeDpi = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle);
            if (nativeDpi == 0 || nativeDpi == _layoutNativeDpi || _dpiRefreshQueued) return;
            _dpiRefreshQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                _dpiRefreshQueued = false;
                if (_closed) return;
                // Ignore a transient DPI flip while the native position and WPF size settle together.
                var settledDpi = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle);
                if (settledDpi > 0 && settledDpi != _layoutNativeDpi) SetRollHeight(false);
            });
        };
        SizeChanged += (_, e) =>
        {
            if (Fence.FileView != FileView.List || Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < .5) return;
            foreach (var item in _files.Items.OfType<ListBoxItem>())
                if (item.Content is StackPanel row)
                    foreach (var label in row.Children.OfType<TextBlock>()) label.MaxWidth = Math.Max(24, ExpandedLogicalWidth - Fence.IconSize - 54);
        };
        Loaded += (_, _) => { MoveTo(Fence.X, Fence.Y); RebuildTabs(); Reload(); SetRollHeight(false); ApplyChameleon(); };
    }

    private string AccentHex(byte alpha) => $"#{alpha:X2}{_accent.Color.R:X2}{_accent.Color.G:X2}{_accent.Color.B:X2}";

    private UIElement Header()
    {
        var minimal = Fence.DisplayMode == FenceDisplayMode.Minimal;
        var header = new Grid { Margin = minimal ? new Thickness(6, 0, 3, 0) : new Thickness(10, 1, 7, 1), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var inlineTabs = minimal && FenceLayout.ShowTabs(Fence);
        if (inlineTabs)
        {
            header.ColumnDefinitions[0].Width = new GridLength(80);
            header.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            Grid.SetColumn(_tabStrip, 1); header.Children.Add(_tabStrip);
            _tabs.Margin = new Thickness(0); _tabs.VerticalAlignment = VerticalAlignment.Center;
            _tabStrip.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        }
        var title = Ui.Text(Fence.Name, minimal ? 10 : 12, _ink, FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap; title.Margin = new Thickness(minimal ? 2 : 25, 0, 8, 0); title.IsHitTestVisible = false;
        AutomationProperties.SetAutomationId(title, "group-title-" + Fence.Id);
        var mover = new Thumb { Cursor = Cursors.SizeAll, Background = Brushes.Transparent, ToolTip = HeaderRevealHint }; _headerMover = mover;
        mover.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent'/></ControlTemplate>");
        var start = new Native.Point(); var origin = new Point(); var haveOrigin = false; var edgeOrigin = 0d; var dragMoved = false; var dragTravel = new Vector(); var clickCount = 0;
        var originalOffset = 0d; var originalLength = 0d; var originalWidth = 0d; var originalHeight = 0d;
        var originalSpan = EdgeSpanMode.Custom; var originalBlockCount = 5; var originalBlockIndex = 0;
        EdgeCollisionGesture? collisionGesture = null;
        mover.DragStarted += (_, _) =>
        {
            SetRollHeight(false); haveOrigin = Native.GetCursorPos(out start); origin = new Point(Fence.X, Fence.Y);
            originalOffset = edgeOrigin = Fence.EdgeOffset; originalLength = Fence.EdgeLength;
            originalWidth = Fence.Width; originalHeight = Fence.Height; originalSpan = Fence.EdgeSpanMode;
            originalBlockCount = Fence.EdgeBlockCount; originalBlockIndex = Fence.EdgeBlockIndex;
            collisionGesture = _app.BeginEdgeCollisionGesture(Fence);
            if (IsEdgeDock)
            {
                var display = DisplayLayout.Select(Fence, AppController.Displays()); var strip = EdgeLayout.Bounds(Fence, display, false);
                edgeOrigin = (Fence.Edge is DockEdge.Left or DockEdge.Right ? strip.Y - display.Y : strip.X - display.X) / display.Scale;
            }
            dragMoved = false; dragTravel = new Vector();
        };
        mover.DragDelta += (_, e) =>
        {
            if (Fence.Locked) return;
            dragTravel += new Vector(e.HorizontalChange, e.VerticalChange);
            if (!dragMoved && Math.Abs(dragTravel.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(dragTravel.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragMoved = true;
            if (IsEdgeDock)
            {
                if (!haveOrigin || !Native.GetCursorPos(out var edgeCursor)) return;
                Fence.Width = originalWidth; Fence.Height = originalHeight;
                Fence.EdgeOffset = originalOffset; Fence.EdgeLength = originalLength; Fence.EdgeSpanMode = originalSpan;
                Fence.EdgeBlockCount = originalBlockCount; Fence.EdgeBlockIndex = originalBlockIndex;
                _app.RestoreEdgeCollisionGesture(collisionGesture, applyBounds: false);
                var dockDisplay = DisplayLayout.Select(Fence, AppController.Displays());
                var delta = Fence.Edge is DockEdge.Left or DockEdge.Right ? edgeCursor.Y - start.Y : edgeCursor.X - start.X;
                if (Math.Abs(delta) > 0) EdgeLayout.UseCustomSpan(Fence, dockDisplay);
                else if (Fence.EdgeSpanMode != EdgeSpanMode.Custom) return;
                var available = (Fence.Edge is DockEdge.Left or DockEdge.Right ? dockDisplay.Height : dockDisplay.Width) / dockDisplay.Scale;
                Fence.EdgeOffset = Math.Clamp(edgeOrigin + delta * AppController.GlobalScale / dockDisplay.Scale, 0, Math.Max(0, available - Fence.EdgeLength));
                _app.PreviewEdgeCollisionGesture(Fence, collisionGesture); return;
            }
            if (haveOrigin && Native.GetCursorPos(out var cursor)) MoveTo(origin.X + (cursor.X - start.X) * AppController.GlobalScale, origin.Y + (cursor.Y - start.Y) * AppController.GlobalScale);
            else { var dpi = VisualTreeHelper.GetDpi(this); MoveTo(Fence.X + e.HorizontalChange * dpi.DpiScaleX * _renderRatio * AppController.GlobalScale, Fence.Y + e.VerticalChange * dpi.DpiScaleY * _renderRatio * AppController.GlobalScale); }
        };
        mover.DragCompleted += (_, e) =>
        {
            if (e.Canceled)
            {
                Fence.X = origin.X; Fence.Y = origin.Y; Fence.Width = originalWidth; Fence.Height = originalHeight;
                Fence.EdgeOffset = originalOffset; Fence.EdgeLength = originalLength; Fence.EdgeSpanMode = originalSpan;
                Fence.EdgeBlockCount = originalBlockCount; Fence.EdgeBlockIndex = originalBlockIndex;
                _app.RestoreEdgeCollisionGesture(collisionGesture); collisionGesture = null;
                if (!_closed) MoveTo(Fence.X, Fence.Y);
                return;
            }
            if (_closed) return;
            if (!dragMoved) { collisionGesture = null; if (clickCount == 1 && Fence.RevealTrigger == RevealTrigger.Click) RevealFromHeader(); return; }
            if (Fence.Locked) { _app.RestoreEdgeCollisionGesture(collisionGesture); collisionGesture = null; return; }
            var display = DisplayLayout.Select(Fence, AppController.Displays());
            if (!IsEdgeDock && _app.Workspace.Preferences.SnapToGrid) { var step = _app.Workspace.Preferences.GridSize * display.Scale; Fence.X = display.X + Math.Round((Fence.X - display.X) / step) * step; Fence.Y = display.Y + Math.Round((Fence.Y - display.Y) / step) * step; }
            if (_app.Workspace.Preferences.SmartSpacing) WorkspaceActions.Snap(Fence, _app.Workspace.Fences, display, _app.Workspace.Preferences.FenceGap);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Native.GetCursorPos(out var drop))
            {
                var target = _app.Windows.FirstOrDefault(w => w != this && Native.GetWindowRect(new WindowInteropHelper(w).Handle, out var rect) && drop.X >= rect.Left && drop.X <= rect.Right && drop.Y >= rect.Top && drop.Y <= rect.Bottom);
                if (target != null) { Ui.Guard(() => _app.Merge(Fence, target.Fence)); collisionGesture = null; return; }
            }
            AppController.ClampToScreen(Fence); MoveTo(Fence.X, Fence.Y); _app.RememberPlacement(Fence);
            collisionGesture = null;
        };
        mover.PreviewMouseLeftButtonDown += (_, e) =>
        {
            clickCount = e.ClickCount;
            if (e.ClickCount != 2) return;
            if (VisuallyCollapsed && (Fence.RevealTrigger is RevealTrigger.Click or RevealTrigger.DoubleClick)) RevealFromHeader();
            else ToggleRoll();
            e.Handled = true;
        };
        header.Children.Add(mover); header.Children.Add(title);
        if (!minimal)
        {
            var glyph = GroupIcons.Create(Fence, Fence.IsSidebar ? _ink : _accent, 18);
            _groupGlyph = glyph;
            glyph.HorizontalAlignment = HorizontalAlignment.Left; glyph.IsHitTestVisible = false; header.Children.Add(glyph);
        }
        var collapseKeyboardAction = false;
        _collapseButton = HeaderButton(VisuallyCollapsed ? "expand" : "collapse", () => { var smooth = !collapseKeyboardAction; collapseKeyboardAction = false; ToggleRoll(smooth); }, VisuallyCollapsed ? L.Text("Развернуть содержимое группы") : IsEdgeDock ? L.Text("Свернуть до полоски у края") : FenceLayout.ShowTabs(Fence) ? L.Text("Свернуть содержимое, оставив верхние вкладки") : L.Text("Свернуть содержимое группы"));
        _collapseButton.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) collapseKeyboardAction = true; };
        _collapseButton.PreviewMouseLeftButtonDown += (_, _) => collapseKeyboardAction = false;
        AutomationProperties.SetAutomationId(_collapseButton, "collapse-group-" + Fence.Id);
        var actions = Ui.Row();
        if (!minimal)
        {
            _searchButton = HeaderButton("search", () => { if (_searchExpanded) { _searchExpanded = false; _search.Text = ""; if (_search.Parent is UIElement parent) parent.Visibility = Visibility.Collapsed; } else ShowFileSearch(); }, L.Text("Поиск в текущей вкладке"));
            AutomationProperties.SetAutomationId(_searchButton, "search-group-" + Fence.Id); actions.Children.Add(_searchButton);
            _pinButton = HeaderButton(Fence.Locked ? "pin-off" : "pin", () => _app.Mutate(() => Fence.Locked = !Fence.Locked), L.Text(Fence.Locked ? "Открепить положение и размер" : "Закрепить положение и размер"));
            AutomationProperties.SetAutomationId(_pinButton, "pin-group-" + Fence.Id); actions.Children.Add(_pinButton);
        }
        actions.Children.Add(_collapseButton);
        actions.Children.Add(Fence.IsSidebar && !minimal ? HeaderButton("settings", () => Editors.Fence(_app, Fence), L.Text("Настройки группы")) : HeaderButton("more", () => { ContextMenu!.PlacementTarget = this; ContextMenu.IsOpen = true; }, L.Text("Меню группы")));
        Grid.SetColumn(actions, inlineTabs ? 2 : 1); header.Children.Add(actions); return header;
    }
    private double HeaderActionSize => Fence.DisplayMode == FenceDisplayMode.Standard ? 32 : 26;
    private double HeaderIconSize => Fence.DisplayMode == FenceDisplayMode.Standard ? 18 : 16;
    private Button HeaderButton(string icon, Action action, string tooltip, double? hitSize = null, double? iconSize = null)
    {
        var button = Ui.Tiny("", action, tooltip); button.Content = Brand.Icon(icon, _muted, iconSize ?? HeaderIconSize);
        button.Resources["Accent"] = _accent; button.Resources["FocusAccent"] = _accent;
        AutomationProperties.SetName(button, tooltip); button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.Width = button.Height = hitSize ?? HeaderActionSize; button.MinHeight = 0; button.Padding = new Thickness(0); button.Margin = new Thickness(0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center; return button;
    }
    private void ShowFileSearch()
    {
        if (Fence.ActiveTab.Widgets.Count > 0 && Fence.ActiveTab.Items.Count == 0 && CurrentPortal == null) return;
        _searchExpanded = true; if (_search.Parent is UIElement parent) parent.Visibility = Visibility.Visible; _search.Focus(); _search.SelectAll();
    }
    internal void MoveTo(double x, double y)
    {
        Fence.X = x; Fence.Y = y;
        StopRevealAnimation(); ApplyRevealVisibility(VisuallyCollapsed);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) { Left = x; Top = y; return; }
        var display = PrepareRevealLayout();
        var bounds = RevealBounds(display, VisuallyCollapsed); _revealMotion.Reset(bounds); ApplyTransientBounds(bounds, display);
    }
    internal void ApplyMonitorBounds() { if (!_preserveRevealBounds) MoveTo(Fence.X, Fence.Y); }
    private double ExpandedLogicalWidth => double.IsFinite(_chrome.Width) && _chrome.Width > 0 ? _chrome.Width : Fence.Width;
    private double WidgetHeightLimit(bool onlyWidgets)
    {
        var fixedSpan = IsEdgeDock && Fence.EdgeSpanMode != EdgeSpanMode.Custom;
        var height = fixedSpan && double.IsFinite(_chrome.Height) ? _chrome.Height : Fence.Height;
        return Math.Max(fixedSpan ? 0 : 80, height - (onlyWidgets ? FenceLayout.HeaderHeight(Fence.DisplayMode) + FenceLayout.TabsHeight(Fence) + 25 : 180));
    }
    internal void RefreshReveal() { RebuildTabs(); SetRollHeight(false); }
    internal void RefreshPeekReveal()
    {
        // Peek changes only transient reveal/fold state. Existing tabs, files and widget
        // controls already describe the active model and do not need rebuilding.
        UpdateTabStates(); SetRollHeight(false);
    }
    internal void ApplyLayer()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0 || _closed) return;
        var child = Native.ChildParent(hwnd) != 0;
        var onTop = ((long)Native.GetWindowLong(hwnd, Native.GwlExStyle) & 8) != 0;
        var floating = ShouldFloat;
        var changeLayer = IsEdgeDock || Fence.Layer != FenceLayer.Desktop || floating
            ? child || onTop != floating
            : !child || onTop;
        if (!changeLayer) { _app.KeepGroupBelowManager(this); return; }
        Native.GetWindowRect(hwnd, out var before);
        _preserveRevealBounds = true;
        try
        {
            if (IsEdgeDock || Fence.Layer != FenceLayer.Desktop) { if (child || onTop != floating) Native.Detach(this, floating); }
            else if (floating) { if (child || !onTop) Native.Detach(this, true); }
            else if (!child || onTop) Native.Attach(this);
        }
        finally
        {
            _preserveRevealBounds = false;
            // Explorer reparenting uses the saved position. Preserve the current frame instead.
            Native.GetWindowRect(hwnd, out var after);
            if (before.Left != after.Left || before.Top != after.Top || before.Right != after.Right || before.Bottom != after.Bottom)
            {
                var point = new Native.Point { X = before.Left, Y = before.Top }; var parent = Native.ChildParent(hwnd);
                if (parent != 0) Native.ScreenToClient(parent, ref point);
                Native.SetWindowPos(hwnd, Native.HwndTop, point.X, point.Y, before.Right - before.Left, before.Bottom - before.Top, Native.SwpNoActivate | Native.SwpNoZOrder);
            }
        }
        _app.KeepGroupBelowManager(this);
    }
    internal void ToggleRoll(bool animate = true)
    {
        if (KeepsExpanded) return;
        _openedAutomatically = false;
        var collapsed = _app.IsPeek || !VisuallyCollapsed;
        if (_app.IsPeek) _app.Peek();
        Fence.RolledUp = collapsed;
        if (!collapsed) Fence.ActiveTab.Collapsed = false;
        _hoverExpanded = IsEdgeDock && !collapsed; _reveal.Reset(_hoverExpanded); _suppressRevealUntilLeave = collapsed; RebuildTabs(); SetRollHeight(animate); _app.ScheduleSave();
    }
    private void SetRollHeight(bool animate = true)
    {
        var rolled = VisuallyCollapsed;
        if (!Fence.ContentCollapsed)
        {
            AppController.ClampToScreen(Fence);
        }
        TransitionReveal(rolled, animate);
        if (_collapseButton != null)
        {
            _collapseButton.IsEnabled = !KeepsExpanded;
            _collapseButton.Content = Brand.Icon(rolled ? "expand" : "collapse", _muted, HeaderIconSize);
            _collapseButton.ToolTip = KeepsExpanded ? AlwaysOpenHint : rolled ? L.Text("Развернуть содержимое группы") : IsEdgeDock ? L.Text("Свернуть до полоски у края") : FenceLayout.ShowTabs(Fence) ? L.Text("Свернуть содержимое, оставив верхние вкладки") : L.Text("Свернуть содержимое группы");
            ToolTipService.SetShowOnDisabled(_collapseButton, true);
            AutomationProperties.SetName(_collapseButton, (string)_collapseButton.ToolTip);
        }
        if (_headerMover != null) _headerMover.ToolTip = HeaderRevealHint;
        if (_pinButton != null)
        {
            _pinButton.Content = Brand.Icon(Fence.Locked ? "pin-off" : "pin", Fence.Locked ? _accent : _muted, HeaderIconSize);
            _pinButton.ToolTip = L.Text(Fence.Locked ? "Открепить положение и размер" : "Закрепить положение и размер");
            AutomationProperties.SetName(_pinButton, (string)_pinButton.ToolTip);
        }
        RefreshResizeHandles();
    }
    internal void ApplyChameleon()
    {
        var canFade = _app.Workspace.Preferences.Chameleon && !_app.IsPeek && !_hoverExpanded && !IsMouseOver;
        var cursorInside = canFade && !_closed && IsVisible && Native.GetCursorPos(out var cursor) && ContainsPointer(cursor);
        var target = canFade && !cursorInside ? _app.Workspace.Preferences.ChameleonOpacity : 1;
        if (_opacityTarget == target) return; _opacityTarget = target;
        if (SystemParameters.ClientAreaAnimation) BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(200))); else Opacity = target;
    }
    private sealed record TabVisual(FenceTab Tab, Border Group, Button Select, Button Fold, TextBlock Label, string Signature)
    {
        internal bool? FoldCollapsed { get; set; }
    }
    private static string TabVisualSignature(FenceTab tab) => JsonSerializer.Serialize(new { tab.Name, tab.Color, tab.PortalPath });
    internal void RebuildTabs(bool animate = false)
    {
        if (_groupGlyph != null)
        {
            var glyph = GroupIcons.Create(Fence, Fence.IsSidebar ? _ink : _accent, 18);
            _groupGlyph.Source = glyph.Source; _groupGlyph.Tag = glyph.Tag;
        }
        var show = FenceLayout.ShowTabs(Fence);
        if (Fence.DisplayMode == FenceDisplayMode.Minimal && _headerView is Grid oldHeader && (_tabStrip.Parent == oldHeader) != show)
        {
            oldHeader.Children.Remove(_tabStrip); _layout.Children.Remove(oldHeader);
            _headerView = Header(); _layout.Children.Insert(0, _headerView);
        }
        _tabStrip.Visibility = _tabs.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _layout.RowDefinitions[1].Height = new GridLength(FenceLayout.TabsHeight(Fence));
        if (!show) { _tabMotion.Hide(); CancelTabDropPreview(); StopTabReorderMotion(); return; }
        var configuration = JsonSerializer.Serialize(new { Language = L.Language, Fence.TabStyle, Fence.DisplayMode, Fence.ContentLocked, AlwaysOpen = KeepsExpanded });
        var signature = configuration + JsonSerializer.Serialize(Fence.Tabs.OrderBy(tab => tab.Id).Select(tab => new { tab.Id, tab.Name, tab.Color, tab.PortalPath }));
        if (_tabsSignature == signature && Fence.Tabs.All(tab => _tabViews.TryGetValue(tab.Id, out var view) && ReferenceEquals(view.Tab, tab)))
        {
            var ordered = Fence.Tabs.Select(tab => _tabViews[tab.Id].Group).ToArray();
            if (!_tabs.Children.OfType<Border>().SequenceEqual(ordered))
            {
                var add = _tabs.Children.OfType<Button>().LastOrDefault();
                _tabs.Children.Clear(); foreach (var group in ordered) _tabs.Children.Add(group); if (add != null) _tabs.Children.Add(add);
                foreach (var view in _tabViews.Values) RefreshTabMenu(view);
                _tabs.UpdateLayout();
            }
            UpdateTabStates(); SyncTabSelection(animate); return;
        }
        var existingViews = configuration == _tabConfigurationSignature ? _tabViews.ToDictionary(pair => pair.Key, pair => pair.Value) : [];
        StopTabReorderMotion();
        _tabsSignature = signature; _tabConfigurationSignature = configuration; _tabViews.Clear(); _tabs.Children.Clear(); _tabMotion.Hide();
        foreach (var tab in Fence.Tabs)
        {
            var visualSignature = TabVisualSignature(tab);
            if (existingViews.TryGetValue(tab.Id, out var existing) && ReferenceEquals(existing.Tab, tab) && existing.Signature == visualSignature)
            {
                RefreshTabMenu(existing); _tabViews.Add(tab.Id, existing); _tabs.Children.Add(existing.Group); continue;
            }
            var group = new Border { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 3, 0) };
            BindTabContextMenu(group, tab);
            var row = new Grid();
            var keyboardAction = false;
            var button = Ui.Tiny(tab.Name, () => { var smooth = !keyboardAction; keyboardAction = false; if (tab == Fence.ActiveTab) ToggleTab(tab, smooth); else ActivateTab(tab, smooth); }, (tab.PortalPath ?? L.Text("Ссылки на файлы")) + (KeepsExpanded ? "" : L.Text("\nЩелчок по активной вкладке — свернуть / развернуть")));
            button.Resources["Accent"] = _accent; button.Resources["FocusAccent"] = _accent;
            var label = new TextBlock { Text = tab.Name, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            button.Content = label;
            button.FontFamily = Ui.AppFont; button.FontWeight = FontWeights.Normal; button.FontSize = Fence.DisplayMode == FenceDisplayMode.Minimal ? 10 : 11; button.BorderThickness = new Thickness(0); button.Background = Brushes.Transparent;
            var textWidth = new FormattedText(tab.Name, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Ui.AppFont, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), button.FontSize, _ink, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
            button.Width = Math.Clamp(Math.Ceiling(textWidth) + (Fence.DisplayMode == FenceDisplayMode.Minimal ? 17 : 23), 32, Fence.DisplayMode == FenceDisplayMode.Minimal ? 104 : 144);
            button.Margin = new Thickness(0); button.Padding = new Thickness(Fence.DisplayMode == FenceDisplayMode.Minimal ? 7 : 10, 2, Fence.DisplayMode == FenceDisplayMode.Minimal ? 7 : 10, 3); button.Height = Fence.DisplayMode == FenceDisplayMode.Standard ? 24 : 22; button.MinHeight = 0;
            AutomationProperties.SetAutomationId(button, "tab-select-" + tab.Id); AutomationProperties.SetName(button, L.Text("Вкладка ") + tab.Name);
            button.AllowDrop = true;
            button.DragEnter += (_, e) => { if (tab != Fence.ActiveTab && e.Data.GetDataPresent(DataFormats.FileDrop)) ActivateTab(tab); };
            button.DragOver += (_, e) =>
            {
                if (e.Data.GetData("WalleFences.Tab") is not TabDrag moving) return;
                AutoScrollTabDrag(e.GetPosition(_tabStrip));
                var after = e.GetPosition(group).X >= group.ActualWidth / 2;
                e.Effects = PreviewTabDrop(moving, tab.Id, after) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true;
            };
            button.Drop += (_, e) =>
            {
                if (e.Data.GetData("WalleFences.Tab") is TabDrag moving) { var after = e.GetPosition(group).X >= group.ActualWidth / 2; Ui.Guard(() => DropTab(moving, tab.Id, after)); e.Handled = true; }
                else { if (tab != Fence.ActiveTab) ActivateTab(tab); OnDrop(button, e); }
            };
            var tabOrigin = new Point();
            button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) keyboardAction = true; };
            button.PreviewMouseLeftButtonDown += (_, e) => { keyboardAction = false; tabOrigin = e.GetPosition(this); };
            button.PreviewMouseMove += (_, e) =>
            {
                if (Fence.ContentLocked || e.LeftButton != MouseButtonState.Pressed || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || (e.GetPosition(this) - tabOrigin).Length < 6) return;
                var data = new DataObject("WalleFences.Tab", new TabDrag(Fence.Id, tab.Id));
                var cancelled = false;
                QueryContinueDragEventHandler cancellation = (_, args) => { if (args.EscapePressed) cancelled = true; };
                button.QueryContinueDrag += cancellation;
                DragDropEffects result;
                StartTabDragFeedback(group);
                try { result = DragDrop.DoDragDrop(button, data, DragDropEffects.Move); }
                finally { button.QueryContinueDrag -= cancellation; StopTabDragFeedback(group); foreach (var window in _app.Windows.ToArray()) window.CancelTabDropPreview(); }
                if (result == DragDropEffects.None && !cancelled && !_closed && Fence.Tabs.Count > 1 && Native.GetCursorPos(out var pointer))
                {
                    var scale = AppController.GlobalScale;
                    if (CanDetachTabAt(pointer) && AppController.Displays().Any(d => new LayoutRect(d.X, d.Y, d.Width, d.Height).Contains(pointer.X * scale, pointer.Y * scale))) _app.DetachTab(Fence, tab, new Point(pointer.X * scale, pointer.Y * scale));
                }
                e.Handled = true;
            };
            var foldKeyboardAction = false;
            var fold = HeaderButton("collapse", () => { var smooth = !foldKeyboardAction; foldKeyboardAction = false; ToggleTab(tab, smooth); }, "", 18, 12);
            fold.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) foldKeyboardAction = true; };
            fold.PreviewMouseLeftButtonDown += (_, _) => foldKeyboardAction = false;
            fold.HorizontalAlignment = HorizontalAlignment.Right; fold.Margin = new Thickness(0, 0, 2, 0); fold.Background = Ui.Brush(Fence.Color, .94); fold.Height = button.Height - 4;
            AutomationProperties.SetAutomationId(fold, "tab-collapse-" + tab.Id);
            var view = new TabVisual(tab, group, button, fold, label, visualSignature); _tabViews.Add(tab.Id, view);
            group.MouseEnter += (_, _) => UpdateTabFold(view); group.MouseLeave += (_, _) => UpdateTabFold(view);
            group.IsKeyboardFocusWithinChanged += (_, _) => UpdateTabFold(view);
            row.Children.Add(button); row.Children.Add(fold); group.Child = row; _tabs.Children.Add(group);
        }
        var addTab = HeaderButton("add", () => Ui.Guard(() => Editors.Tab(_app, Fence)), L.Text("Добавить вкладку"), 22, 13); addTab.IsEnabled = !Fence.ContentLocked; addTab.Opacity = .65; _tabs.Children.Add(addTab);
        UpdateTabStates(); _tabs.UpdateLayout(); SyncTabSelection(false);
    }
    private void UpdateTabStates()
    {
        foreach (var view in _tabViews.Values)
        {
            var selected = view.Tab == Fence.ActiveTab;
            var collapsed = !KeepsExpanded && (selected ? VisuallyCollapsed : Fence.RolledUp || view.Tab.Collapsed);
            view.Select.Foreground = selected ? _ink : _muted;
            view.Label.FontFamily = selected ? Ui.MediumFont : Ui.AppFont;
            view.Label.FontWeight = selected ? FontWeights.Medium : FontWeights.Normal;
            if (view.FoldCollapsed != collapsed)
            {
                view.Fold.Content = Brand.Icon(collapsed ? "expand" : "collapse", _muted, 12);
                view.Fold.ToolTip = (collapsed ? L.Text("Развернуть вкладку") : L.Text("Свернуть вкладку")) + " · " + view.Tab.Name;
                AutomationProperties.SetName(view.Fold, (collapsed ? L.Text("Развернуть ") : L.Text("Свернуть ")) + view.Tab.Name);
                view.FoldCollapsed = collapsed;
            }
            UpdateTabFold(view);
        }
    }
    private void UpdateTabFold(TabVisual view)
    {
        var visible = !KeepsExpanded && (view.Group.IsMouseOver || view.Group.IsKeyboardFocusWithin || view.Group.ContextMenu?.IsOpen == true);
        view.Fold.IsEnabled = !KeepsExpanded;
        view.Fold.Opacity = visible ? .8 : 0; view.Fold.IsHitTestVisible = visible;
        view.Label.Margin = new Thickness(0, 0, visible ? 14 : 0, 0);
    }
    private void SyncTabSelection(bool animate)
    {
        if (_closed || !HasVisibleTabStrip || !_tabViews.TryGetValue(Fence.ActiveTab.Id, out var view) || view.Group.ActualWidth <= 0) { _tabMotion.Hide(); return; }
        var flat = Fence.TabStyle == TabStyle.Flat;
        _tabSelection.Background = Ui.Brush(Fence.ActiveTab.Color ?? Fence.Accent, flat ? .085 : .105);
        _tabSelection.CornerRadius = new CornerRadius(Fence.TabStyle == TabStyle.Rounded ? 5 : flat ? 4 : 0);
        _tabSelection.BorderThickness = Fence.TabStyle == TabStyle.Segmented ? new Thickness(1, 1, 1, 0) : new Thickness(0);
        _tabSelection.BorderBrush = Ui.Brush(Fence.ActiveTab.Color ?? Fence.Accent, .2);
        _tabIndicator.Background = Ui.Brush(Fence.ActiveTab.Color ?? Fence.Accent, .9);
        _tabIndicator.CornerRadius = new CornerRadius(flat ? 0 : 1);
        _tabMotion.MoveTo(ActiveTabBounds, animate);
        if (animate || !_tabMotion.IsAnimating)
        {
            var bounds = ActiveTabBounds;
            if (bounds.Left < _tabStrip.HorizontalOffset) _tabStrip.ScrollToHorizontalOffset(bounds.Left);
            else if (bounds.Right > _tabStrip.HorizontalOffset + _tabStrip.ViewportWidth) _tabStrip.ScrollToHorizontalOffset(bounds.Right - _tabStrip.ViewportWidth);
        }
    }
    internal void CycleTab(int direction)
    {
        if (direction == 0) return;
        var index = Fence.Tabs.IndexOf(Fence.ActiveTab);
        ActivateTab(Fence.Tabs[(index + Math.Sign(direction) + Fence.Tabs.Count) % Fence.Tabs.Count], false);
    }
    internal void ActivateTab(FenceTab tab, bool animate = true)
    {
        if (!Fence.Tabs.Contains(tab)) return;
        _openedAutomatically = false;
        var previous = Fence.ActiveTab;
        RequestTabContentMotion(tab, previous, animate);
        var restoreWidgetFocus = _widgets.IsKeyboardFocusWithin;
        Fence.ActiveTabId = tab.Id; Fence.RolledUp = false; tab.Collapsed = false;
        _hoverExpanded = IsEdgeDock || KeepsExpanded; _reveal.Reset(_hoverExpanded); _suppressRevealUntilLeave = false; _search.Text = "";
        RebuildTabs(animate); SetRollHeight(animate); Reload();
        if (tab.Widgets.Count > 0)
        {
            if (restoreWidgetFocus && tab.Items.Count == 0 && tab.PortalPath == null) _widgetScroll.Focus();
            ResetWidgetScroll();
        }
        _app.ScheduleSave();
    }
    internal void ToggleTab(FenceTab tab, bool animate = true)
    {
        if (!Fence.Tabs.Contains(tab)) return;
        _openedAutomatically = false;
        if (KeepsExpanded) { ActivateTab(tab, animate); return; }
        var changedTab = tab != Fence.ActiveTab; var collapsed = !changedTab ? !VisuallyCollapsed : !(Fence.RolledUp || tab.Collapsed);
        if (changedTab) RequestTabContentMotion(tab, Fence.ActiveTab, animate && !collapsed);
        if (IsEdgeDock && collapsed && _app.IsPeek) _app.Peek();
        Fence.ActiveTabId = tab.Id; Fence.RolledUp = false; tab.Collapsed = collapsed;
        _hoverExpanded = IsEdgeDock && !collapsed; _reveal.Reset(_hoverExpanded); _suppressRevealUntilLeave = collapsed; if (changedTab) _search.Text = "";
        RebuildTabs(changedTab && animate); SetRollHeight(animate); if (changedTab) Reload(); _app.ScheduleSave();
    }
    internal async void Reload()
    {
        var version = ++_loadVersion; var tab = Fence.ActiveTab; var portal = CurrentPortal; var hidden = _app.Workspace.Preferences.ShowHiddenFiles; var sort = Fence.Sort; var references = FileCatalog.References(tab);
        var contentMotion = _pendingTabContentMotion?.TabId == tab.Id ? _pendingTabContentMotion : null; _pendingTabContentMotion = null;
        if (_entriesTabId != tab.Id)
        {
            _files.Items.Clear(); _empty.Visibility = Visibility.Collapsed; _count.Text = L.Text("Загрузка…"); _renderSignature = null;
        }
        if (_renderSignature == null) _count.Text = L.Text("Загрузка…"); RenderNavigation(); WatchPortal(portal);
        _widgets.Sync();
        var onlyWidgets = tab.Widgets.Count > 0 && tab.Items.Count == 0 && portal == null;
        _widgetScroll.Visibility = tab.Widgets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _widgetScroll.MaxHeight = WidgetHeightLimit(onlyWidgets);
        if (_search.Parent is UIElement searchParent) searchParent.Visibility = !onlyWidgets && (_searchExpanded || _search.Text.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
        if (_searchButton != null) _searchButton.Visibility = onlyWidgets ? Visibility.Collapsed : Visibility.Visible;
        _files.Visibility = onlyWidgets ? Visibility.Collapsed : Visibility.Visible;
        _count.Visibility = onlyWidgets ? Visibility.Collapsed : Visibility.Visible;
        if (contentMotion != null && tab.Widgets.Count > 0)
        {
            if (!VisuallyCollapsed) _tabContentMotion.Enter(true, offsetX: contentMotion.OffsetX, offsetY: 0);
            contentMotion = null;
        }
        try
        {
            var entries = await Task.Run(() => FileCatalog.Sort(portal != null ? FileCatalog.ReadFolder(portal, hidden) : references.Select(FileCatalog.Entry), sort));
            if (_closed || version != _loadVersion || Fence.ActiveTab.Id != tab.Id) return; _entries = entries; _entriesTabId = tab.Id; _entriesPortal = portal; RenderItems();
            if (contentMotion != null && !VisuallyCollapsed) _tabContentMotion.Enter(true, offsetX: contentMotion.OffsetX, offsetY: 0);
        }
        catch (Exception ex) { if (!_closed && version == _loadVersion) { _empty.Text = L.Text("Не удалось прочитать папку"); _empty.Visibility = Visibility.Visible; _app.Log(ex); } }
    }
    private void RenderNavigation()
    {
        var portal = CurrentPortal;
        var signature = Fence.ActiveTab.Id + "|" + portal;
        if (_navigationSignature == signature) return;
        _navigationSignature = signature; _navigation.Children.Clear();
        if (portal == null) return;
        var pathText = new TextBox { Text = portal, FontSize = 10, Padding = new Thickness(4, 2, 4, 2), ToolTip = L.Text("Путь папки · Enter — перейти"), Background = new SolidColorBrush(_ink.Color) { Opacity = .08 }, Foreground = _ink, CaretBrush = _ink, BorderThickness = new Thickness(0) };
        pathText.SelectionBrush = _accent; pathText.Resources["FocusAccent"] = _accent; pathText.Resources["Muted"] = _muted;
        pathText.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Ui.Guard(() => Navigate(Path.GetFullPath(Environment.ExpandEnvironmentVariables(pathText.Text.Trim())))); e.Handled = true; } };
        var row = new DockPanel(); var buttons = Ui.Row(HeaderButton("back", GoBack, L.Text("Назад"), 32, 18), HeaderButton("up", GoUp, L.Text("Родительская папка"), 32, 18), HeaderButton("home", () => Navigate(Fence.ActiveTab.PortalPath!), L.Text("Корень портала"), 32, 18));
        DockPanel.SetDock(buttons, Dock.Left); row.Children.Add(buttons); row.Children.Add(pathText); _navigation.Children.Add(row);
    }
    internal void Navigate(string path)
    {
        if (!Directory.Exists(path)) { Ui.Error(L.Text("Папка недоступна.")); return; }
        var tab = Fence.ActiveTab;
        if (!_history.TryGetValue(tab.Id, out var history)) _history[tab.Id] = history = new();
        if (CurrentPortal is { } current && !PathExtensions.Same(current, path)) history.Push(current);
        _portalLocations[tab.Id] = path; _search.Text = ""; Reload();
    }
    private void GoBack() { if (_history.TryGetValue(Fence.ActiveTab.Id, out var history) && history.TryPop(out var path)) { _portalLocations[Fence.ActiveTab.Id] = path; _search.Text = ""; Reload(); } }
    private void GoUp() { if (CurrentPortal is { } current && Directory.GetParent(current) is { } parent) Navigate(parent.FullName); }
    private void WatchPortal(string? path)
    {
        if (string.Equals(path, _watchedPath, StringComparison.OrdinalIgnoreCase)) return;
        _portalWatcher?.Dispose(); _portalWatcher = null; _watchedPath = path;
        if (path == null || !Directory.Exists(path)) return;
        try
        {
            _portalWatcher = new FileSystemWatcher(path) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
            FileSystemEventHandler refresh = (_, _) => _app.QueueRefresh();
            _portalWatcher.Created += refresh; _portalWatcher.Deleted += refresh; _portalWatcher.Changed += refresh; _portalWatcher.Renamed += (_, _) => _app.QueueRefresh(); _portalWatcher.Error += (_, _) => _app.QueueRefresh(); _portalWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { _app.Log(ex); }
    }
    private void RenderItems()
    {
        if (_entriesTabId != Fence.ActiveTab.Id || _entriesPortal != CurrentPortal) return;
        var query = _search.Text.Trim(); var filtered = _entries.Where(e => query.Length == 0 || e.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        var renderSignature = JsonSerializer.Serialize(new { Query = query, Tab = Fence.ActiveTab.Id, Portal = CurrentPortal, Items = filtered.Take(1000), Total = filtered.Count });
        if (_renderSignature == renderSignature) return;
        _renderSignature = renderSignature;
        var selected = _files.SelectedItems.Cast<ListBoxItem>().Select(i => ((FileEntry)i.Tag).Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = _files.Items.Cast<ListBoxItem>().ToDictionary(i => ((FileEntry)i.Tag).Path, StringComparer.OrdinalIgnoreCase);
        var paths = filtered.Take(1000).Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _files.Items.Cast<ListBoxItem>().Where(i => !paths.Contains(((FileEntry)i.Tag).Path)).ToArray()) _files.Items.Remove(item);
        var index = 0;
        foreach (var entry in filtered.Take(1000))
        {
            if (existing.TryGetValue(entry.Path, out var reusable) && Equals(reusable.Tag, entry))
            {
                if (_files.Items.IndexOf(reusable) != index) { _files.Items.Remove(reusable); _files.Items.Insert(index, reusable); reusable.IsSelected = selected.Contains(entry.Path); }
                index++; continue;
            }
            if (existing.TryGetValue(entry.Path, out var outdated)) _files.Items.Remove(outdated);
            var width = Math.Max(72, Fence.IconSize + 35); var icon = Native.Icon(entry.Path);
            var list = Fence.FileView == FileView.List;
            var content = new StackPanel { Width = list ? double.NaN : width, Orientation = list ? Orientation.Horizontal : Orientation.Vertical, Margin = new Thickness(Fence.IconSpacing / 2.0, Fence.IconSpacing / 2.0 + 1, Fence.IconSpacing / 2.0, Fence.IconSpacing / 2.0 + 1), Opacity = entry.Exists ? 1 : .45 };
            var visual = new Grid { Width = Fence.IconSize, Height = Fence.IconSize, Margin = new Thickness(0, 3, 0, 6) };
            if (icon != null)
            {
                visual.Children.Add(new Image { Source = icon, Stretch = Stretch.Uniform });
                if (Fence.TintIcons) visual.Children.Add(new Border { Background = Ui.Brush(Fence.Accent), Opacity = .78, OpacityMask = new ImageBrush(icon) { Stretch = Stretch.Uniform } });
            }
            else visual.Children.Add(Brand.Icon(entry.IsDirectory ? "folder" : "file", Ui.Brush(Fence.Accent), Fence.IconSize));
            if (list) visual.Margin = new Thickness(5, 4, 10, 4);
            content.Children.Add(visual);
            if (Fence.ShowLabels) content.Children.Add(new TextBlock { Text = entry.Name, FontFamily = Ui.AppFont, Foreground = _ink, FontSize = Fence.LabelFontSize, TextAlignment = list ? TextAlignment.Left : TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = list ? Math.Max(24, ExpandedLogicalWidth - Fence.IconSize - 54) : width, MaxHeight = Fence.LabelFontSize * 2.8 + 1, LineHeight = Fence.LabelFontSize * 1.4, VerticalAlignment = VerticalAlignment.Center });
            var item = new ListBoxItem { Content = content, Tag = entry, Padding = new Thickness(0), Margin = new Thickness(0), ToolTip = entry.Path + (entry.Exists ? "" : L.Text("\nФайл не найден")), ContextMenu = ItemMenu(entry) };
            item.ContextMenuOpening += (_, _) => item.ContextMenu = ItemMenu((FileEntry)item.Tag);
            AutomationProperties.SetName(item, entry.Name); _files.Items.Insert(index++, item); if (selected.Contains(entry.Path)) item.IsSelected = true;
        }
        _count.Text = filtered.Count > 1000 ? L.Format("Показано 1000 из {0} · уточните поиск", filtered.Count) : L.Format("{0} элементов", filtered.Count) + (query.Length > 0 ? L.Format(" из {0}", _entries.Count) : "");
        if (Fence.ActiveTab.Widgets.Count > 0 && Fence.ActiveTab.Items.Count == 0 && Fence.ActiveTab.PortalPath == null) _count.Text = L.Format("Виджетов: {0}", Fence.ActiveTab.Widgets.Count);
        _empty.Text = query.Length > 0 ? L.Text("Ничего не найдено") : CurrentPortal is { } path && !Directory.Exists(path) ? L.Text("Папка недоступна\nОткройте меню вкладки, чтобы изменить путь") : CurrentPortal != null ? L.Text("Папка пуста") : L.Text("Перетащите сюда файлы\nили добавьте их через меню группы");
        _empty.Visibility = filtered.Count == 0 && Fence.ActiveTab.Widgets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ResetWidgetScroll()
    {
        var tabId = Fence.ActiveTab.Id; _widgetScroll.ScrollToTop();
        // Keyboard focus on a later module can queue BringIntoView after a tab swap.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!_closed && Fence.ActiveTab.Id == tabId && _widgetScroll.IsVisible) _widgetScroll.ScrollToTop();
        }));
    }
    internal void FocusFiles()
    {
        _openedAutomatically = false;
        _keyboardAccess = true; _accessOnTop = true; ApplyLayer(); Activate();
        var tab = Fence.ActiveTab;
        if (tab.Widgets.Count > 0 && tab.Items.Count == 0 && tab.PortalPath == null)
        {
            // Focus the viewport itself, so opening does not jump to the first action
            // near the bottom of an overview (driver link, focus timer, or note editor).
            _widgetScroll.Focus(); ResetWidgetScroll();
        }
        else if (_files.Items.Count > 0) { _files.SelectedIndex = 0; _files.Focus(); }
        else if (_widgets.IsVisible) { _widgetScroll.Focus(); ResetWidgetScroll(); }
        else ShowFileSearch();
    }
    private void OpenEntry(FileEntry entry)
    {
        if (CurrentPortal != null && entry.IsDirectory && !Fence.PortalFoldersInExplorer) { Navigate(entry.Path); return; }
        else { Native.Open(entry.Path, CurrentPortal == null ? FileCatalog.Launch(Fence.ActiveTab, entry.Path) : null); if (_app.IsPeek) _app.Peek(); }
        if (Fence.CloseAfterLaunch && !KeepsExpanded) { Fence.RolledUp = true; _hoverExpanded = false; _reveal.Reset(); _suppressRevealUntilLeave = true; RebuildTabs(); SetRollHeight(); _app.ScheduleSave(); }
    }
    private static ListBoxItem? FindItem(DependencyObject? source)
    {
        while (source != null && source is not ListBoxItem) source = VisualTreeHelper.GetParent(source);
        return source as ListBoxItem;
    }
    private void FileKeys(object sender, KeyEventArgs e)
    {
        var selected = SelectedPaths();
        if (e.Key == Key.Enter && _files.SelectedItem is ListBoxItem { Tag: FileEntry entry }) { OpenEntry(entry); e.Handled = true; }
        if (e.Key == Key.Delete && CurrentPortal == null && selected.Length > 0) { Ui.Guard(() => _app.Mutate(() => FileCatalog.RemoveReferences(Fence.ActiveTab, selected))); e.Handled = true; }
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && selected.Length > 0) { Clipboard.SetDataObject(FileTransferData(selected), true); e.Handled = true; }
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Clipboard.ContainsFileDropList()) { Ui.Guard(() => ReceivePaths(Clipboard.GetFileDropList().Cast<string>().ToArray(), Clipboard.GetDataObject(), false)); e.Handled = true; }
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { _files.SelectAll(); e.Handled = true; }
    }
    private string[] SelectedPaths() => _files.SelectedItems.Cast<ListBoxItem>().Select(i => ((FileEntry)i.Tag).Path).ToArray();
    private void DragFiles(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || FindItem(e.OriginalSource as DependencyObject) == null) return;
        var current = e.GetPosition(_files); if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var paths = SelectedPaths(); if (paths.Length == 0) return;
        var data = FileTransferData(paths);
        DragDrop.DoDragDrop(_files, data, DragDropEffects.Copy | DragDropEffects.Move); e.Handled = true;
    }
    internal DataObject FileTransferData(string[] paths)
    {
        var data = new DataObject(); data.SetData(DataFormats.FileDrop, paths);
        if (CurrentPortal == null) data.SetData("WalleFences.Items", JsonSerializer.Serialize(new ItemDrag(Fence.Id, Fence.ActiveTab.Id, paths)));
        return data;
    }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("WalleFences.Tab") is TabDrag moving) { AutoScrollTabDrag(e.GetPosition(_tabStrip)); e.Effects = PreviewTabDrop(moving, null, false) ? DragDropEffects.Move : DragDropEffects.None; }
        else { CancelTabDropPreview(); e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? CurrentPortal != null ? DragDropEffects.Copy : e.Data.GetDataPresent("WalleFences.Items") ? DragDropEffects.Move : DragDropEffects.Copy : DragDropEffects.None; }
        e.Handled = true;
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("WalleFences.Tab") is TabDrag moving)
        {
            Ui.Guard(() => DropTab(moving, null, false)); e.Handled = true; return;
        }
        e.Handled = true;
        Ui.Guard(() =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            ReceivePaths(paths, e.Data, (Keyboard.Modifiers & ModifierKeys.Control) == 0);
        });
    }
    internal void ReceivePaths(string[] paths, IDataObject? data, bool move)
    {
        if (CurrentPortal == null && data?.GetData("WalleFences.Items") is string serialized)
        {
            var drag = JsonSerializer.Deserialize<ItemDrag>(serialized);
            var source = _app.Workspace.Fences.FirstOrDefault(f => f.Id == drag?.FenceId)?.Tabs.FirstOrDefault(t => t.Id == drag!.TabId);
            if (source != null) { TransferPaths(source, paths, move); return; }
        }
        AddPaths(paths);
    }
    internal void AddPaths(string[] paths)
    {
        if (Fence.ContentLocked) { Ui.Error(L.Text("Содержимое группы защищено. Отключите защиту в настройках группы.")); return; }
        if (CurrentPortal is { } portal)
        {
            if (!Ui.Confirm(L.Format("Скопировать {0} элементов в папку «{1}»? Существующие файлы будут пропущены.", paths.Length, portal))) return;
            _ = CopyToPortal(paths, portal);
        }
        else
        {
            var references = ShortcutReferences.Read(paths);
            _app.Mutate(() => FileCatalog.ImportReferences(Fence.ActiveTab, references));
        }
    }
    internal void TransferPaths(FenceTab source, string[] paths, bool move)
    {
        WorkspaceActions.RequireEditable(Fence);
        var references = ShortcutReferences.Read(source, paths);
        _app.Mutate(() => FileCatalog.TransferReferences(source, Fence.ActiveTab, references, move));
    }
    private async Task CopyToPortal(string[] paths, string portal)
    {
        try
        {
            var count = await Task.Run(() =>
            {
                var done = 0;
                foreach (var path in paths)
                {
                    var destination = Path.Combine(portal, Path.GetFileName(path));
                    if (File.Exists(destination) || Directory.Exists(destination)) continue;
                    if (Directory.Exists(path))
                    {
                        var sourceKey = PathExtensions.Key(path);
                        if (PathExtensions.Key(destination).StartsWith(sourceKey + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException(L.Text("Нельзя копировать папку внутрь самой себя."));
                        CopyDirectory(path, destination);
                    }
                    else File.Copy(path, destination, false);
                    done++;
                }
                return done;
            });
            _app.SetStatus(L.Format("Скопировано элементов: {0}", count)); Reload();
        }
        catch (Exception ex) { _app.Log(ex); Ui.Error(L.Text("Копирование не завершено: ") + ex.Message); Reload(); }
    }
    private static void CopyDirectory(string source, string target)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException(L.Text("Копирование ссылок на папки не поддерживается."));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
        foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
    private static MenuItem MenuHeading(string text, string? icon = null)
    {
        var item = new MenuItem { Header = text };
        if (icon != null) item.Icon = Brand.Icon(icon, Ui.Muted, 16);
        AutomationProperties.SetName(item, text); return item;
    }
    private static MenuItem Menu(string text, Action action, string? icon = null) { var item = MenuHeading(text, icon); item.Click += (_, _) => Ui.Guard(action); return item; }
    private MenuItem ContentMenu(string text, Action action, string? icon = null) { var item = Menu(text, action, icon); item.IsEnabled = !Fence.ContentLocked; return item; }
    private ContextMenu FenceMenu()
    {
        var menu = new ContextMenu();
        AutomationProperties.SetAutomationId(menu, "group-context-menu");
        menu.Items.Add(Menu(L.Text("Настроить группу…"), () => Editors.Fence(_app, Fence), "settings"));
        var add = MenuHeading(L.Text("Добавить"), "plus");
        AutomationProperties.SetAutomationId(add, "group-add-menu");
        add.Items.Add(ContentMenu(L.Text("Добавить файлы…"), () => Editors.AddFiles(this, _app), "file"));
        var programs = ContentMenu(L.Text("Добавить программы…"), () => Editors.AddPrograms(this, _app), "desktop");
        programs.IsEnabled = !Fence.ContentLocked && string.IsNullOrEmpty(Fence.ActiveTab.PortalPath);
        if (!programs.IsEnabled && !Fence.ContentLocked) { programs.ToolTip = L.Text("В портал нельзя добавить ссылку: выберите обычную вкладку."); ToolTipService.SetShowOnDisabled(programs, true); }
        add.Items.Add(programs);
        var widget = Menu(L.Text("Добавить виджет…"), () => _app.AddWidgetPanel(this, Fence), "widgets");
        AutomationProperties.SetAutomationId(widget, "group-add-widget");
        widget.ToolTip = L.Text("Каждый виджет появится отдельной панелью в текущем наборе.");
        add.Items.Add(widget);
        add.Items.Add(ContentMenu(L.Text("Добавить вкладку / портал…"), () => Editors.Tab(_app, Fence), "add"));
        menu.Items.Add(add);
        var tabList = MenuHeading(L.Text("Вкладки"), "tabs");
        AutomationProperties.SetAutomationId(tabList, "group-tabs-menu");
        foreach (var tab in Fence.Tabs)
        {
            var item = TabMotionMenu(tab.Name, smooth => ActivateTab(tab, smooth)); item.IsCheckable = true; item.IsChecked = tab == Fence.ActiveTab; item.Tag = tab.Id;
            item.ToolTip = L.Text("ПКМ — действия с вкладкой"); BindTabContextMenu(item, tab);
            AutomationProperties.SetAutomationId(item, "menu-tab-select-" + tab.Id); tabList.Items.Add(item);
        }
        menu.Items.Add(tabList);
        var activeTabActions = MenuHeading(L.Text("Текущая вкладка"), "tabs");
        AutomationProperties.SetAutomationId(activeTabActions, "group-active-tab-actions");
        tabList.Items.Add(new Separator());
        AddTabActions(activeTabActions.Items, Fence.ActiveTab); tabList.Items.Add(activeTabActions);
        var tabVisibility = MenuHeading(L.Text("Показывать вкладки"), "tabs");
        foreach (var visibility in Enum.GetValues<TabVisibility>())
        {
            var item = Menu(Editors.TabVisibilityLabels[(int)visibility], () => _app.Mutate(() => { Fence.TabVisibility = visibility; if (Fence.TabStyle == TabStyle.MenuOnly) Fence.TabStyle = TabStyle.Flat; }));
            item.IsCheckable = true; item.IsChecked = Fence.TabStyle == TabStyle.MenuOnly ? visibility == TabVisibility.Hidden : Fence.TabVisibility == visibility;
            AutomationProperties.SetAutomationId(item, "menu-tab-visibility-" + visibility); tabVisibility.Items.Add(item);
        }
        tabList.Items.Add(tabVisibility);
        var sets = MenuHeading(L.Text("Наборы"), "pages");
        foreach (var page in _app.Workspace.Pages) { var item = Menu(page.Name, () => _app.SwitchPage(page.Id)); item.IsCheckable = true; item.IsChecked = page.Id == _app.Workspace.ActivePageId; sets.Items.Add(item); }
        if (_app.Workspace.Pages.Count > 1) menu.Items.Add(sets);
        menu.Items.Add(new Separator());
        var roll = TabMotionMenu(L.Text("Свернуть группу"), smooth => ToggleRoll(smooth));
        roll.IsEnabled = !KeepsExpanded;
        var pin = Menu(L.Text("Закрепить / разрешить перемещение"), () => _app.Mutate(() => Fence.Locked = !Fence.Locked));
        menu.Items.Add(roll);
        var more = MenuHeading(L.Text("Дополнительно"), "more");
        AutomationProperties.SetAutomationId(more, "group-more-menu");
        more.Items.Add(pin);
        menu.Opened += (_, _) =>
        {
            foreach (var item in tabList.Items.OfType<MenuItem>()) item.IsChecked = Equals(item.Tag, Fence.ActiveTabId);
            activeTabActions.Items.Clear(); AddTabActions(activeTabActions.Items, Fence.ActiveTab);
            var rollLabel = L.Text(VisuallyCollapsed ? "Развернуть группу" : "Свернуть группу");
            var rollHint = VisuallyCollapsed ? L.Text("Развернуть содержимое группы") : IsEdgeDock ? L.Text("Свернуть до полоски у края") : FenceLayout.ShowTabs(Fence) ? L.Text("Свернуть содержимое, оставив верхние вкладки") : L.Text("Свернуть содержимое группы");
            roll.Header = rollLabel; roll.ToolTip = KeepsExpanded ? AlwaysOpenHint : rollHint; roll.IsEnabled = !KeepsExpanded; ToolTipService.SetShowOnDisabled(roll, true); roll.Icon = Brand.Icon(VisuallyCollapsed ? "expand" : "collapse", Ui.Muted, 16); AutomationProperties.SetName(roll, rollLabel);
            var pinLabel = L.Text(Fence.Locked ? "Открепить положение и размер" : "Закрепить положение и размер");
            pin.Header = pin.ToolTip = pinLabel; pin.Icon = Brand.Icon(Fence.Locked ? "pin-off" : "pin", Ui.Muted, 16); AutomationProperties.SetName(pin, pinLabel);
            FenceMenuTheme.Apply(menu, Fence);
        };
        var modes = MenuHeading(L.Text("Режим группы"), "appearance");
        foreach (var mode in Enum.GetValues<FenceDisplayMode>()) { var item = Menu(Editors.ModeLabels[(int)mode], () => _app.Mutate(() => Fence.DisplayMode = mode)); item.IsCheckable = true; item.IsChecked = Fence.DisplayMode == mode; modes.Items.Add(item); }
        more.Items.Add(modes);
        var layers = MenuHeading(L.Text("Положение поверх окон"), "layers");
        foreach (var layer in Enum.GetValues<FenceLayer>()) { var item = Menu(Editors.LayerLabels[(int)layer], () => _app.Mutate(() => Fence.Layer = layer)); item.IsCheckable = true; item.IsChecked = Fence.Layer == layer; layers.Items.Add(item); }
        more.Items.Add(layers);
        var edges = MenuHeading(L.Text("Полоска у края"), "collapse");
        foreach (var edge in Enum.GetValues<DockEdge>())
        {
            var item = Menu(Editors.EdgeLabels[(int)edge], () => _app.Mutate(() => { Fence.Edge = edge; if (edge != DockEdge.None) { Fence.Layer = FenceLayer.AlwaysOnTop; Fence.HoverExpand = true; } })); item.IsCheckable = true; item.IsChecked = Fence.Edge == edge; edges.Items.Add(item);
        }
        more.Items.Add(edges);
        var displays = MenuHeading(L.Text("Перенести на монитор"), "monitor");
        var monitors = AppController.Displays();
        foreach (var (display, index) in monitors.Select((d, i) => (d, i))) displays.Items.Add(Menu(L.Format("Монитор {0} · {1} · {2:0}%", index + 1, display.Name, display.Scale / AppController.GlobalScale * 100), () => _app.MoveToMonitor(Fence, display)));
        if (monitors.Length > 1) more.Items.Add(displays);
        var sort = MenuHeading(L.Text("Сортировка"), "sort");
        foreach (var order in Enum.GetValues<SortOrder>()) { var item = Menu(Editors.SortLabels[(int)order], () => _app.Mutate(() => Fence.Sort = order)); item.IsCheckable = true; item.IsChecked = Fence.Sort == order; sort.Items.Add(item); }
        more.Items.Add(sort); more.Items.Add(new Separator()); more.Items.Add(Menu(L.Text("Настройки Walle Fences"), _app.ShowManager, "settings"));
        menu.Items.Add(more); menu.Items.Add(new Separator());
        menu.Items.Add(ContentMenu(L.Text("Удалить группу…"), () => Editors.DeleteFence(_app, Fence), "delete"));
        FenceMenuTheme.Apply(menu, Fence); return menu;
    }
    private ContextMenu ItemMenu(FileEntry entry)
    {
        var menu = new ContextMenu(); menu.Items.Add(Menu(entry.IsDirectory && CurrentPortal != null && !Fence.PortalFoldersInExplorer ? L.Text("Открыть в портале") : L.Text("Открыть"), () => OpenEntry(entry), entry.IsDirectory ? "folder" : "external-link"));
        var launch = CurrentPortal == null ? FileCatalog.Launch(Fence.ActiveTab, entry.Path) : null;
        if (AppController.CanSaveProgram(entry.Path, launch))
        {
            var favorite = _app.IsFavoriteProgram(entry.Path, launch);
            menu.Items.Add(Menu(L.Text(favorite ? "Убрать из избранного" : "Добавить в избранное"), () => _app.ToggleProgramFavorite(entry.Path, launch), favorite ? "star-filled" : "star"));
        }
        menu.Items.Add(Menu(L.Text("Показать в Проводнике"), () => Native.Reveal(entry.Path), "folder"));
        menu.Items.Add(ContentMenu(L.Text("Меню Windows…"), () => Dispatcher.BeginInvoke(() => Ui.Guard(() => { ShellMenu.Show(entry.Path, this); _app.QueueRefresh(); })), "more"));
        menu.Items.Add(Menu(L.Text("Скопировать путь"), () => Clipboard.SetText(entry.Path), "copy"));
        if (CurrentPortal == null && !Fence.ContentLocked)
        {
            var move = MenuHeading(L.Text("Переместить в группу"), "groups");
            foreach (var target in _app.Workspace.Fences.Where(f => !f.ContentLocked))
                foreach (var tab in target.Tabs.Where(t => string.IsNullOrEmpty(t.PortalPath) && t != Fence.ActiveTab))
                    move.Items.Add(Menu(target.Name + " / " + tab.Name, () => { var references = ShortcutReferences.Read(Fence.ActiveTab, [entry.Path]); _app.Mutate(() => FileCatalog.TransferReferences(Fence.ActiveTab, tab, references, true)); }));
            menu.Items.Add(move); menu.Items.Add(Menu(L.Text("Убрать ссылку из группы"), () => _app.Mutate(() => FileCatalog.RemoveReferences(Fence.ActiveTab, [entry.Path])), "close"));
        }
        FenceMenuTheme.Apply(menu, Fence); return menu;
    }
}
