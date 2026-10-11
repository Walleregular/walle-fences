using System.Globalization;
using System.Threading.Tasks;

namespace WalleFences.App;

internal sealed partial class AppController
{
    private FirstRunWindow? _firstRunWindow;
    private bool _firstRunCompleting;
    internal bool NeedsFirstRun => Workspace.Preferences.SetupVersion >= 1 && !Workspace.Preferences.FirstRunCompleted;

    private static Workspace CreateInitialWorkspace()
    {
        var systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var language = L.Codes.Contains(systemLanguage, StringComparer.Ordinal) ? systemLanguage : "en";
        L.SetLanguage(language);
        return new Workspace
        {
            Preferences = new Preferences
            {
                Language = language, AdvancedSettings = false,
                SetupVersion = 1, FirstRunCompleted = false,
                AutoOrganize = false, HideDesktopIcons = false
            }
        };
    }

    private void MigrateFirstRunPreferences()
    {
        // Existing layouts already belong to their owner. An upgrade must not
        // restart setup, reimport files or replace any of their preferences.
        if (Workspace.Preferences.SetupVersion >= 1) return;
        Workspace.Preferences.SetupVersion = 1;
        Workspace.Preferences.FirstRunCompleted = true;
    }

    private void ShowFirstRun()
    {
        if (_disposed || !NeedsFirstRun) return;
        if (_firstRunWindow is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Show(); existing.Activate(); return;
        }
        var window = new FirstRunWindow(Workspace.Preferences.Language, Workspace.Preferences.AdvancedSettings,
            PreviewFirstRunPreferences, CompleteFirstRunAsync, FinishFirstRun);
        _firstRunWindow = window;
        window.Closed += (_, _) => { _firstRunWindow = null; if (!_disposed && NeedsFirstRun) SaveNow(); };
        try { window.Show(); window.Activate(); }
        catch { _firstRunWindow = null; throw; }
    }

    private void PreviewFirstRunPreferences(string language, bool advanced)
    {
        if (!NeedsFirstRun || _firstRunCompleting) return;
        var prefs = Workspace.Preferences;
        var renameInitialSet = Workspace.Pages.Count == 1 && Workspace.Fences.Count == 0
            && L.Codes.Any(code => Workspace.Pages[0].Name == L.Catalog(code)["Основной"]);
        prefs.Language = L.Normalize(language); prefs.AdvancedSettings = advanced;
        RefreshLanguage();
        if (renameInitialSet) Workspace.Pages[0].Name = L.Text("Основной");
        SaveNow();
    }

    private async Task CompleteFirstRunAsync(FirstRunChoices choices)
    {
        if (_disposed || !NeedsFirstRun || _firstRunCompleting) return;
        var original = WorkspaceStore.Clone(Workspace);
        // The rollback and every import snapshot must remain a pending setup.
        original.Preferences.SetupVersion = Math.Max(1, original.Preferences.SetupVersion);
        original.Preferences.FirstRunCompleted = false;
        _firstRunCompleting = true;
        var undoCount = _undo.Count;
        var startupChanged = false;
        var previousStartup = false;
        try
        {
            Workspace.Preferences.Language = L.Normalize(choices.Language);
            Workspace.Preferences.AdvancedSettings = choices.AdvancedSettings;
            // Initial import is a one-time action; ongoing sorting is enabled
            // explicitly later in the automation settings.
            Workspace.Preferences.AutoOrganize = false;
            RefreshLanguage();
            if (choices.ImportDesktop) await RedistributeDesktopAsync();
            if (_disposed) throw new OperationCanceledException();
            if (choices.AutoStart)
            {
                previousStartup = AutoStart;
                if (!previousStartup) { AutoStart = true; startupChanged = true; }
                if (!AutoStart) throw new InvalidOperationException(L.Text("Windows не сохранила настройку автозагрузки."));
            }
            var completed = WorkspaceStore.Clone(Workspace);
            completed.Preferences.FirstRunCompleted = true;
            completed.Preferences.OnboardingDismissed = !choices.ShowTraining;
            completed.Preferences.GuidedTourVersion = 1;
            completed.Preferences.GuidedTourStepId = "welcome";
            completed.Preferences.GuidedTourCompleted = false;
            // The window closes only after the completed state is durable.
            // Keep the live model pending while saving the final candidate, so
            // a failed write cannot leak Completed into a timer or snapshot.
            _saveTimer.Stop();
            Store.Save(completed);
            Workspace = completed;
            _undo.Clear();
        }
        catch
        {
            _saveTimer.Stop();
            Workspace = original;
            while (_undo.Count > undoCount) _undo.RemoveAt(_undo.Count - 1);
            if (startupChanged)
            {
                try { AutoStart = previousStartup; }
                catch (Exception rollbackError) { Log(rollbackError); }
            }
            RefreshLanguage();
            // Redistribution may already have saved its pending intermediate
            // layout. Persist the original pending state again on rollback.
            try { Store.Save(Workspace); }
            catch (Exception rollbackError) { Log(rollbackError); }
            throw;
        }
        finally { _firstRunCompleting = false; }
    }

    private void FinishFirstRun(bool showTraining)
    {
        if (_disposed || NeedsFirstRun) return;
        if (_tray != null)
        {
            _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); _tray = null; CreateTray();
        }
        Rebuild(); UpdateIntegration();
        _shellTimer.Start(); _pointerTimer.Start(); _focusTimer.Start();
        SetStatus(L.Text("Рабочий стол готов"));
        ShowManager();
        if (showTraining) Manager?.StartOnboarding();
    }
}
