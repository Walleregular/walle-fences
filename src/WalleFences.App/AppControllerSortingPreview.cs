namespace WalleFences.App;

internal sealed partial class AppController
{
    private int _sortingPreviewDepth;
    internal bool AutomaticSortingPaused => _sortingPreviewDepth > 0;

    internal IDisposable PauseAutomaticSortingForPreview()
    {
        _sortingPreviewDepth++;
        return new SortingPreviewScope(this);
    }

    private sealed class SortingPreviewScope(AppController owner) : IDisposable
    {
        private AppController? _owner = owner;
        public void Dispose()
        {
            var controller = _owner; if (controller == null) return; _owner = null;
            if (--controller._sortingPreviewDepth == 0 && !controller._disposed) controller.QueueRefresh();
        }
    }
}
