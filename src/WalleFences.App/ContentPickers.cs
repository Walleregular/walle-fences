using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace WalleFences.App;

internal static class ContentPickers
{
    private sealed class DialogOwner(Window owner) : Forms.IWin32Window
    {
        public nint Handle { get; } = new WindowInteropHelper(owner).EnsureHandle();
    }

    internal static string[]? SelectFiles(Window owner)
    {
        // Virtual application shortcuts must reach our own importer unchanged.
        // The modern Shell dialog otherwise tries to resolve them before it returns.
        using var picker = new Forms.OpenFileDialog
        {
            Multiselect = true, Title = L.Text("Добавить ссылки на файлы"), CheckFileExists = true,
            CheckPathExists = true, DereferenceLinks = false, AutoUpgradeEnabled = false,
            RestoreDirectory = true, Filter = L.Text("Все файлы") + " (*.*)|*.*"
        };
        return picker.ShowDialog(new DialogOwner(owner)) == Forms.DialogResult.OK ? picker.FileNames : null;
    }

    internal static string? SelectFolder(Window owner, string title, string? currentPath = null)
    {
        using var picker = new Forms.FolderBrowserDialog
        {
            Description = title, UseDescriptionForTitle = true, AutoUpgradeEnabled = false,
            SelectedPath = currentPath is { Length: > 0 } && Directory.Exists(currentPath) ? currentPath : ""
        };
        return picker.ShowDialog(new DialogOwner(owner)) == Forms.DialogResult.OK ? picker.SelectedPath : null;
    }
}
