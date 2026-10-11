using System.Threading;
using System.Security.Cryptography;
using System.Text;

namespace WalleFences.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--hardware-sensor-host")) return HardwareSensorHost.Run(args);
        var layoutFlags = new[] { "--layout-template", "--import-layout", "--export-layout" };
        foreach (var flag in layoutFlags) { var flagIndex = Array.IndexOf(args, flag); if (flagIndex >= 0 && (flagIndex + 1 >= args.Length || args[flagIndex + 1].StartsWith("--", StringComparison.Ordinal))) return 2; }
        if (layoutFlags.Count(flag => args.Contains(flag)) > 1) return 2;
        var index = Array.IndexOf(args, "--data-dir");
        if (index >= 0 && index + 1 >= args.Length) return 2;
        var root = index >= 0 ? Path.GetFullPath(args[index + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WalleFences");
        Directory.CreateDirectory(root);
        if (args.Contains("--sensor-diagnostics"))
        {
            var sample = CpuTemperatureMonitor.Probe();
            File.WriteAllText(Path.Combine(root, "cpu-sensors.json"), System.Text.Json.JsonSerializer.Serialize(sample, WorkspaceStore.JsonOptions));
            return sample.State == TemperatureState.Error ? 1 : 0;
        }
        var exportIndex = Array.IndexOf(args, "--export-layout");
        if (exportIndex >= 0)
        {
            try { LayoutDeployment.Export(new WorkspaceStore(root), Path.GetFullPath(args[exportIndex + 1])); return 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(root, "deployment-error.txt"), ex.ToString()); return 1; }
        }
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..20];
        using var mutex = new Mutex(true, @"Local\WalleFences-" + identity, out var first);
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WalleFences-Activate-" + identity);
        if (!first) { if (layoutFlags.Any(flag => args.Contains(flag))) return 3; activate.Set(); return 0; }
        AppController? controller = null;
        RegisteredWaitHandle? listener = null;
        try
        {
            var templateIndex = Array.IndexOf(args, "--layout-template"); var importIndex = Array.IndexOf(args, "--import-layout");
            if (templateIndex >= 0) LayoutDeployment.Import(new WorkspaceStore(root), Path.GetFullPath(args[templateIndex + 1]), true);
            if (importIndex >= 0) LayoutDeployment.Import(new WorkspaceStore(root), Path.GetFullPath(args[importIndex + 1]));
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Ui.Theme(application);
            controller = new AppController(root);
            var activeController = controller;
            application.DispatcherUnhandledException += (_, e) =>
            {
                activeController.Log(e.Exception);
                Ui.Error(L.Text("Ошибка приложения: ") + e.Exception.Message); e.Handled = true;
            };
            application.Startup += async (_, _) =>
            {
                activeController.Start(args.Contains("--background"));
                if (!activeController.NeedsFirstRun && args.Contains("--organize-desktop")) await activeController.RedistributeDesktopAsync(args.Contains("--top-shelf"), args.Contains("--hide-desktop-icons"));
                if (args.Contains("--diagnose"))
                {
                    File.WriteAllText(Path.Combine(root, "diagnostics.json"), System.Text.Json.JsonSerializer.Serialize(new
                    {
                        activeController.DesktopStatus, activeController.HotkeyStatus,
                        Monitors = AppController.Displays().Select(d => new { d.Name, d.Primary, d.X, d.Y, d.Width, d.Height, Scale = d.Scale / AppController.GlobalScale }),
                        Panels = activeController.Windows.Select(w => new { w.Fence.Name, w.Fence.X, w.Fence.Y, w.Fence.Width, w.Fence.Height, w.Fence.Edge, w.Fence.Layer, w.Fence.DisplayMode, w.Fence.RevealTrigger, w.Fence.ContentLocked, ActualDpi = Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(w).Handle), AttachedToDesktop = Native.ChildParent(new System.Windows.Interop.WindowInteropHelper(w).Handle) != 0 }),
                        PanelCount = activeController.Windows.Count, ProcessId = Environment.ProcessId, SessionId = Process.GetCurrentProcess().SessionId
                    }, WorkspaceStore.JsonOptions));
                    application.Shutdown(0);
                }
            };
            listener = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => application.Dispatcher.BeginInvoke(activeController.ShowManager), null, Timeout.Infinite, false);
            return application.Run();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(root, "startup-error.txt"), ex.ToString());
            MessageBox.Show(ex.Message, "Walle Fences", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        finally { listener?.Unregister(null); controller?.Dispose(); mutex.ReleaseMutex(); }
    }
}
