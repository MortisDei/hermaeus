using Aether.Core.Models;
using Aether.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("data root migration previews moveable files", DataRootMigrationPreview),
    ("data root migration refuses conflicts", DataRootMigrationRefusesConflicts),
    ("data root migration moves db files and leaves no junk", DataRootMigrationMovesFiles),
    ("backup excludes secrets and refuses overwrite restore", BackupExcludesSecretsAndRefusesOverwrite),
    ("redaction hides common secrets and home path", RedactionHidesSecrets)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failed > 0)
    return failed;

Console.WriteLine($"All {tests.Length} Aether tests passed.");
return 0;

static async Task DataRootMigrationPreview()
{
    using var temp = new TempDir();
    var previous = temp.PathFor("previous");
    var next = temp.PathFor("next");
    Directory.CreateDirectory(previous);
    File.WriteAllText(Path.Combine(previous, "conversations.db"), "db");
    File.WriteAllText(Path.Combine(previous, "conversations.db-wal"), "wal");

    var service = NewSettings(temp);
    var plan = service.PreviewDataRootMigration(previous, next);

    Equal(true, plan.WillMove, "migration should be allowed");
    Equal(2, plan.FilesToMove, "all conversation db files should be counted");
    Equal(0, plan.Conflicts.Count, "clean target should have no conflicts");
    await Task.CompletedTask;
}

static async Task DataRootMigrationRefusesConflicts()
{
    using var temp = new TempDir();
    var previous = temp.PathFor("previous");
    var next = temp.PathFor("next");
    Directory.CreateDirectory(previous);
    Directory.CreateDirectory(next);
    File.WriteAllText(Path.Combine(previous, "conversations.db"), "old");
    File.WriteAllText(Path.Combine(next, "conversations.db"), "existing");

    var service = NewSettings(temp);
    var plan = service.PreviewDataRootMigration(previous, next);
    Equal(false, plan.WillMove, "migration preview should refuse conflicts");
    Equal(1, plan.Conflicts.Count, "conflicting db should be reported");

    service.Settings.DataRootDirectory = next;
    await ThrowsAsync<IOException>(() => service.SaveAsync(previous));
}

static async Task DataRootMigrationMovesFiles()
{
    using var temp = new TempDir();
    var previous = temp.PathFor("previous");
    var next = temp.PathFor("next");
    Directory.CreateDirectory(previous);
    File.WriteAllText(Path.Combine(previous, "conversations.db"), "db");
    File.WriteAllText(Path.Combine(previous, "conversations.db-shm"), "shm");

    var service = NewSettings(temp);
    service.Settings.DataRootDirectory = next;
    var result = await service.SaveAsync(previous);

    Equal(true, result.DataMigrated, "migration should report moved data");
    Equal(2, result.FilesMoved, "all db files should move");
    True(File.Exists(Path.Combine(next, "conversations.db")), "db should exist in new root");
    True(File.Exists(Path.Combine(next, "conversations.db-shm")), "sidecar db file should exist in new root");
    False(File.Exists(Path.Combine(previous, "conversations.db")), "old db should not be left behind");
    True(result.BackupDirectory is not null && File.Exists(Path.Combine(result.BackupDirectory, "conversations.db")),
        "migration should keep a backup copy in the target backup folder");
}

static async Task BackupExcludesSecretsAndRefusesOverwrite()
{
    using var temp = new TempDir();
    var root = temp.PathFor("root");
    var backupTarget = temp.PathFor("backup");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "conversations.db"), "db");
    File.WriteAllText(Path.Combine(root, "secrets.local.json"), "secret");

    var service = NewSettings(temp);
    service.Settings.DataRootDirectory = root;
    var backups = new BackupService(service);
    var backup = await backups.BackupAsync(backupTarget);

    using (var archive = System.IO.Compression.ZipFile.OpenRead(backup.Path))
    {
        True(archive.GetEntry("conversations.db") is not null, "conversation db should be backed up");
        True(archive.GetEntry("secrets.local.json") is null, "local secrets should not be backed up");
    }

    var restoreRoot = temp.PathFor("restore");
    Directory.CreateDirectory(restoreRoot);
    File.WriteAllText(Path.Combine(restoreRoot, "conversations.db"), "existing");
    service.Settings.DataRootDirectory = restoreRoot;
    await ThrowsAsync<IOException>(() => backups.RestoreAsync(backup.Path));
}

static Task RedactionHidesSecrets()
{
    var redactor = new RedactionService();
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var value = $"{home}/project api_key=abcdefghi123456789 bearer token_123456789012345 sk-abc123456789abcdef";
    var redacted = redactor.Redact(value);

    False(redacted.Contains("abcdefghi123456789", StringComparison.Ordinal), "api key value should be removed");
    False(redacted.Contains("token_123456789012345", StringComparison.Ordinal), "bearer token should be removed");
    False(redacted.Contains("sk-abc123456789abcdef", StringComparison.Ordinal), "sk token should be removed");
    if (!string.IsNullOrWhiteSpace(home))
        False(redacted.Contains(home, StringComparison.Ordinal), "home path should be shortened");

    return Task.CompletedTask;
}

static SettingsService NewSettings(TempDir temp) => new(temp.PathFor("settings/settings.json"));

static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}. Expected '{expected}', got '{actual}'.");
}

static void True(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}

static void False(bool value, string message) => True(!value, message);

sealed class TempDir : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aether-tests-{Guid.NewGuid():N}");

    public string PathFor(string relative) => Path.Combine(_root, relative);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
