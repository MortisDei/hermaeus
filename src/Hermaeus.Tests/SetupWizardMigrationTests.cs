using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r12 01-settings-lifecycle.md 1.1: re-running the wizard (Settings'
/// "re-run setup" link) and changing the data root used to call a plain
/// SaveAsync(), bypassing migration entirely - the same "conversations lost"
/// symptom the r11 wizard-singleton fix addressed, through a second door.
/// </summary>
internal static class SetupWizardMigrationTests
{
    private static SetupWizardViewModel NewWizard(ISettingsService settings) => new(
        settings, new RuntimeProfileService(settings), new FakeVoiceProviderRegistry(settings),
        new FakeDoctorService(), new FakeToasts(), new FakeSystemInfo());

    public static async Task WizardDataRootStepMigratesExistingDatabasesWithAToast()
    {
        using var temp = new TempDir();
        var oldRoot = temp.PathFor("old-root");
        Directory.CreateDirectory(oldRoot);
        await File.WriteAllTextAsync(Path.Combine(oldRoot, "conversations.db"), "data");
        var newRoot = temp.PathFor("new-root");

        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = oldRoot;
        var toasts = new FakeToasts();
        string? toastTitle = null;
        toasts.ToastRaised += t => toastTitle = t.Title;
        var wizard = new SetupWizardViewModel(
            settings, new RuntimeProfileService(settings), new FakeVoiceProviderRegistry(settings),
            new FakeDoctorService(), toasts, new FakeSystemInfo());
        wizard.LoadFromSettings();
        wizard.DataRootDirectory = newRoot;

        await wizard.NextCommand.ExecuteAsync(null);

        True(File.Exists(Path.Combine(newRoot, "conversations.db")), "the wizard's data-root step must move the same file the Settings page would move");
        Equal(newRoot, settings.Settings.DataManagement.DataRootDirectory, "settings should now point at the new root");
        Equal(1, wizard.StepIndex, "a successful migration should advance to the next step");
        Equal("Hermaeus data moved", toastTitle ?? string.Empty, "a migration toast should be shown, matching the Settings page's save flow");
    }

    /// <summary>A partial conflict (some but not all files clash) stays genuinely
    /// ambiguous, so the wizard still refuses rather than guessing which side wins.</summary>
    public static async Task WizardDataRootStepRefusesAPartiallyConflictingTargetAndKeepsSettingsOnTheOldRoot()
    {
        using var temp = new TempDir();
        var oldRoot = temp.PathFor("old-root");
        Directory.CreateDirectory(oldRoot);
        await File.WriteAllTextAsync(Path.Combine(oldRoot, "conversations.db"), "data");
        await File.WriteAllTextAsync(Path.Combine(oldRoot, "memories.db"), "memory-data");
        var newRoot = temp.PathFor("new-root");
        Directory.CreateDirectory(newRoot);
        await File.WriteAllTextAsync(Path.Combine(newRoot, "conversations.db"), "already exists");

        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = oldRoot;
        var wizard = NewWizard(settings);
        wizard.LoadFromSettings();
        wizard.DataRootDirectory = newRoot;

        await wizard.NextCommand.ExecuteAsync(null);

        Equal(0, wizard.StepIndex, "a partially conflicting target must refuse to advance the step");
        Equal(oldRoot, settings.Settings.DataManagement.DataRootDirectory, "settings on disk must still point at the old root");
        Equal("already exists", await File.ReadAllTextAsync(Path.Combine(newRoot, "conversations.db")), "the conflicting target file must not be overwritten");
    }

    /// <summary>Regression for a real field incident: a blanked DataRootDirectory made
    /// the app fall back to the default root and create fresh stray files there.
    /// Repointing back at the user's real, already fully populated folder must
    /// succeed as a plain repoint (every file conflicts, so there is nothing
    /// ambiguous to move) instead of refusing forever with no way out.</summary>
    public static async Task WizardDataRootStepRepointsWithoutMovingWhenTargetAlreadyHasEveryFile()
    {
        using var temp = new TempDir();
        var oldRoot = temp.PathFor("old-root");
        Directory.CreateDirectory(oldRoot);
        await File.WriteAllTextAsync(Path.Combine(oldRoot, "conversations.db"), "stray");
        var newRoot = temp.PathFor("new-root");
        Directory.CreateDirectory(newRoot);
        await File.WriteAllTextAsync(Path.Combine(newRoot, "conversations.db"), "real-data");

        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = oldRoot;
        var wizard = NewWizard(settings);
        wizard.LoadFromSettings();
        wizard.DataRootDirectory = newRoot;

        await wizard.NextCommand.ExecuteAsync(null);

        Equal(1, wizard.StepIndex, "repointing to an already-fully-populated folder should advance the step");
        Equal(newRoot, settings.Settings.DataManagement.DataRootDirectory, "settings should now point at the new root");
        Equal("stray", await File.ReadAllTextAsync(Path.Combine(oldRoot, "conversations.db")), "the old root's file must be left untouched, not deleted");
        Equal("real-data", await File.ReadAllTextAsync(Path.Combine(newRoot, "conversations.db")), "the target's existing file must not be overwritten");
    }

    public static async Task WizardDataRootStepOnFirstRunCompletesWithoutMigrationNoise()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var wizard = NewWizard(settings);
        wizard.LoadFromSettings();
        wizard.DataRootDirectory = temp.PathFor("fresh-root");

        await wizard.NextCommand.ExecuteAsync(null);

        Equal(1, wizard.StepIndex, "first run with nothing to migrate should still advance");
        Equal(temp.PathFor("fresh-root"), settings.Settings.DataManagement.DataRootDirectory, "settings should record the newly chosen root");
    }
}
