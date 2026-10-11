using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Markup;

namespace WalleFences.App;

internal sealed record FirstRunChoices(string Language, bool AdvancedSettings, bool ImportDesktop, bool AutoStart, bool ShowTraining);

// Setup is a separate, resumable flow. Language and settings density are the
// only preview changes; Windows integration starts after the final action.
internal sealed class FirstRunWindow : Window
{
    private const int StepCount = 3;
    private readonly Action<string, bool> _preview;
    private readonly Func<FirstRunChoices, Task> _complete;
    private readonly Action<bool> _finished;
    private readonly TextBlock _heading = Ui.Text("", 24, Ui.Ink, FontWeights.Bold);
    private readonly TextBlock _description = Ui.Text("", 12, Ui.Muted);
    private readonly TextBlock _progress = Ui.Text("", 10.5, Ui.Muted);
    private readonly TextBlock _error = Ui.Text("", 11, Ui.Ink);
    private readonly StackPanel _body = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _footer = new();
    private readonly DockPanel _titlebar = new();
    private readonly SurfaceMotion _bodyMotion;
    private Button? _next, _back, _later, _close;
    private string _language;
    private bool _advanced, _importDesktop, _autoStart, _showTraining = true, _busy, _completed;
    private int _step;

    internal FirstRunWindow(string language, bool advanced, Action<string, bool> preview,
        Func<FirstRunChoices, Task> complete, Action<bool> finished)
    {
        _language = L.Normalize(language); _advanced = advanced;
        _preview = preview; _complete = complete; _finished = finished;
        FontFamily = Ui.AppFont; Icon = Brand.WindowIcon; Background = Ui.Paper; Foreground = Ui.Ink;
        Width = 720; Height = 570; MinWidth = 620; MinHeight = 510;
        MaxHeight = Math.Max(510, SystemParameters.WorkArea.Height - 48);
        Height = Math.Min(Height, MaxHeight); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize; ShowInTaskbar = true;
        AutomationProperties.SetAutomationId(this, "first-run-window"); Ui.WindowFrame(this);

        var shell = new DockPanel();
        var header = new Border { Child = _titlebar, Height = 44, Padding = new Thickness(17, 0, 8, 0), BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock or Border or DockPanel) { DragMove(); e.Handled = true; } };
        DockPanel.SetDock(header, Dock.Top); shell.Children.Add(header);
        var main = new DockPanel { Margin = new Thickness(28, 21, 28, 24) };
        var foot = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        _error.Visibility = Visibility.Collapsed; _error.Margin = new Thickness(0, 0, 0, 10);
        AutomationProperties.SetAutomationId(_error, "first-run-error"); AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Assertive);
        foot.Children.Add(_error); foot.Children.Add(_footer); DockPanel.SetDock(foot, Dock.Bottom); main.Children.Add(foot);
        var page = new StackPanel();
        var progress = new Grid { Margin = new Thickness(0, 0, 0, 15) };
        progress.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); progress.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        progress.Children.Add(_progress); Grid.SetColumn(_dots, 1); progress.Children.Add(_dots);
        AutomationProperties.SetAutomationId(_progress, "first-run-progress"); AutomationProperties.SetLiveSetting(_progress, AutomationLiveSetting.Polite);
        page.Children.Add(progress); _heading.Margin = new Thickness(0, 0, 0, 8); page.Children.Add(_heading);
        _description.Margin = new Thickness(0, 0, 0, 24); page.Children.Add(_description); page.Children.Add(_body);
        main.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        shell.Children.Add(main);
        Content = new Border { Child = shell, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), ClipToBounds = true };
        _bodyMotion = new SurfaceMotion(_body);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !_busy) { Close(); e.Handled = true; } };
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
        Closed += (_, _) => _bodyMotion.Dispose();
        Render(false);
    }

    private void Render(bool animate)
    {
        _bodyMotion.Reset();
        Title = L.Text("Первичная настройка"); Language = XmlLanguage.GetLanguage(L.Culture.Name);
        _titlebar.Children.Clear();
        _close = Ui.IconButton("close", Close, L.Text("Закрыть"), "first-run-close"); _close.Width = 32; _close.Height = 32; _close.Margin = new Thickness(0);
        DockPanel.SetDock(_close, Dock.Right); _titlebar.Children.Add(_close);
        var name = Ui.Text("Walle Fences", 12, Ui.Ink, FontWeights.SemiBold); name.Margin = new Thickness(8, 0, 0, 0); _titlebar.Children.Add(Ui.Row(Brand.Logo(26), name));
        _progress.Text = L.Format("Шаг {0} из {1}", _step + 1, StepCount);
        _dots.Children.Clear();
        for (var i = 0; i < StepCount; i++)
            _dots.Children.Add(new Border { Width = i == _step ? 22 : 6, Height = 6, CornerRadius = new CornerRadius(3), Background = i == _step ? Ui.FocusAccent : Ui.Line, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        _body.Children.Clear(); _error.Text = ""; _error.Visibility = Visibility.Collapsed;
        if (_step == 0) LanguageStep(); else if (_step == 1) ModeStep(); else StartStep();
        RenderFooter();
        if (IsLoaded) _bodyMotion.Enter(animate);
    }

    private void LanguageStep()
    {
        _heading.Text = L.Text("Добро пожаловать в Walle Fences");
        _description.Text = L.Text("Выберите язык интерфейса. Его можно поменять в настройках в любое время.");
        var choices = new Grid();
        for (var i = 0; i < L.Codes.Length; i++) choices.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < L.Codes.Length; i++)
        {
            var code = L.Codes[i]; var nativeName = L.Names[i];
            var card = ChoiceCard("language", nativeName, code == _language ? L.Text("Выбрано") : "", code == _language,
                () => { _language = code; _preview(_language, _advanced); Render(false); }, "first-run-language-" + code);
            card.Margin = new Thickness(i == 0 ? 0 : 5, 0, i == L.Codes.Length - 1 ? 0 : 5, 0); Grid.SetColumn(card, i); choices.Children.Add(card);
        }
        _body.Children.Add(choices);
        var note = Ui.Text(L.Text("Начнём с языка и удобного уровня настроек. Затем вы выберете, как оформить свой рабочий стол."), 11, Ui.Muted);
        note.Margin = new Thickness(0, 20, 0, 0); _body.Children.Add(note);
    }

    private void ModeStep()
    {
        _heading.Text = L.Text("Настройки под ваши задачи");
        _description.Text = L.Text("Выберите удобный режим. Сменить его можно в любой момент, а ваши группы и настройки сохранятся.");
        var choices = new Grid(); choices.ColumnDefinitions.Add(new()); choices.ColumnDefinitions.Add(new());
        var simple = ChoiceCard("desktop", L.Text("Простые"), L.Text("Основные действия, оформление и поведение групп. Всё нужное для начала."), !_advanced,
            () => { _advanced = false; _preview(_language, _advanced); Render(false); }, "first-run-mode-simple");
        var advanced = ChoiceCard("settings", L.Text("Продвинутые"), L.Text("Точные размеры, вкладки, задержки, автоматизация и сочетания клавиш."), _advanced,
            () => { _advanced = true; _preview(_language, _advanced); Render(false); }, "first-run-mode-advanced");
        simple.Margin = new Thickness(0, 0, 6, 0); advanced.Margin = new Thickness(6, 0, 0, 0); choices.Children.Add(simple); Grid.SetColumn(advanced, 1); choices.Children.Add(advanced); _body.Children.Add(choices);
    }

    private void StartStep()
    {
        _heading.Text = L.Text("Ваш первый рабочий стол");
        _description.Text = L.Text("Начните с пустого набора или соберите собственные значки рабочего стола в группы.");
        var choices = new Grid(); choices.ColumnDefinitions.Add(new()); choices.ColumnDefinitions.Add(new());
        var empty = ChoiceCard("plus", L.Text("Чистый старт"), L.Text("Добавляйте свои группы и виджеты по одному."), !_importDesktop,
            () => { _importDesktop = false; Render(false); }, "first-run-start-empty");
        var import = ChoiceCard("folder", L.Text("Собрать мой рабочий стол"), L.Text("Добавить ссылки на ваши файлы и программы. Оригиналы останутся на месте."), _importDesktop,
            () => { _importDesktop = true; Render(false); }, "first-run-start-desktop");
        empty.Margin = new Thickness(0, 0, 6, 0); import.Margin = new Thickness(6, 0, 0, 0); choices.Children.Add(empty); Grid.SetColumn(import, 1); choices.Children.Add(import); _body.Children.Add(choices);
        var options = new StackPanel { Margin = new Thickness(0, 17, 0, 0) };
        var training = Ui.Check(options, L.Text("Показать подсказки в интерфейсе"), _showTraining);
        AutomationProperties.SetAutomationId(training, "first-run-training"); training.Checked += (_, _) => _showTraining = true; training.Unchecked += (_, _) => _showTraining = false;
        var trainingHint = Ui.Text(L.Text("Подсказки покажут нужные кнопки прямо в программе. Их можно пройти в своём темпе или пропустить."), 10.5, Ui.Muted);
        trainingHint.Margin = new Thickness(25, 0, 0, 10); options.Children.Add(trainingHint);
        var startup = Ui.Check(options, L.Text("Запускать Walle Fences вместе с Windows"), _autoStart);
        AutomationProperties.SetAutomationId(startup, "first-run-autostart"); startup.Checked += (_, _) => _autoStart = true; startup.Unchecked += (_, _) => _autoStart = false;
        _body.Children.Add(options);
    }

    private static Button ChoiceCard(string icon, string title, string description, bool selected, Action choose, string id)
    {
        var button = Ui.Button("", choose, id: id);
        button.Padding = new Thickness(15, 14, 15, 14); button.MinHeight = 146;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Top;
        button.Background = selected ? Ui.AccentWash : Ui.Surface; button.BorderBrush = selected ? Ui.FocusAccent : Ui.Line; button.Tag = selected ? "selected" : null;
        var body = new StackPanel(); var top = new Grid(); top.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var glyph = Brand.Icon(icon, selected ? Ui.FocusAccent : Ui.Muted, 23); glyph.HorizontalAlignment = HorizontalAlignment.Left; top.Children.Add(glyph);
        if (selected) { var tick = Brand.Icon("check", Ui.FocusAccent, 16); Grid.SetColumn(tick, 1); top.Children.Add(tick); }
        body.Children.Add(top);
        var label = Ui.Text(title, 13, Ui.Ink, FontWeights.SemiBold); label.Margin = new Thickness(0, 13, 0, 7); body.Children.Add(label);
        if (!string.IsNullOrEmpty(description)) body.Children.Add(Ui.Text(description, 10.5, Ui.Muted));
        button.Content = body; AutomationProperties.SetName(button, title); AutomationProperties.SetHelpText(button, description); AutomationProperties.SetItemStatus(button, selected ? L.Text("Выбрано") : "");
        return button;
    }

    private void RenderFooter()
    {
        _footer.Children.Clear(); _footer.ColumnDefinitions.Clear();
        _footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _later = Ui.Button(L.Text("Позже"), Close, id: "first-run-later"); _later.Background = Brushes.Transparent; _later.BorderThickness = new Thickness(0); _later.Foreground = Ui.Muted; _later.HorizontalAlignment = HorizontalAlignment.Left; _footer.Children.Add(_later);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var pointer = false;
        _back = Ui.Button(L.Text("Назад"), () => { var animate = pointer; pointer = false; _step--; Render(animate); }, id: "first-run-back"); _back.IsEnabled = _step > 0; _back.Margin = new Thickness(0, 0, 7, 0);
        _back.PreviewMouseLeftButtonDown += (_, _) => pointer = true; _back.PreviewKeyDown += (_, _) => pointer = false; actions.Children.Add(_back);
        var nextPointer = false;
        _next = Ui.Button(L.Text(_step == StepCount - 1 ? "Начать" : "Далее"), () =>
        {
            var animate = nextPointer; nextPointer = false;
            if (_step < StepCount - 1) { _step++; Render(animate); }
            else _ = CompleteAsync();
        }, true, "first-run-next");
        _next.IsDefault = true; _next.Margin = new Thickness(0); _next.PreviewMouseLeftButtonDown += (_, _) => nextPointer = true; _next.PreviewKeyDown += (_, _) => nextPointer = false; actions.Children.Add(_next);
        Grid.SetColumn(actions, 1); _footer.Children.Add(actions);
        foreach (var button in new[] { _back!, _next!, _later! }) { button.MinHeight = 35; button.Padding = new Thickness(14, 7, 14, 7); AutomationProperties.SetName(button, button.Content?.ToString() ?? ""); }
    }

    private async Task CompleteAsync()
    {
        if (_busy || _completed) return;
        _busy = true; _bodyMotion.Reset(); _body.IsEnabled = false;
        foreach (var button in new[] { _back, _next, _later, _close }) if (button != null) button.IsEnabled = false;
        if (_next != null) _next.Content = L.Text("Настраиваем…");
        try
        {
            await _complete(new(_language, _advanced, _importDesktop, _autoStart, _showTraining));
            _completed = true; _busy = false; Close(); Ui.Guard(() => _finished(_showTraining));
        }
        catch (Exception ex)
        {
            _busy = false; _body.IsEnabled = true;
            foreach (var button in new[] { _back, _next, _later, _close }) if (button != null) button.IsEnabled = true;
            if (_next != null) _next.Content = L.Text("Начать");
            _error.Text = L.Text("Не удалось завершить настройку. Попробуйте снова.") + " " + ex.Message; _error.Visibility = Visibility.Visible;
        }
    }
}
