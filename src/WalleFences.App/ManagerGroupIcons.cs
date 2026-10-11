namespace WalleFences.App;

internal sealed partial class ManagerWindow
{
    private void ChooseGroupIcon(Fence fence)
    {
        if (GroupIcons.Pick(fence, fence.Icon, this) is not { } selected || selected == fence.Icon) return;
        _app.Mutate(() =>
        {
            var current = _app.Workspace.Fences.FirstOrDefault(candidate => candidate.Id == fence.Id)
                ?? throw new InvalidOperationException(L.Text("Группа больше не существует."));
            current.Icon = selected;
        });
        _app.SetStatus(L.Text("Изменения сохранены"), refreshViews: false);
    }
}
