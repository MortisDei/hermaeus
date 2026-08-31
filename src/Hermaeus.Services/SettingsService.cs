using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class SettingsService : ISettingsService
{
    private sealed record MigrationFile(string SourcePath, string RelativePath);

    private sealed class DataRootMigration
    {
        private readonly string _previous;
        private readonly IReadOnlyList<(string SourcePath, string TargetPath)> _moved;
        private readonly IReadOnlyList<string> _createdDirectories;

        public DataRootMigration(string previous,
            IReadOnlyList<(string SourcePath, string TargetPath)> moved,
            IReadOnlyList<string> createdDirectories, SettingsSaveResult result)
        {
            _previous = previous;
            _moved = moved;
            _createdDirectories = createdDirectories;
            Result = result;
        }

        public SettingsSaveResult Result { get; }

        public void Commit()
        {
            if (!ModelPathSafety.AreSameLocalPath(_previous, DefaultDir) && Directory.Exists(_previous))
            {
                PruneEmptyDirectories(_previous);
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(_previous).Any())
                        Directory.Delete(_previous);
                }
                catch
                {
                    // The new root is already complete and active. An old
                    // duplicate root is safer than turning a successful save
                    // into an active-root mismatch because cleanup was denied.
                }
            }
        }

        public void Rollback()
        {
            var failures = new List<Exception>();
            for (var i = _moved.Count - 1; i >= 0; i--)
            {
                var (sourcePath, targetPath) = _moved[i];
                try
                {
                    var sourceExists = File.Exists(sourcePath);
                    var targetExists = File.Exists(targetPath);
                    if (sourceExists && targetExists)
                        throw new IOException($"Both migration paths exist for '{Path.GetRelativePath(_previous, sourcePath)}'.");
                    if (!sourceExists && !targetExists)
                        throw new IOException($"Neither migration path exists for '{Path.GetRelativePath(_previous, sourcePath)}'.");
                    if (targetExists)
                        File.Move(targetPath, sourcePath);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            foreach (var directory in _createdDirectories.OrderByDescending(path => path.Length))
            {
                try
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            if (failures.Count > 0)
                throw new IOException("Data-root migration rollback failed; the old and new roots require manual reconciliation.", new AggregateException(failures));
        }
    }

    private const int MaxPerConversationMemoryOverrides = 1000;
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private static readonly string DefaultDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermaeus");
    private readonly string _path;
    private readonly Action<string, string> _moveFile;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppSettings Settings { get; private set; } = new();
    public event EventHandler? SettingsChanged;
    public event Action<string>? NormalizationWarning;

    public SettingsService() : this(Path.Combine(DefaultDir, "settings.json"), null) { }

    public SettingsService(string settingsPath) : this(settingsPath, null) { }

    internal SettingsService(string settingsPath, Action<string, string>? moveFile)
    {
        _path = Path.GetFullPath(settingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _moveFile = moveFile ?? File.Move;
    }

    public static string ResolveDataRoot(AppSettings settings)
    {
        var configured = settings.DataManagement.DataRootDirectory?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? DefaultDir : Path.GetFullPath(configured);
    }

    public static void NormalizeManagedServers(List<ServerConfig> servers, Action<string>? warning = null)
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

            UpgradeSpeculativeDecoding(server);
            NormalizeKvCache(server, warning);
            server.PromptThreads = Math.Max(0, server.PromptThreads);
            NormalizeGpuPlacement(server, warning);

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

    private static void NormalizeGpuPlacement(ServerConfig server, Action<string>? warning)
    {
        if (server.GpuPlacement is null)
        {
            if (GpuPlacementIntent.TryFromLegacy(server.GpuLayers, out var legacy, out var legacyError))
            {
                server.GpuPlacement = legacy;
                server.GpuPlacementValidationError = string.Empty;
            }
            else
            {
                server.GpuPlacementValidationError = legacyError ?? "GPU placement is invalid.";
                warning?.Invoke($"Managed server '{server.Name}' has invalid GPU placement: {server.GpuPlacementValidationError} Repair it before launching.");
            }

            return;
        }

        if (!server.GpuPlacement.TryValidate(out var error))
        {
            server.GpuPlacementValidationError = error ?? "GPU placement is invalid.";
            warning?.Invoke($"Managed server '{server.Name}' has invalid GPU placement: {server.GpuPlacementValidationError} Repair it before launching.");
            return;
        }

        server.GpuPlacementValidationError = string.Empty;
        if (server.GpuPlacement.LegacyGpuLayers is int legacyLayers)
            server.GpuLayers = legacyLayers;
    }

    private static void NormalizeKvCache(ServerConfig server, Action<string>? warning)
    {
        var canonical = string.IsNullOrWhiteSpace(server.KvCacheType) ? "f16" : server.KvCacheType.Trim();
        var legacyK = string.IsNullOrWhiteSpace(server.KvCacheTypeK) ? "f16" : server.KvCacheTypeK.Trim();
        var legacyV = string.IsNullOrWhiteSpace(server.KvCacheTypeV) ? "f16" : server.KvCacheTypeV.Trim();

        if (string.Equals(canonical, "f16", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(legacyK, "f16", StringComparison.OrdinalIgnoreCase))
            canonical = legacyK;

        if (!string.Equals(legacyK, legacyV, StringComparison.OrdinalIgnoreCase))
            warning?.Invoke($"Managed server '{server.Name}' had divergent legacy K/V cache types; using K value '{legacyK}'.");

        server.KvCacheType = canonical;
        server.KvCacheTypeK = canonical;
        server.KvCacheTypeV = canonical;
    }

    /// <summary>
    /// r27 03-drafting-and-proof.md 3.1: a legacy <c>NgramSpeculative: true</c>
    /// becomes <c>Speculative.Types = ["ngram-mod"]</c>, which is exactly the
    /// flag pair the bool used to emit, so a settings file written by 0.33.0
    /// produces byte-identical launch arguments after the upgrade.
    /// Runs exactly once: the bool is cleared as it is read, and a config that
    /// already has types is left alone so the upgrade cannot duplicate or
    /// resurrect a type the user has since removed.
    /// </summary>
    public static void UpgradeSpeculativeDecoding(ServerConfig server)
    {
        server.Speculative ??= new SpeculativeDecodingConfig();
        if (!server.NgramSpeculative)
            return;

        server.NgramSpeculative = false;
        if (server.Speculative.Types.Count > 0)
            return;

        server.Speculative.Types.Add("ngram-mod");
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
            // Materialize the typed placement in memory, but do not persist it
            // merely because the application started. The next owner-initiated
            // settings save writes the new shape.
            NormalizeSettings(Settings);
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
        => await SaveCandidateAsync(Settings, previousDataRootDirectory, replaceLiveSettings: false);

    public async Task<SettingsSaveResult> SaveAsync(AppSettings settings, string? previousDataRootDirectory = null)
        => await SaveCandidateAsync(settings, previousDataRootDirectory, replaceLiveSettings: true);

    /// <summary>
    /// Writes and validates a candidate before publishing it as the live
    /// settings object. Publishing earlier let observers see new in-memory
    /// values while a reload could still read the previous file from disk.
    /// </summary>
    private async Task<SettingsSaveResult> SaveCandidateAsync(
        AppSettings candidate,
        string? previousDataRootDirectory,
        bool replaceLiveSettings)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        DataRootMigration? migration = null;
        var currentDataRoot = string.Empty;
        await _gate.WaitAsync();
        try
        {
            NormalizeSettings(candidate);
            currentDataRoot = ResolveDataRoot(candidate);
            ValidateDataRoot(currentDataRoot);

            migration = previousDataRootDirectory is null
                ? null
                : MigrateDataRoot(previousDataRootDirectory, candidate.DataManagement.DataRootDirectory);

            Directory.CreateDirectory(currentDataRoot);
            await WriteTextAtomicAsync(_path, JsonSerializer.Serialize(candidate, Opts));
            migration?.Commit();
            if (replaceLiveSettings)
                Settings = candidate;
        }
        catch (Exception ex)
        {
            if (migration is not null)
            {
                try
                {
                    migration.Rollback();
                }
                catch (Exception rollbackException)
                {
                    throw new IOException("Data-root migration failed and automatic rollback also failed.",
                        new AggregateException(ex, rollbackException));
                }
            }

            if (previousDataRootDirectory is not null)
                candidate.DataManagement.DataRootDirectory = previousDataRootDirectory;
            throw;
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return migration?.Result ?? new SettingsSaveResult(false, null, currentDataRoot, null, 0);
    }

    public DataMigrationPlan PreviewDataRootMigration(string? previousDataRootDirectory, string? nextDataRootDirectory)
    {
        var previous = ResolveDataRoot(new AppSettings { DataManagement = { DataRootDirectory = previousDataRootDirectory ?? string.Empty } });
        var next = ResolveDataRoot(new AppSettings { DataManagement = { DataRootDirectory = nextDataRootDirectory ?? string.Empty } });
        if (ModelPathSafety.AreSameLocalPath(previous, next))
            return new DataMigrationPlan(false, previous, next, 0, []);

        if (!Directory.Exists(previous))
            return new DataMigrationPlan(false, previous, next, 0, []);

        var files = EnumerateMigrationFiles(previous).ToList();
        var conflicts = files
            .Select(f => Path.Combine(next, f.RelativePath))
            .Where(File.Exists)
            .ToList();

        // A conflict on every file means the target already holds its own
        // copy of the data - Save will repoint there without moving
        // anything (see MigrateDataRoot), not block. Only a partial
        // conflict is genuinely ambiguous and stays blocked.
        if (files.Count > 0 && conflicts.Count == files.Count)
            return new DataMigrationPlan(false, previous, next, 0, []);

        return new DataMigrationPlan(files.Count > 0 && conflicts.Count == 0, previous, next, files.Count, conflicts);
    }

    private DataRootMigration MigrateDataRoot(string? previousDataRootDirectory, string? nextDataRootDirectory)
    {
        var previous = ResolveDataRoot(new AppSettings { DataManagement = { DataRootDirectory = previousDataRootDirectory ?? string.Empty } });
        var next = ResolveDataRoot(new AppSettings { DataManagement = { DataRootDirectory = nextDataRootDirectory ?? string.Empty } });
        ValidateDataRoot(next);
        if (ModelPathSafety.AreSameLocalPath(previous, next))
            return NoMigration(previous, next);

        Directory.CreateDirectory(next);
        if (!Directory.Exists(previous))
            return NoMigration(previous, next);

        var files = EnumerateMigrationFiles(previous).ToList();
        if (files.Count == 0)
            return NoMigration(previous, next);

        var conflicts = files.Where(file => File.Exists(Path.Combine(next, file.RelativePath))).ToList();
        if (conflicts.Count == files.Count)
        {
            // Every file this migration would move already exists at the
            // destination - the user is repointing to a folder that already
            // holds its own copy of the data (e.g. undoing an accidental
            // reset back to the default root), not asking to move anything.
            // Treating that as a hard conflict used to throw here, which
            // left the settings save failed and the data root reverted to
            // blank with no way to just repoint without an unwanted move.
            return NoMigration(previous, next);
        }

        if (conflicts.Count > 0)
        {
            var target = Path.Combine(next, conflicts[0].RelativePath);
            throw new IOException($"Cannot move Hermaeus data because '{target}' already exists.");
        }

        var backupDir = Path.Combine(next, ".hermaeus-backups",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDir);
        foreach (var file in files)
        {
            var backupTarget = Path.Combine(backupDir, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupTarget)!);
            File.Copy(file.SourcePath, backupTarget);
        }

        var moved = new List<(string SourcePath, string TargetPath)>();
        var createdDirectories = new HashSet<string>(ModelPathSafety.LocalPathComparer);
        try
        {
            foreach (var file in files)
            {
                var target = Path.Combine(next, file.RelativePath);
                var targetDirectory = Path.GetDirectoryName(target)!;
                CreateMigrationDirectory(targetDirectory, createdDirectories);
                _moveFile(file.SourcePath, target);
                moved.Add((file.SourcePath, target));
                if (IsSecretsFile(target))
                    TryRestrictSecretsPermissions(target);
            }
        }
        catch (Exception ex)
        {
            try
            {
                new DataRootMigration(previous, moved, createdDirectories.ToArray(),
                    new SettingsSaveResult(true, previous, next, backupDir, moved.Count)).Rollback();
            }
            catch (Exception rollbackException)
            {
                throw new IOException("Data-root migration failed and automatic rollback also failed.",
                    new AggregateException(ex, rollbackException));
            }
            throw;
        }

        return new DataRootMigration(previous, moved, createdDirectories.ToArray(),
            new SettingsSaveResult(true, previous, next, backupDir, files.Count));
    }

    private static DataRootMigration NoMigration(string previous, string next) =>
        new(previous, [], [], new SettingsSaveResult(false, previous, next, null, 0));

    private static void CreateMigrationDirectory(string directory, ISet<string> createdDirectories)
    {
        if (Directory.Exists(directory))
            return;
        Directory.CreateDirectory(directory);
        createdDirectories.Add(directory);
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

    private void NormalizeSettings(AppSettings settings)
    {
        NormalizeManagedServers(settings.ManagedServers, message => NormalizationWarning?.Invoke(message));
        NormalizeTuneProfiles(settings.LlamaTuneProfiles);

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

    private static void NormalizeTuneProfiles(List<LlamaTuneProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            if (profile.GpuPlacement is null)
            {
                if (GpuPlacementIntent.TryFromLegacy(profile.GpuLayers, out var legacy, out _))
                    profile.GpuPlacement = legacy;
                continue;
            }

            if (profile.GpuPlacement.TryValidate(out _)
                && profile.GpuPlacement.LegacyGpuLayers is int legacyLayers)
                profile.GpuLayers = legacyLayers;
        }
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
        GpuPlacement = GpuPlacementIntent.Cpu(),
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
        if (string.Equals(full, root, ModelPathSafety.LocalPathComparison))
            throw new IOException("Hermaeus data root cannot be the filesystem root.");
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
