using System.Windows.Automation;
using System.Windows.Markup;

namespace WalleFences.App;

// Creating a widget stays a single decision: choose a type, adjust its draft, then add.
// The window owns only disposable previews and drafts; the caller commits the result.
internal sealed class WidgetPickerWindow : Window
{
    private enum Category { All, System, Time, Storage }
    private readonly AppController _app;
    private readonly Fence _style;
    private readonly Dictionary<WidgetKind, DesktopWidget> _drafts = [];
    private readonly Dictionary<WidgetKind, Button> _cards = [];
    private readonly Dictionary<Category, Button> _categories = [];
    private readonly Grid _gallery = new();
    private readonly Grid _content = new();
    private readonly ScrollViewer _galleryScroll, _detailsScroll;
    private readonly Border _gallerySide, _detailsSide;
    private readonly TextBox _search = new() { Margin = new Thickness(0), MaxLength = 100 };
    private readonly StackPanel _details = new();
    private readonly Border _preview = new();
    private readonly TextBlock _selectedName = Ui.Text("", 17, Ui.Ink, FontWeights.SemiBold);
    private readonly TextBlock _selectedDescription = Ui.Text("", 11, Ui.Muted);
    private readonly TextBlock _destination = Ui.Text(L.Text("Каждый виджет появится отдельной панелью в текущем наборе."), 10, Ui.Muted);
    private readonly TextBlock _empty = Ui.Text(L.Text("Виджеты не найдены"), 15, Ui.Ink, FontWeights.SemiBold);
    private readonly Button _add;
    private readonly DispatcherTimer _previewDelay = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly SurfaceMotion _previewMotion;
    private readonly TextBox _title, _note;
    private readonly Slider _minutes;
    private readonly CheckBox _hours;
    private readonly ComboBox _processSort, _processCount;
    private readonly StackPanel _fields = new(), _noteFields = new(), _focusFields = new(), _clockFields = new(), _processFields = new();
    private readonly TextBlock _selectionHint = Ui.Text(L.Text("Выберите виджет из каталога слева."), 12, Ui.Muted);
    private readonly Border _emptyGallery;
    private WidgetKind[] _visibleKinds = [];
    private Category _category;
    private WidgetKind? _selected;
    private int _columns = 2;
    private bool _syncing, _closed, _stacked;
    private DesktopWidget? _result;

    internal static DesktopWidget? Pick(AppController app, Window? owner = null, Fence? styleSource = null)
    {
        var dialog = new WidgetPickerWindow(app, styleSource);
        var manager = app.Manager is { IsVisible: true, WindowState: not WindowState.Minimized } visibleManager ? visibleManager : null;
        static bool BelongsTo(Window window, Window parent)
        {
            for (var current = window.Owner; current != null; current = current.Owner) if (ReferenceEquals(current, parent)) return true;
            return false;
        }
        var activeForm = Application.Current?.Windows.OfType<FormWindow>().FirstOrDefault(window => window.IsActive && window.IsVisible && (manager == null || BelongsTo(window, manager)));
        // The visible manager stays above desktop groups. Owning the picker by a
        // group in that case would let the manager cover its modal dialog.
        Window? effectiveOwner = activeForm ?? (Window?)manager;
        effectiveOwner ??= owner is { IsVisible: true, WindowState: not WindowState.Minimized } ? owner : null;
        if (effectiveOwner != null) { dialog.Owner = effectiveOwner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        return dialog.ShowDialog() == true ? dialog._result : null;
    }

    private WidgetPickerWindow(AppController app, Fence? styleSource)
    {
        _app = app;
        _style = new Fence { Color = styleSource?.Color ?? "#1D2021", Accent = styleSource?.Accent ?? "#A0E9C0", CornerRadius = styleSource?.CornerRadius ?? 12 };
        Title = L.Text("Добавить виджет"); Icon = Brand.WindowIcon; ShowInTaskbar = false;
        FontFamily = Ui.AppFont; Language = XmlLanguage.GetLanguage(L.Culture.Name);
        SetResourceReference(BackgroundProperty, "Paper"); SetResourceReference(ForegroundProperty, "Ink");
        var work = SystemParameters.WorkArea;
        Width = Math.Min(1040, Math.Max(320, work.Width - 32)); Height = Math.Min(760, Math.Max(300, work.Height - 32));
        MinWidth = Math.Min(680, Width); MinHeight = Math.Min(500, Height); ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Ui.WindowFrame(this);
        AutomationProperties.SetAutomationId(this, "widget-picker");

        var shell = new Grid();
        shell.RowDefinitions.Add(new() { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new() { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new() { Height = GridLength.Auto });
        shell.Children.Add(Header());
        var finder = new StackPanel { Margin = new Thickness(20, 16, 20, 10) };
        var heading = Ui.Text(L.Text("Выберите виджет"), 22, Ui.Ink, FontWeights.SemiBold); finder.Children.Add(heading);
        var intro = Ui.Text(L.Text("Найдите нужный виджет и настройте его перед добавлением."), 11, Ui.Muted); intro.Margin = new Thickness(0, 4, 0, 14); finder.Children.Add(intro);
        finder.Children.Add(Search());
        var categories = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var category in Enum.GetValues<Category>())
        {
            var button = Ui.Segment(CategoryLabel(category), () => ChangeCategory(category), selected: category == Category.All, id: "widget-picker-category-" + category);
            button.Margin = new Thickness(0, 0, 5, 5); button.Padding = new Thickness(11, 7, 11, 7);
            _categories.Add(category, button); categories.Children.Add(button);
        }
        finder.Children.Add(categories); Grid.SetRow(finder, 1); shell.Children.Add(finder);

        _galleryScroll = new ScrollViewer { Content = _gallery, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(_galleryScroll, "widget-picker-gallery");
        var emptyBody = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
        var emptyIcon = Brand.Icon("search", Ui.Muted, 28); emptyIcon.HorizontalAlignment = HorizontalAlignment.Center; emptyIcon.Margin = new Thickness(0, 0, 0, 14); emptyBody.Children.Add(emptyIcon);
        _empty.TextAlignment = TextAlignment.Center; emptyBody.Children.Add(_empty);
        var emptyHint = Ui.Text(L.Text("Попробуйте другое название или выберите другую категорию."), 11, Ui.Muted); emptyHint.TextAlignment = TextAlignment.Center; emptyHint.Margin = new Thickness(0, 7, 0, 0); emptyBody.Children.Add(emptyHint);
        _emptyGallery = new Border { Child = emptyBody, Visibility = Visibility.Collapsed };
        var galleryLayer = new Grid(); galleryLayer.Children.Add(_galleryScroll); galleryLayer.Children.Add(_emptyGallery);
        _gallerySide = new Border { Child = galleryLayer };

        _selectedDescription.LineHeight = 17; _selectedDescription.Margin = new Thickness(0, 6, 0, 16);
        _details.Children.Add(_selectedName); _details.Children.Add(_selectedDescription);
        var previewLabel = Ui.Text(L.Text("Предпросмотр"), 11, Ui.Muted, FontWeights.SemiBold); previewLabel.Margin = new Thickness(0, 0, 0, 6); _details.Children.Add(previewLabel);
        AutomationProperties.SetAutomationId(_preview, "widget-picker-preview"); _details.Children.Add(_preview);
        var sampleHint = Ui.Text(L.Text("Пример оформления"), 9, Ui.Muted); sampleHint.Margin = new Thickness(0, 6, 0, 18); _details.Children.Add(sampleHint);
        _previewMotion = new SurfaceMotion(_preview);
        _title = Ui.Input(_fields, L.Text("Название (необязательно)")); _title.MaxLength = 100; _title.Margin = new Thickness(0, 0, 0, 12); AutomationProperties.SetAutomationId(_title, "widget-picker-title");
        _fields.Children.Add(_noteFields);
        _note = Ui.Input(_noteFields, L.Text("Текст заметки")); _note.AcceptsReturn = true; _note.TextWrapping = TextWrapping.Wrap; _note.Height = 108; _note.MaxLength = 20000; _note.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; AutomationProperties.SetAutomationId(_note, "widget-picker-note");
        _fields.Children.Add(_focusFields);
        _minutes = Ui.Slider(_focusFields, L.Text("Длительность, минут"), 1, 180, 25); AutomationProperties.SetAutomationId(_minutes, "widget-picker-focus-minutes");
        var durations = new WrapPanel();
        foreach (var duration in new[] { 15, 25, 50, 90 })
        {
            var button = Ui.Button(L.Format("{0} мин", duration), () => _minutes.Value = duration);
            button.Padding = new Thickness(9, 5, 9, 5); button.FontSize = 10; durations.Children.Add(button);
        }
        _focusFields.Children.Add(durations);
        _fields.Children.Add(_clockFields); _hours = Ui.Check(_clockFields, L.Text("24-часовой формат"), true); AutomationProperties.SetAutomationId(_hours, "widget-picker-clock-format");
        _fields.Children.Add(_processFields);
        _processSort = Ui.Combo(_processFields, L.Text("Сортировать процессы"), [L.Text("По загрузке CPU"), L.Text("По использованию памяти")]);
        _processSort.Margin = new Thickness(0, 0, 0, 12); AutomationProperties.SetAutomationId(_processSort, "widget-picker-process-sort");
        Ui.Label(_processFields, L.Text("Количество процессов"));
        _processCount = new ComboBox { ItemsSource = Enumerable.Range(3, 8).ToArray(), SelectedItem = 5 };
        AutomationProperties.SetAutomationId(_processCount, "widget-picker-process-count"); AutomationProperties.SetName(_processCount, L.Text("Количество процессов")); _processFields.Children.Add(_processCount);
        _details.Children.Add(_fields);
        var settingsHint = Ui.Text(L.Text("Дополнительные настройки доступны через ПКМ по виджету."), 10, Ui.Muted); settingsHint.LineHeight = 16; settingsHint.Margin = new Thickness(0, 16, 0, 0); _details.Children.Add(settingsHint);
        _detailsScroll = new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(_detailsScroll, "widget-picker-details");
        var detailsLayer = new Grid(); detailsLayer.Children.Add(_detailsScroll); _selectionHint.VerticalAlignment = VerticalAlignment.Center; _selectionHint.TextAlignment = TextAlignment.Center; detailsLayer.Children.Add(_selectionHint);
        _detailsSide = new Border { Child = detailsLayer, Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16) };
        _content.Children.Add(_gallerySide); _content.Children.Add(_detailsSide); _content.Margin = new Thickness(20, 0, 20, 16); Grid.SetRow(_content, 2); shell.Children.Add(_content);

        var footer = new Grid { Margin = new Thickness(20, 12, 14, 14) }; footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _destination.LineHeight = 16; _destination.Margin = new Thickness(0, 0, 16, 0); footer.Children.Add(_destination);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var cancel = Ui.Button(L.Text("Отмена"), Close, id: "widget-picker-cancel"); cancel.IsCancel = true; actions.Children.Add(cancel);
        _add = Ui.Button(L.Text("Добавить виджет"), Add, primary: true, id: "widget-picker-add"); _add.IsDefault = true; actions.Children.Add(_add); Grid.SetColumn(actions, 1); footer.Children.Add(actions);
        var footerFrame = new Border { Child = footer, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0) }; Grid.SetRow(footerFrame, 3); shell.Children.Add(footerFrame);
        Content = new Border { Child = shell, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Background = Ui.Paper };

        _search.TextChanged += (_, _) => RefreshGallery();
        _title.TextChanged += (_, _) => DraftChanged(); _note.TextChanged += (_, _) => DraftChanged(); _minutes.ValueChanged += (_, _) => DraftChanged();
        _hours.Checked += (_, _) => DraftChanged(); _hours.Unchecked += (_, _) => DraftChanged();
        _processSort.SelectionChanged += (_, _) => DraftChanged(); _processCount.SelectionChanged += (_, _) => DraftChanged();
        _previewDelay.Tick += (_, _) => { _previewDelay.Stop(); RebuildPreview(); };
        _galleryScroll.SizeChanged += (_, _) => LayoutGallery();
        SizeChanged += (_, _) => LayoutContent();
        Loaded += (_, _) => { LayoutContent(); _search.Focus(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { _search.Focus(); _search.SelectAll(); e.Handled = true; }
        };
        Closed += (_, _) => { _closed = true; _previewDelay.Stop(); DisposePreview(); _previewMotion.Dispose(); };
        LayoutContent(); RefreshGallery();
    }

    private Border Header()
    {
        var row = new DockPanel();
        var close = Ui.IconButton("close", Close, L.Text("Закрыть"), "widget-picker-close"); close.Margin = new Thickness(0); DockPanel.SetDock(close, Dock.Right); row.Children.Add(close);
        var title = Ui.Row(Brand.Icon("widgets", Ui.Accent, 17), Ui.Text(Title, 12, Ui.Ink, FontWeights.SemiBold)); title.Children[1].SetValue(MarginProperty, new Thickness(9, 0, 0, 0)); row.Children.Add(title);
        var header = new Border { Child = row, Height = 40, Padding = new Thickness(16, 0, 6, 0), Background = Ui.Paper, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        header.MouseLeftButtonDown += (_, e) => { if (!close.IsMouseOver) { DragMove(); e.Handled = true; } }; return header;
    }

    private Grid Search()
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        AutomationProperties.SetAutomationId(_search, "widget-picker-search"); AutomationProperties.SetName(_search, L.Text("Поиск виджетов")); _search.ToolTip = L.Text("Найти виджет…"); _search.Padding = new Thickness(34, 9, 10, 9);
        var input = new Grid(); input.Children.Add(_search);
        var icon = Brand.Icon("search", Ui.Muted, 15); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(11, 0, 0, 0); icon.IsHitTestVisible = false; input.Children.Add(icon);
        var placeholder = Ui.Text(L.Text("Найти виджет…"), 12, Ui.Muted); placeholder.Margin = new Thickness(35, 0, 10, 0); placeholder.IsHitTestVisible = false; input.Children.Add(placeholder); row.Children.Add(input);
        var clear = Ui.IconButton("close", () => { _search.Clear(); _search.Focus(); }, L.Text("Очистить поиск"), "widget-picker-search-clear");
        clear.Margin = new Thickness(6, 0, 0, 0); clear.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(clear, 1); row.Children.Add(clear);
        _search.TextChanged += (_, _) => { clear.IsEnabled = _search.Text.Length > 0; placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }; clear.IsEnabled = false; return row;
    }

    private static string CategoryLabel(Category category) => L.Text(category switch { Category.System => "Система", Category.Time => "Время и задачи", Category.Storage => "Диски и сеть", _ => "Все" });
    private static Category KindCategory(WidgetKind kind) => kind switch { WidgetKind.Clock or WidgetKind.Note or WidgetKind.Focus => Category.Time, WidgetKind.Drives or WidgetKind.Network => Category.Storage, _ => Category.System };

    private void ChangeCategory(Category category)
    {
        _category = category; foreach (var pair in _categories) pair.Value.Tag = pair.Key == category ? "selected" : null;
        RefreshGallery(); _galleryScroll.ScrollToTop();
    }

    private void RefreshGallery()
    {
        if (_closed) return;
        var terms = _search.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _visibleKinds = Enum.GetValues<WidgetKind>().Where(kind => (_category == Category.All || KindCategory(kind) == _category)
            && terms.All(term => (WidgetEditor.Labels[(int)kind] + " " + WidgetCatalog.Description(kind) + " " + kind).Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        _cards.Clear(); _gallery.Children.Clear();
        var next = _selected.HasValue && _visibleKinds.Contains(_selected.Value) ? _selected : _visibleKinds.Length > 0 ? _visibleKinds[0] : (WidgetKind?)null;
        foreach (var kind in _visibleKinds)
        {
            var button = WidgetCatalog.Choice(kind, () => Select(kind), compact: false, selected: next == kind, id: "widget-picker-type-" + kind);
            button.MinHeight = 160; _cards.Add(kind, button); _gallery.Children.Add(button);
        }
        _emptyGallery.Visibility = _visibleKinds.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _galleryScroll.Visibility = _visibleKinds.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        LayoutGallery(); Select(next, animate: false);
    }

    private void LayoutContent()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width; _stacked = width < 700;
        _content.ColumnDefinitions.Clear(); _content.RowDefinitions.Clear();
        if (_stacked)
        {
            _content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            _content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); _content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_detailsSide, 0); Grid.SetRow(_detailsSide, 1); _detailsSide.Margin = new Thickness(0, 10, 0, 0);
        }
        else
        {
            _content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            _content.ColumnDefinitions.Add(new() { Width = new GridLength(width < 900 ? 285 : 330) });
            _content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_detailsSide, 1); Grid.SetRow(_detailsSide, 0); _detailsSide.Margin = new Thickness(16, 0, 0, 0);
        }
        _destination.Visibility = width < 700 ? Visibility.Collapsed : Visibility.Visible; LayoutGallery();
    }

    private void LayoutGallery()
    {
        var width = _galleryScroll.ViewportWidth > 0 ? _galleryScroll.ViewportWidth : Math.Max(180, (ActualWidth > 0 ? ActualWidth : Width) - (_stacked ? 40 : 386));
        _columns = width >= 440 ? 2 : 1;
        _gallery.ColumnDefinitions.Clear(); _gallery.RowDefinitions.Clear();
        for (var column = 0; column < _columns; column++) _gallery.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < (_visibleKinds.Length + _columns - 1) / _columns; row++) _gallery.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < _visibleKinds.Length; index++)
        {
            var button = _cards[_visibleKinds[index]]; Grid.SetColumn(button, index % _columns); Grid.SetRow(button, index / _columns);
            button.Margin = new Thickness(index % _columns == 0 ? 0 : 5, 0, index % _columns == _columns - 1 ? 0 : 5, 10);
        }
    }

    private void Select(WidgetKind? kind, bool animate = true)
    {
        if (_closed) return;
        SaveDraft(); _selected = kind; _previewDelay.Stop();
        foreach (var pair in _cards)
        {
            var selected = pair.Key == kind; pair.Value.Tag = selected ? "selected" : null; pair.Value.Background = selected ? Ui.AccentWash : Ui.Surface; pair.Value.BorderBrush = selected ? Ui.Accent : Ui.Line;
            AutomationProperties.SetItemStatus(pair.Value, selected ? L.Text("Выбрано") : "");
        }
        _add.IsEnabled = kind.HasValue; _detailsScroll.Visibility = kind.HasValue ? Visibility.Visible : Visibility.Collapsed; _selectionHint.Visibility = kind.HasValue ? Visibility.Collapsed : Visibility.Visible;
        if (!kind.HasValue) { DisposePreview(); return; }
        if (!_drafts.TryGetValue(kind.Value, out var draft)) { draft = new DesktopWidget { Kind = kind.Value }; _drafts.Add(kind.Value, draft); }
        _syncing = true;
        try
        {
            _selectedName.Text = WidgetEditor.Labels[(int)kind.Value]; _selectedDescription.Text = WidgetCatalog.Description(kind.Value);
            _title.Text = draft.Title; _note.Text = draft.Text; _minutes.Value = draft.FocusMinutes; _hours.IsChecked = draft.Use24HourClock;
            _processSort.SelectedIndex = (int)draft.ProcessSort; _processCount.SelectedItem = draft.ProcessCount;
            _noteFields.Visibility = kind == WidgetKind.Note ? Visibility.Visible : Visibility.Collapsed;
            _focusFields.Visibility = kind == WidgetKind.Focus ? Visibility.Visible : Visibility.Collapsed;
            _clockFields.Visibility = kind == WidgetKind.Clock ? Visibility.Visible : Visibility.Collapsed;
            _processFields.Visibility = kind == WidgetKind.Processes ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _syncing = false; }
        RebuildPreview(); _detailsScroll.ScrollToTop(); _previewMotion.Enter(animate);
    }

    private void SaveDraft()
    {
        if (_syncing || !_selected.HasValue || !_drafts.TryGetValue(_selected.Value, out var draft)) return;
        draft.Title = _title.Text.Trim(); draft.Text = _note.Text; draft.Use24HourClock = _hours.IsChecked == true;
        draft.FocusMinutes = (int)_minutes.Value; draft.RemainingSeconds = draft.FocusMinutes * 60; draft.FocusEndsAt = null;
        draft.ProcessSort = (ProcessSortMode)Math.Clamp(_processSort.SelectedIndex, 0, 1); draft.ProcessCount = _processCount.SelectedItem is int count ? Math.Clamp(count, 3, 10) : 5;
    }

    private void DraftChanged()
    {
        if (_syncing || _closed || !_selected.HasValue) return;
        SaveDraft(); _previewDelay.Stop(); _previewDelay.Start();
    }

    private void RebuildPreview()
    {
        DisposePreview();
        if (!_closed && _selected.HasValue && _drafts.TryGetValue(_selected.Value, out var draft)) _preview.Child = WidgetCatalog.Preview(_app, _style, draft);
    }

    private void DisposePreview()
    {
        if (_preview.Child is Border { Child: WidgetHost host }) host.Dispose();
        _preview.Child = null;
    }

    private void Add()
    {
        if (!_selected.HasValue || !_visibleKinds.Contains(_selected.Value)) return;
        SaveDraft(); _result = WidgetCatalog.Copy(_drafts[_selected.Value]); DialogResult = true;
    }
}
