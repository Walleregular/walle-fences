using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace WalleFences.App;

internal sealed class EdgeSpanControl : StackPanel
{
    private readonly ComboBox _mode, _count;
    private readonly StackPanel _division = new();
    private readonly UniformGrid _blocks = new() { Rows = 1, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _hint;
    private DockEdge _edge;
    private int _blockIndex;
    internal event EventHandler? Changed;
    internal EdgeSpanMode Mode => (EdgeSpanMode)_mode.SelectedIndex;
    internal int BlockCount => _count.SelectedIndex + 1;
    internal int BlockIndex => _blockIndex;
    internal DockEdge Edge => _edge;

    internal EdgeSpanControl(Fence fence, bool compact = false)
    {
        _edge = fence.Edge; _blockIndex = fence.EdgeBlockIndex;
        AutomationProperties.SetAutomationId(this, compact ? "inspector-edge-span" : "editor-edge-span");
        var labels = new[] { L.Text("Своя длина"), L.Text("Весь край"), L.Text("Равные блоки") };
        if (compact)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 5) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(70) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(Ui.Text(L.Text("Длина"), 10.5, Ui.Muted));
            _mode = new ComboBox { ItemsSource = labels, SelectedIndex = (int)fence.EdgeSpanMode, Height = 28, FontSize = 11, Margin = new Thickness(0) };
            Grid.SetColumn(_mode, 1); row.Children.Add(_mode); Children.Add(row);
        }
        else _mode = Ui.Combo(this, L.Text("Протяжённость группы"), labels, (int)fence.EdgeSpanMode);
        AutomationProperties.SetAutomationId(_mode, compact ? "inspector-edge-span-mode" : "editor-edge-span-mode");
        AutomationProperties.SetName(_mode, L.Text("Протяжённость группы"));
        _mode.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(ComboBox.SelectedItem)) { Source = _mode });

        var countRow = new Grid(); countRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); countRow.ColumnDefinitions.Add(new() { Width = new GridLength(64) });
        countRow.Children.Add(Ui.Text(L.Text("Количество блоков"), compact ? 10.5 : 12, Ui.Muted));
        _count = new ComboBox { ItemsSource = Enumerable.Range(1, 12).Select(value => value.ToString(L.Culture)).ToArray(), SelectedIndex = Math.Clamp(fence.EdgeBlockCount, 1, 12) - 1, Height = 28, FontSize = compact ? 11 : 12, Margin = new Thickness(0) };
        AutomationProperties.SetName(_count, L.Text("Количество блоков"));
        AutomationProperties.SetAutomationId(_count, compact ? "inspector-edge-block-count" : "editor-edge-block-count");
        Grid.SetColumn(_count, 1); countRow.Children.Add(_count); _division.Children.Add(countRow);
        _division.Children.Add(_blocks); Children.Add(_division);
        _hint = Ui.Text("", compact ? 10.5 : 11, Ui.Muted); _hint.Margin = new Thickness(0, 4, 0, compact ? 5 : 12); Children.Add(_hint);
        _mode.SelectionChanged += (_, _) => Update(true);
        _count.SelectionChanged += (_, _) => Update(true);
        Update(false);
    }

    internal void UpdateEdge(DockEdge edge) { _edge = edge; Update(false); }
    internal void UseCustom() { if (Mode != EdgeSpanMode.Custom) _mode.SelectedIndex = (int)EdgeSpanMode.Custom; }
    internal void Apply(Fence fence)
    {
        fence.EdgeSpanMode = Mode; fence.EdgeBlockCount = BlockCount; fence.EdgeBlockIndex = BlockIndex;
        if (Mode != EdgeSpanMode.Custom) fence.OpeningSize = OpeningSize.Custom;
    }

    private void Update(bool notify)
    {
        Visibility = _edge == DockEdge.None ? Visibility.Collapsed : Visibility.Visible;
        _blockIndex = Math.Clamp(_blockIndex, 0, BlockCount - 1);
        _division.Visibility = Mode == EdgeSpanMode.EqualBlocks ? Visibility.Visible : Visibility.Collapsed;
        _blocks.Columns = BlockCount; _blocks.Children.Clear();
        for (var i = 0; i < BlockCount; i++)
        {
            var index = i;
            var label = (i + 1).ToString(L.Culture);
            var button = Ui.Button(label, () => { _blockIndex = index; Update(true); }, id: "edge-span-block-" + i);
            button.MinWidth = 0; button.MinHeight = 0; button.Height = 28; button.Padding = new Thickness(0); button.Margin = new Thickness(0, 0, i == BlockCount - 1 ? 0 : 2, 0); button.FontSize = 10;
            button.Background = i == _blockIndex ? Ui.AccentWash : Ui.Surface;
            button.BorderBrush = i == _blockIndex ? Ui.FocusAccent : Ui.Line;
            button.ToolTip = L.Format("Блок {0} из {1}", i + 1, BlockCount);
            AutomationProperties.SetName(button, (string)button.ToolTip); _blocks.Children.Add(button);
        }
        _hint.Text = Mode switch
        {
            EdgeSpanMode.Full => L.Text(_edge is DockEdge.Top or DockEdge.Bottom ? "Полоска и группа занимают всю ширину рабочей области." : "Полоска и группа занимают всю высоту рабочей области."),
            EdgeSpanMode.EqualBlocks => L.Format("Блок {0} из {1}", _blockIndex + 1, BlockCount) + " · " + L.Text(_edge is DockEdge.Top or DockEdge.Bottom ? "Слева направо" : "Сверху вниз"),
            _ => ""
        };
        _hint.Visibility = Mode == EdgeSpanMode.Custom ? Visibility.Collapsed : Visibility.Visible;
        if (notify) Changed?.Invoke(this, EventArgs.Empty);
    }
}
