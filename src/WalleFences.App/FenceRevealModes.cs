using System.Windows.Controls.Primitives;

namespace WalleFences.App;

internal sealed partial class FenceWindow
{
    private RevealTrigger _appliedRevealTrigger;
    private bool _openedAutomatically;
    private Thumb? _headerMover;
    private bool KeepsExpanded => Fence.RevealTrigger == RevealTrigger.AlwaysOpen;
    private bool OpensOnPointer => Fence.RevealTrigger is RevealTrigger.Proximity or RevealTrigger.HoverPause or RevealTrigger.Hover;
    private double RevealMargin(DisplayArea display) => IsEdgeDock && Fence.RevealTrigger != RevealTrigger.Hover ? Fence.ActivationDistance * display.Scale : 0;

    private void SynchronizeRevealMode()
    {
        if (_appliedRevealTrigger != Fence.RevealTrigger)
        {
            _appliedRevealTrigger = Fence.RevealTrigger;
            _openedAutomatically = false;
            _hoverExpanded = false;
            _reveal.Reset();
            _suppressRevealUntilLeave = false;
        }
        if (KeepsExpanded)
        {
            _openedAutomatically = false;
            _hoverExpanded = true;
            _reveal.Reset(true);
            _suppressRevealUntilLeave = false;
        }
    }

    private void RevealFromHeader()
    {
        if (_closed || !VisuallyCollapsed) return;
        _openedAutomatically = false;
        _hoverExpanded = true;
        _reveal.Reset(true);
        _suppressRevealUntilLeave = false;
        RebuildTabs();
        SetRollHeight();
        ApplyChameleon();
    }

    internal void ApplyGameModeGuard(bool blocked)
    {
        if (!blocked || _closed || !IsVisible || _app.IsPeek || KeepsExpanded || !_openedAutomatically) return;
        // An explicit interaction takes ownership of a panel that was initially opened by hover.
        // Do not close a context menu, keyboard session or a drag underneath the user.
        if (IsKeyboardFocusWithin || IsMouseCaptureWithin || IsResizing || Mouse.LeftButton == MouseButtonState.Pressed
            || ContextMenu?.IsOpen == true || HasOpenTabContextMenu || OwnedWindows.Count > 0) return;
        _openedAutomatically = false; _hoverExpanded = false; _reveal.Reset(); _suppressRevealUntilLeave = true;
        UpdateTabStates(); SetRollHeight(); ApplyChameleon();
    }

    private string HeaderRevealHint => L.Text(KeepsExpanded ? "Перетащите группу." : VisuallyCollapsed && Fence.RevealTrigger == RevealTrigger.Click ? "Щелчок — развернуть. Перетащите для перемещения." : VisuallyCollapsed && Fence.RevealTrigger == RevealTrigger.DoubleClick ? "Двойной щелчок — развернуть. Перетащите для перемещения." : "Перетащите группу. Двойной щелчок — свернуть / развернуть.");
    private static string AlwaysOpenHint => L.Text("В режиме «Всегда раскрыта» содержимое остаётся открытым.");
}
