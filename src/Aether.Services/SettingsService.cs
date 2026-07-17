using System.Text.Json;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class SettingsService : ISettingsService
{
    private sealed record MigrationFile(string SourcePath, string RelativePath);

    private const int MaxPerConversationMemoryOverrides = 1000;
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private static readonly string DefaultDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether");
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppSettings Settings { get; private set; } = new();
    public event EventHandler? SettingsChanged;

    public SettingsService()
    {
        Directory.CreateDirectory(DefaultDir);
        _path = Path.Combine(DefaultDir, "settings.json");
    }

    public SettingsService(string settingsPath)
    {
        _path = Path.GetFullPath(settingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public static string ResolveDataRoot(AppSettings settings)
    {
        var configured = settings.DataManagement.DataRootDirectory?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? DefaultDir : Path.GetFullPath(configured);
    }

    public static void NormalizeManagedServers(List<ServerConfig> servers)
    {
        if (servers.Count == 0)
        {
            servers.Add(CreateDefaultServer(false));
            servers.Add(CreateDefaultServer(true));
            return;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDefaultRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = servers.Count - 1; i >= 0; i--)
        {
            var server = servers[i];
            if (string.IsNullOrWhiteSpace(server.Id))
                server.Id = Guid.NewGuid().ToString();

            if (!seenIds.Add(server.Id))
            {
                servers.RemoveAt(i);
                continue;
            }

            if (!IsDefaultManagedServerName(server))
                continue;

            var role = server.EmbeddingsMode ? "embeddings" : "chat";
            if (!seenDefaultRoles.Add(role))
                servers.RemoveAt(i);
        }

        if (!servers.Any(server => !server.EmbeddingsMode))
            servers.Insert(0, CreateDefaultServer(false));
        if (!servers.Any(server => server.EmbeddingsMode))
            servers.Add(CreateDefaultServer(true));
    }

    public async Task LoadAsync()
    {
        var needsPersist = false;
        var notify = false;
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(_path)) return;
            var json = await File.ReadAllTextAsync(_path);
            Settings = JsonSerializer.Deserialize<AppSettings>(json, Opts) ?? new();
            needsPersist = MigrateLegacyLocalEndpoints(Settings);
            needsPersist |= MigrateLegacyLocalApiToken(Settings);
            notify = true;
        }
        catch
        {
            TryBackupUnreadableSettings(_path);
            Settings = new();
            notify = true;
        }
        finally
        {
            _gate.Release();
        }

        if (needsPersist)
        {
            await SaveAsync();
            return;
        }

        if (notify)
            SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<SettingsSaveResult> SaveAsync(string? previousDataRootDirectory = null)
    {
        SettingsSaveResult migration;
        await _gate.WaitAsync();
        try
        {
            NormalizeSettings(Settings);
            var currentDataRoot = ResolveDataRoot(Settings);
            ValidateDataRoot(currentDataRoot);

            migration = previousDataRootDirectory is null
                ? new SettingsSaveResult(false, null, currentDataRoot, null, 0)
                : MigrateDataRoot(previousDataRootDirectory, Settings.DataManagement.DataRootDirectory);

            Directory.CreateDirectory(currentDataRoot);
            await WriteTextAtomicAsync(_path, JsonSerializer.Serialize(Settings, Opts));
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return migration;
    }

    public async Task<SettingsSaveResult> SaveAsync(AppSettings settings, string? previousDataRootDirectory = null)
    {
        var previous = Settings;
        Settings = settings;
        try
        {
            return await SaveAsync(previousDataRootDirectory);
        }
        catch
        {
            Settings = previous;
            throw;
        }
    }

    public DataMigrationPlan PreviewDataRootMigration(string? previousDataRootDirectory, string? nextDataRootDirectory)
    {
        var previous = ResolveDataRoot(new AppSettings { DataManagement = { DataRootDirectory = previousDataRootDirectory ?? string.Empty } });
        var next = ResolveDataRoot(new AppSettings { DataManagement = { DataRootDirectory = nextDataRootDirectory ?? string.Empty } });
        if (string.Equals(previous, next, StringComparison.OrdinalIgnoreCase))
            return new DataMigrationPlan(false, previous, next, 0, []);

        if (!Directory.Exists(previous))
            return new DataMigrationPlan(false, previous, next, 0, []);

        var files = EnumerateMigrationFiles(previous).ToList();
        var conflicts = files
            .Select(f => Path.Combine(next, f.RelativePath))
            .Where(File.Exists)
            .ToList();

        return new DataMigrationPlan(files.Count > 0 && conflicts.Count == 0, previous, next, files.Count, conflicts);
    }

    private static SettingsSaveResult MigrateDataRoot(string? previousDataRootDirectory, string? nextDataRootDirectory)
    {
        var previous = ResolveDataRoot(new AppSettings { DataManagement = { DataRootDirectory = previousDataRootDirectory ?? string.Empty } });
        var next = ResolveDataRoot(new AppSettings { DataManagement = { DataRootDirectory = nextDataRootDirectory ?? string.Empty } });
        ValidateDataRoot(next);
        if (string.Equals(previous, next, StringComparison.OrdinalIgnoreCase))
            return new SettingsSaveResult(false, previous, next, null, 0);

        Directory.CreateDirectory(next);
        if (!Directory.Exists(previous))
            return new SettingsSaveResult(false, previous, next, null, 0);

        var files = EnumerateMigrationFiles(previous).ToList();
        if (files.Count == 0)
            return new SettingsSaveResult(false, previous, next, null, 0);

        foreach (var file in files)
        {
            var target = Path.Combine(next, file.RelativePath);
            if (File.Exists(target))
                throw new IOException($"Cannot move Aether data because '{target}' already exists.");
        }

        var backupDir = Path.Combine(next, ".aether-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backupDir);
        foreach (var file in files)
        {
            var backupTarget = Path.Combine(backupDir, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupTarget)!);
            File.Copy(file.SourcePath, backupTarget);
        }

        foreach (var file in files)
        {
            var target = Path.Combine(next, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(file.SourcePath, target);
            if (IsSecretsFile(target))
                TryRestrictSecretsPermissions(target);
        }

        // Moving every file (r11 3.1) can leave now-empty subdirectories
        // (logs/, voice/, agent-scenarios/, ...) behind; prune those before
        // checking whether the old root itself is empty, or a full migration
        // never cleans up the old root at all.
        if (!string.Equals(previous, DefaultDir, StringComparison.OrdinalIgnoreCase) && Directory.Exists(previous))
        {
            PruneEmptyDirectories(previous);
            if (!Directory.EnumerateFileSystemEntries(previous).Any())
                Directory.Delete(previous);
        }

        return new SettingsSaveResult(true, previous, next, backupDir, files.Count);
    }

    private static void PruneEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch
            {
            }
        }
    }

    private static void NormalizeSettings(AppSettings settings)
    {
        NormalizeManagedServers(settings.ManagedServers);

        if (settings.Memory.EnabledPerConversation.Count == 0)
            return;

        foreach (var key in settings.Memory.EnabledPerConversation
                     .Where(pair => pair.Value == settings.Memory.Enabled)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            settings.Memory.EnabledPerConversation.Remove(key);
        }

        if (settings.Memory.EnabledPerConversation.Count <= MaxPerConversationMemoryOverrides)
            return;

        var keep = settings.Memory.EnabledPerConversation
            .OrderByDescending(pair => pair.Key, StringComparer.Ordinal)
            .Take(MaxPerConversationMemoryOverrides)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        settings.Memory.EnabledPerConversation = keep;
    }

    private static bool MigrateLegacyLocalEndpoints(AppSettings settings)
    {
        const string legacyChatBaseUrl = "http://localhost:8080";
        var changed = false;

        if (string.Equals(settings.Llm.LlamaCppBaseUrl?.Trim(), legacyChatBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            settings.Llm.LlamaCppBaseUrl = "http://localhost:39201";
            changed = true;
        }

        var embeddingBaseUrl = settings.Rag.EmbeddingBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(embeddingBaseUrl)
            || string.Equals(embeddingBaseUrl, legacyChatBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            settings.Rag.EmbeddingBaseUrl = "http://localhost:39202";
            changed = true;
        }

        foreach (var server in settings.ManagedServers)
        {
            if (!IsDefaultManagedServerName(server))
                continue;

            if (!server.EmbeddingsMode && server.Port == 8080)
            {
                server.Port = 39201;
                changed = true;
            }

            if (server.EmbeddingsMode && server.Port == 8081)
            {
                server.Port = 39202;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Converts a pre-per-app-tokens single shared ApiToken into a "Default"
    /// named token so an existing user's working integrations keep working
    /// after upgrading (docs/review/03-next-level-roadmap.md Phase 2).
    /// </summary>
    private static bool MigrateLegacyLocalApiToken(AppSettings settings)
    {
        if (settings.LocalApi.Tokens.Count > 0 || string.IsNullOrWhiteSpace(settings.LocalApi.ApiToken))
            return false;

        settings.LocalApi.Tokens.Add(new LocalApiTokenEntry { Name = "Default", SecretRef = settings.LocalApi.ApiToken });
        settings.LocalApi.ApiToken = string.Empty;
        return true;
    }

    private static ServerConfig CreateDefaultServer(bool embeddingsMode) => new()
    {
        Name = embeddingsMode ? "Embeddings" : "Chat",
        ExecutablePath = "llama-server",
        Port = embeddingsMode ? 39202 : 39201,
        ContextSize = embeddingsMode ? 2048 : 4096,
        GpuLayers = 0,
        Threads = 4,
        EmbeddingsMode = embeddingsMode,
        AutoStart = false
    };

    private static bool IsDefaultManagedServerName(ServerConfig server)
    {
        var name = server.Name.Trim();
        return server.EmbeddingsMode
            ? name.Equals("Embeddings", StringComparison.OrdinalIgnoreCase)
            : name.Equals("Chat", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// r11 3.1: previously moved only conversations.db*/memories.db*/benchmarks.db*/agent/,
    /// stranding secrets.local.*, traces.db, eval_runs.db, logs/, voice/lexicon.txt,
    /// agent-scenarios/, and eval-runs/ in the old root. Now backed by
    /// DataRootManifest, the same enumerator BackupService uses, so a moved data
    /// root and a backup can never disagree about what the data root contains.
    /// </summary>
    private static IEnumerable<MigrationFile> EnumerateMigrationFiles(string root) =>
        DataRootManifest.EnumerateAll(root).Select(f => new MigrationFile(f.SourcePath, f.RelativePath));

    private static bool IsSecretsFile(string path) =>
        string.Equals(Path.GetFileName(path), "secrets.local.json", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetFileName(path), "secrets.local.key", StringComparison.OrdinalIgnoreCase);

    /// <summary>Mirrors SecretStore's own TryRestrictPermissions so a moved secrets file keeps the same owner-only mode it had before the move (r11 3.1 security-review note).</summary>
    private static void TryRestrictSecretsPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
        }
    }

    private static void ValidateDataRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
            throw new IOException("Data root must be an absolute path.");

        var full = Path.GetFullPath(path);
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Aether data root cannot be the filesystem root.");
    }

    private static async Task WriteTextAtomicAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryBackupUnreadableSettings(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var backupPath = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(path, backupPath, overwrite: false);
        }
        catch
        {
        }
    }
}
