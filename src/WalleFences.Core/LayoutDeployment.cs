namespace WalleFences.Core;

// A template seeds only a new profile. An explicit import snapshots the existing profile first.
public static class LayoutDeployment
{
    public static bool Import(WorkspaceStore store, string source, bool onlyWhenEmpty = false)
    {
        if (onlyWhenEmpty && File.Exists(store.ConfigPath)) return false;
        var next = WorkspaceStore.Parse(File.ReadAllText(source));
        if (File.Exists(store.ConfigPath)) store.Snapshot(WorkspaceStore.Parse(File.ReadAllText(store.ConfigPath)), L.Text("Перед импортом шаблона"));
        store.Save(next); return true;
    }
    public static void Export(WorkspaceStore store, string destination)
    {
        var workspace = WorkspaceStore.Parse(File.ReadAllText(store.ConfigPath));
        if (PathExtensions.Same(destination, store.ConfigPath)) throw new ArgumentException("Choose an export file outside the active layout.", nameof(destination));
        File.WriteAllText(destination, System.Text.Json.JsonSerializer.Serialize(workspace, WorkspaceStore.JsonOptions));
    }
}
