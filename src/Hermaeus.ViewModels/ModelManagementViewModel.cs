using Hermaeus.Core.Models;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public partial class ModelManagementViewModel : ObservableObject
{
    private readonly ILlmService _llm;
    private readonly ModelProfileService _profiles;
    private readonly IToastService _toasts;
    private readonly ISettingsService _settings;
    private readonly ISystemInfoService _system;
    private readonly ServicesViewModel _services;
    private readonly ModelManifestStore _manifest;
    private readonly HuggingFaceClient _hf;
    private readonly ModelDownloadService _downloader;
    private readonly IActivityRecorder? _activity;
    private long _lastRefreshUtcTicks = DateTime.MinValue.Ticks;
    private readonly List<LlmModel> _modelCache = [];
    private readonly object _modelCacheLock = new();
    private readonly List<ModelProfileItemViewModel> _allModels = [];
    private CancellationTokenSource? _hfSelectionCts;

    /// <summary>The (possibly filtered) rows shown in the list. Filtering narrows this
    /// from <see cref="_allModels"/> without a refetch (r13 02-model-library.md 2.1).</summary>
    public UiBoundCollection<ModelProfileItemViewModel> Models { get; } = [];

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool   _isError;
    [ObservableProperty] private bool   _forceRefresh;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool   _isAutoTuningAll;
    [ObservableProperty] private string _autoTuneAllStatus = string.Empty;
    [ObservableProperty] private bool   _isOrganizing;
    [ObservableProperty] private string _organizeStatus = string.Empty;

    private volatile bool _isTuneInProgress;
    private CancellationTokenSource? _autoTuneAllCts;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        IEnumerable<ModelProfileItemViewModel> matches = string.IsNullOrEmpty(filter)
            ? _allModels
            : _allModels.Where(m =>
                m.EffectiveName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || m.RawName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || m.TagsDisplay.Contains(filter, StringComparison.OrdinalIgnoreCase));

        Models.Clear();
        foreach (var m in matches)
            Models.Add(m);
        OnPropertyChanged(nameof(HasNoModels));
    }

    /// <summary>
    /// r29 doc 02 2.2: drives the shared MossEmptyState. True both when nothing
    /// was detected and when the filter matched nothing; the empty state's own
    /// copy covers the first case and the filter box is visible above it for
    /// the second.
    /// </summary>
    public bool HasNoModels => Models.Count == 0;

    /// <summary>Emitted before moving any file so the view can show a "from -> to" preview and
    /// require an explicit confirmation; returning false cancels the whole operation without
    /// touching disk (r13 02-model-library.md 2.6).</summary>
    public Func<ModelOrganizePlan, Task<bool>>? RequestOrganizeConfirmation { get; set; }

    /// <summary>Emitted after a successful move, only when leftover empty directories exist;
    /// a second, separate confirmation from the move itself, and declining leaves the
    /// directories in place (never deletes files, r13 02-model-library.md 2.6).</summary>
    public Func<int, Task<bool>>? RequestEmptyDirectoryCleanupConfirmation { get; set; }
    public Func<ModelDeletionPlan, Task<bool>>? RequestDeleteModelConfirmation { get; set; }
    public Func<ModelDeletionPlan, Task<CompanionDisableChoice>>? RequestCompanionDisableConfirmation { get; set; }
    public Action<string>? RequestNavigate { get; set; }

    [RelayCommand]
    private void OpenServices() => RequestNavigate?.Invoke("services");

    public ModelManagementViewModel(ILlmService llm, ModelProfileService profiles, IToastService toasts, ISettingsService settings, ISystemInfoService system, ServicesViewModel services,
        ModelManifestStore manifest, HuggingFaceClient hf, ModelDownloadService downloader, IActivityRecorder? activity = null)
    {
        _activity = activity;
        _llm = llm;
        _profiles = profiles;
        _toasts = toasts;
        _settings = settings;
        _system = system;
        _services = services;
        _manifest = manifest;
        _hf = hf;
        _downloader = downloader;
    }

    private List<LlmModel> DiscoverLocalGgufModels(IReadOnlyList<LlmModel> existingModels)
    {
        return LocalAiAssetLocator.FindGgufModels(_settings.Settings.DataManagement.LocalAiAssetsRoot)
            .Where(path => !existingModels.Any(model => SameLocalModelIdentity(model.Id, path)))
            .Select(path => new LlmModel
            {
                Id = path,
                Name = Path.GetFileNameWithoutExtension(path),
                Provider = "local GGUF",
                ProviderTag = "llama.cpp",
                SizeBytes = new FileInfo(path).Length,
                ModifiedAt = File.GetLastWriteTimeUtc(path)
            })
            .ToList();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true; StatusMessage = string.Empty; IsError = false;
        try
        {
            List<LlmModel>? cachedModels;
            lock (_modelCacheLock)
            {
                var lastTicks = Interlocked.Read(ref _lastRefreshUtcTicks);
                var lastRefresh = new DateTime(lastTicks, DateTimeKind.Utc);
                var useCache = !ForceRefresh
                               && _modelCache.Count > 0
                               && DateTime.UtcNow - lastRefresh < TimeSpan.FromMinutes(2);
                cachedModels = useCache ? _modelCache.ToList() : null;
            }

            if (cachedModels is null && ForceRefresh)
                _llm.InvalidateModelCache();

            var reportedModels = cachedModels ?? await _llm.GetModelsAsync();
            if (cachedModels is null)
            {
                lock (_modelCacheLock)
                {
                    _modelCache.Clear();
                    _modelCache.AddRange(reportedModels);
                    Interlocked.Exchange(ref _lastRefreshUtcTicks, DateTime.UtcNow.Ticks);
                }
            }

            var runningIds = new HashSet<string>(reportedModels.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            var models = new List<LlmModel>(reportedModels);
            models.AddRange(DiscoverLocalGgufModels(models));

            _profiles.ApplyProfiles(models);
            var hardware = await _system.GetHardwareProfileAsync();
            var manifestEntries = await _manifest.LoadAsync();
            _allModels.Clear();
            foreach (var m in models)
            {
                var profile = _profiles.GetOrCreate(m.Id, m.Provider);
                var item = new ModelProfileItemViewModel(m, profile, runningIds.Contains(m.Id));
                RefreshTuneSummary(item);
                var existingTune = LlamaTuneProfileStore.Find(_settings.Settings, item.ModelId);
                var ggufInfo = item.IsLocalGguf
                    ? await Task.Run(() => GgufMetadataReader.TryRead(item.ModelId))
                    : null;
                ApplyFit(item, m.SizeBytes, hardware, ggufInfo, ResolveProbeContextSize(item, existingTune), profile.DefaultKvCacheType);
                ApplyManifestState(item, manifestEntries);
                _allModels.Add(item);
            }
            ApplyFilter();

            StatusMessage = models.Count == 0
                ? "No models detected. Add GGUF files to your AI assets root or start a runtime."
                : $"{models.Count} model(s) detected, {runningIds.Count} currently running{(cachedModels is not null ? " (from cache)" : "")}";
            ForceRefresh = false;
        }
        catch (Exception ex) { StatusMessage = ex.Message; IsError = true; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task SaveProfileAsync(ModelProfileItemViewModel? item)
    {
        if (item is null) return;

        var wasAutoManagingCompanions = _profiles.Get(item.ModelId)?.AutoManageCompanionAssets ?? true;
        if (wasAutoManagingCompanions && !item.AutoManageCompanionAssets)
        {
            var existing = await _manifest.FindAsync(item.ModelId);
            var companionFiles = existing?.Companions
                .Select(c => c.LocalFilePath)
                .Where(File.Exists)
                .ToList() ?? [];
            ModelDeletionPlan? removalPlan = null;
            var removalError = string.Empty;
            var removalPlanValid = companionFiles.Count > 0
                && ModelDeletionService.TryPlanFiles(
                    item.ModelId,
                    companionFiles,
                    _settings.Settings.DataManagement.LocalAiAssetsRoot,
                    $"Companion files for {item.EffectiveName}:\n{string.Join("\n", companionFiles)}",
                    out removalPlan,
                    out removalError);
            if (removalPlanValid)
            {
                var choice = RequestCompanionDisableConfirmation is null
                    ? CompanionDisableChoice.KeepFiles
                    : await RequestCompanionDisableConfirmation(removalPlan!);
                if (choice == CompanionDisableChoice.Cancel)
                {
                    item.AutoManageCompanionAssets = true;
                    return;
                }

                if (choice == CompanionDisableChoice.RemoveFiles)
                {
                    var remaining = ModelDeletionService.DeleteExact(removalPlan!);
                    if (remaining.Count > 0)
                    {
                        _toasts.Show("Companion removal incomplete", string.Join(", ", remaining), ToastKind.Warning, 7000);
                        return;
                    }

                    foreach (var file in companionFiles)
                        await _manifest.RemoveAsync(file);
                }
            }
            else if (companionFiles.Count > 0 && !string.IsNullOrWhiteSpace(removalError))
            {
                _toasts.Show("Cannot change companion policy", removalError, ToastKind.Warning, 7000);
                return;
            }
        }

        item.ApplyTuneProfileChanges();
        await _profiles.SaveAsync(item.ToProfile());
        item.ApplySavedState();
        lock (_modelCacheLock)
            _profiles.ApplyProfiles(_modelCache);
        _toasts.Show("Model profile saved", $"Updated metadata for {item.DisplayName}.", ToastKind.Success);
    }

    [RelayCommand]
    private async Task ResetProfileAsync(ModelProfileItemViewModel? item)
    {
        if (item is null) return;

        await _profiles.ResetAsync(item.ModelId);
        item.Reset();
        lock (_modelCacheLock)
            _profiles.ApplyProfiles(_modelCache);
        _toasts.Show("Model profile reset", $"Hermaeus metadata for {item.RawName} was cleared.", ToastKind.Info);
    }

    [RelayCommand]
    private async Task DeleteModelAsync(ModelProfileItemViewModel? item)
    {
        if (item is null || !item.IsLocalGguf) return;
        var running = _services.Servers.Any(s => s.IsRunning && ModelPathSafety.AreSameLocalPath(s.ModelPath, item.ModelId));
        if (!ModelDeletionService.TryPlan(item.ModelId, _settings.Settings.DataManagement.LocalAiAssetsRoot, running, out var plan, out var error))
        {
            _toasts.Show("Cannot delete model", error, ToastKind.Warning, 7000);
            return;
        }
        if (RequestDeleteModelConfirmation is null || !await RequestDeleteModelConfirmation(plan!)) return;
        var remaining = ModelDeletionService.DeleteExact(plan!);
        if (remaining.Count > 0)
        {
            _toasts.Show("Delete incomplete", $"Some files remain: {string.Join(", ", remaining)}", ToastKind.Warning, 8000);
            return;
        }
        await _manifest.RemoveAsync(item.ModelId);
        _settings.Settings.ModelProfiles.RemoveAll(p => ModelPathSafety.AreSameLocalPath(p.ModelId, item.ModelId));
        foreach (var server in _settings.Settings.ManagedServers.Where(s => ModelPathSafety.AreSameLocalPath(s.ModelPath, item.ModelId)))
            server.ModelPath = string.Empty;
        await _settings.SaveAsync();
        ForceRefresh = true;
        await RefreshAsync();
        _toasts.Show("Model deleted", $"Removed {item.RawName}.", ToastKind.Info);
    }

    private static void ApplyFit(ModelProfileItemViewModel item, long sizeBytes, HardwareProfile hardware, GgufModelInfo? info, int contextSize, string? kvCacheType = null)
    {
        var fit = ModelFitEstimator.Estimate(sizeBytes, hardware, info, contextSize, kvCacheType ?? "f16");
        item.FitTier = fit.Tier;
        item.FitReason = fit.Reason;
    }

    private static bool SameLocalModelIdentity(string reportedId, string localPath)
    {
        if (string.Equals(reportedId, localPath, ModelPathSafety.LocalPathComparison))
            return true;

        if (!reportedId.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            return false;

        return ModelPathSafety.AreSameLocalPath(reportedId, localPath);
    }

    /// <summary>Restores the update chip's last-known state from the manifest across a
    /// refresh; only models with a manifest entry carrying a RepoId participate at all -
    /// everything else shows nothing (r13 03-hugging-face.md 3.2).</summary>
    private static void ApplyManifestState(ModelProfileItemViewModel item, IReadOnlyList<ModelManifestEntry> manifestEntries)
    {
        var normalized = Path.GetFullPath(item.ModelId);
        var entry = manifestEntries.FirstOrDefault(e => ModelPathSafety.AreSameLocalPath(e.FilePath, normalized));
        if (entry is null || string.IsNullOrWhiteSpace(entry.RepoId))
        {
            item.RepoId = string.Empty;
            item.UpdateStatus = ModelUpdateStatus.NotLinked;
            item.HasMissingCompanions = false;
            item.HasStaleCompanions = false;
            item.HasVerifiedCompanionReplacement = false;
            item.CompanionStatus = string.Empty;
            item.ManualCompanionRepairLabel = string.Empty;
            return;
        }

        item.RepoId = entry.RepoId;
        item.UpdateStatus = entry.NoLongerPublished
            ? ModelUpdateStatus.NoLongerPublished
            : entry.HasPendingUpdate
                ? ModelUpdateStatus.UpdateAvailable
                : entry.LastCheckedAtUtc is not null
                    ? ModelUpdateStatus.UpToDate
                    : ModelUpdateStatus.NotLinked; // linked but never checked yet

        ApplyCompanionState(item, entry);
    }

    private static void ApplyCompanionState(ModelProfileItemViewModel item, ModelManifestEntry entry)
    {
        var missing = new List<string>();
        var stale = new List<string>();
        var repairable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manualRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var companion in entry.Companions.Where(c => !string.IsNullOrWhiteSpace(c.LocalFilePath)))
        {
            var localPath = companion.LocalFilePath;
            if (!File.Exists(localPath))
            {
                missing.Add(Path.GetFileName(localPath));
            }
            else if (companion.SizeBytes is > 0)
            {
                try
                {
                    if (new FileInfo(localPath).Length != companion.SizeBytes.Value)
                        stale.Add(Path.GetFileName(localPath));
                }
                catch (IOException)
                {
                    stale.Add(Path.GetFileName(localPath));
                }
                catch (UnauthorizedAccessException)
                {
                    stale.Add(Path.GetFileName(localPath));
                }
            }

            if (missing.Contains(Path.GetFileName(localPath), StringComparer.OrdinalIgnoreCase)
                || stale.Contains(Path.GetFileName(localPath), StringComparer.OrdinalIgnoreCase))
            {
                if (HasVerifiedCompanionMetadata(entry, companion))
                    repairable.Add(companion.LocalFilePath);
                else
                    manualRoles.Add(CompanionRoleLabel(companion.Role));
            }
        }

        item.HasMissingCompanions = missing.Count > 0;
        item.HasStaleCompanions = stale.Count > 0;
        item.HasVerifiedCompanionReplacement = repairable.Count > 0;
        item.ManualCompanionRepairLabel = manualRoles.Count == 0
            ? string.Empty
            : $"Open Services to browse or clear {string.Join(" and ", manualRoles)}";

        var repairDetails = missing
            .Select(name => $"{name} (missing)")
            .Concat(stale.Select(name => $"{name} (changed)"))
            .ToList();
        if (repairDetails.Count == 0)
        {
            item.CompanionStatus = entry.Companions.Count > 0 ? "Known companions present" : string.Empty;
        }
        else if (item.HasVerifiedCompanionReplacement)
        {
            item.CompanionStatus = $"Companion repair needed: {string.Join(", ", repairDetails)}. Verified compatible replacement available; choose Reacquire known companions."
                + (manualRoles.Count == 0 ? string.Empty : $" Remaining manual repair: browse or clear {string.Join(" and ", manualRoles)} in Services.");
        }
        else
        {
            item.CompanionStatus = $"Companion repair needed: {string.Join(", ", repairDetails)}. No verified replacement is available for {string.Join(" and ", manualRoles)}; browse or clear it in Services.";
        }
    }

    private static string CompanionRoleLabel(string role) => role.Trim().ToLowerInvariant() switch
    {
        "projector" => "projector",
        "draft_head" => "MTP draft head",
        _ => string.IsNullOrWhiteSpace(role) ? "companion" : role.Trim()
    };

    private static bool HasVerifiedCompanionMetadata(ModelManifestEntry primary, ModelCompanionManifestEntry companion) =>
        !companion.RequiresUserConfirmation
        && !string.IsNullOrWhiteSpace(primary.RepoId)
        && !string.IsNullOrWhiteSpace(companion.RepoFile)
        && IsSha256(companion.Sha256)
        && (!string.IsNullOrWhiteSpace(companion.RevisionSha) || !string.IsNullOrWhiteSpace(primary.RevisionSha));

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private void RefreshTuneSummary(ModelProfileItemViewModel item)
    {
        var profile = LlamaTuneProfileStore.Find(_settings.Settings, item.ModelId);
        item.SetTuneProfile(profile);
        item.TuneSummary = profile is null
            ? string.Empty
            : profile.TotalLayers is int total
                ? $"{profile.GpuLayers}/{total} GPU layers, {profile.Threads} threads, context {profile.ContextSize:N0}"
                : $"{profile.GpuLayers} GPU layers, {profile.Threads} threads, context {profile.ContextSize:N0}";
    }

    /// <summary>The first managed llama-server entry whose ExecutablePath actually resolves
    /// (r11 ExecutableResolver), used to probe local GGUFs that aren't attached to any server
    /// row yet. Null when nothing is configured, so callers can ask the user to set one up
    /// on the Services page (r13 02-model-library.md 2.3).</summary>
    private string? ResolveManagedExecutable()
    {
        foreach (var server in _settings.Settings.ManagedServers)
        {
            if (string.IsNullOrWhiteSpace(server.ExecutablePath))
                continue;
            var resolution = ExecutableResolver.Resolve(server.ExecutablePath, "llama-server");
            if (resolution.Success)
                return resolution.Path;
        }
        return null;
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private int ResolveProbeContextSize(ModelProfileItemViewModel item, LlamaTuneProfile? existing) =>
        existing?.ContextSize > 0
            ? existing.ContextSize
            : item.DefaultContextSize ?? (_settings.Settings.ManagedServers.FirstOrDefault()?.ContextSize ?? 4096);

    [RelayCommand]
    private async Task AutoTuneModelAsync(ModelProfileItemViewModel? item)
    {
        if (item is null || !item.IsLocalGguf)
            return;

        if (item.IsRunning)
        {
            _toasts.Show("Cannot auto-tune", $"{item.EffectiveName} is currently running. Stop it first.", ToastKind.Warning);
            return;
        }

        if (_isTuneInProgress)
        {
            _toasts.Show("Auto-tune busy", "Another auto-tune is already in progress.", ToastKind.Warning);
            return;
        }

        var executable = ResolveManagedExecutable();
        if (executable is null)
        {
            _toasts.Show("No managed executable", "Set up a managed llama-server executable on the Services page first.", ToastKind.Warning, 7000);
            return;
        }

        _isTuneInProgress = true;
        item.IsTuning = true;
        try
        {
            var existing = LlamaTuneProfileStore.Find(_settings.Settings, item.ModelId);
            var contextSize = ResolveProbeContextSize(item, existing);
            var probe = new ServerConfig
            {
                ExecutablePath = executable,
                ModelPath = item.ModelId,
                Port = GetFreePort(),
                ContextSize = contextSize,
                AutoStart = false
            };

            var result = await ServerProcessManager.AutoTuneAsync(probe);
            LlamaTuneProfileStore.Upsert(_settings.Settings, item.ModelId, contextSize, string.Empty, result.GpuLayers, result.Threads, result);
            await _settings.SaveAsync();
            RefreshTuneSummary(item);
            item.RetuneRecommended = false;
            _toasts.Show("Auto-tune complete", $"{item.EffectiveName}: {item.TuneSummary}.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            _toasts.Show("Auto-tune failed", $"{item.EffectiveName}: {ex.Message}", ToastKind.Warning, 7000);
        }
        finally
        {
            item.IsTuning = false;
            _isTuneInProgress = false;
        }
    }

    /// <summary>doc 04 4.1: registered next to the ViewModel that owns the action.</summary>
    public void RegisterCommands(ICommandRegistry registry)
    {
        registry.Register(new AppCommand(
            Id: "models.check-for-updates", Title: "Check for model updates", Area: "Models",
            Description: "Check installed models for available updates.",
            Keywords: ["models", "update", "check"], Shortcut: "",
            CanExecute: () => true,
            Execute: () => CheckForUpdatesCommand.ExecuteAsync(null)));

        registry.Register(new AppCommand(
            Id: "models.auto-tune-all", Title: "Auto-tune all models", Area: "Models",
            Description: "Run auto-tune against every installed model.",
            Keywords: ["models", "tune", "auto-tune"], Shortcut: "",
            CanExecute: () => !_isTuneInProgress,
            DisabledReason: () => "Auto-tune is already running.",
            Execute: () => AutoTuneAllCommand.ExecuteAsync(null)));
    }

    [RelayCommand]
    private async Task AutoTuneAllAsync()
    {
        if (_isTuneInProgress)
            return;

        var eligible = _allModels.Where(m => m.IsLocalGguf && !m.IsRunning).ToList();
        var candidates = eligible.Where(IsTuneStale).ToList();
        if (candidates.Count == 0)
        {
            _toasts.Show("Auto-tune all", "Every local model already has a fresh tune profile.", ToastKind.Info);
            return;
        }

        var executable = ResolveManagedExecutable();
        if (executable is null)
        {
            _toasts.Show("No managed executable", "Set up a managed llama-server executable on the Services page first.", ToastKind.Warning, 7000);
            return;
        }

        _autoTuneAllCts = new CancellationTokenSource();
        _isTuneInProgress = true;
        IsAutoTuningAll = true;
        var tuned = 0;
        var failed = 0;
        string? firstFailure = null;
        try
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (_autoTuneAllCts.IsCancellationRequested)
                    break;

                var item = candidates[i];
                AutoTuneAllStatus = $"Tuning {i + 1}/{candidates.Count}: {item.EffectiveName}";
                item.IsTuning = true;
                try
                {
                    var existing = LlamaTuneProfileStore.Find(_settings.Settings, item.ModelId);
                    var contextSize = ResolveProbeContextSize(item, existing);
                    var probe = new ServerConfig
                    {
                        ExecutablePath = executable,
                        ModelPath = item.ModelId,
                        Port = GetFreePort(),
                        ContextSize = contextSize,
                        AutoStart = false
                    };

                    var result = await ServerProcessManager.AutoTuneAsync(probe, ct: _autoTuneAllCts.Token);
                    LlamaTuneProfileStore.Upsert(_settings.Settings, item.ModelId, contextSize, string.Empty, result.GpuLayers, result.Threads, result);
                    await _settings.SaveAsync();
                    RefreshTuneSummary(item);
                    tuned++;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    failed++;
                    firstFailure ??= $"{item.EffectiveName}: {ex.Message}";
                }
                finally
                {
                    item.IsTuning = false;
                }
            }

            var skipped = eligible.Count - candidates.Count;
            AutoTuneAllStatus = $"Tuned {tuned}, skipped {skipped}, failed {failed}" + (firstFailure is not null ? $" (first failure: {firstFailure})" : "");
            _toasts.Show("Auto-tune all complete", AutoTuneAllStatus, failed > 0 ? ToastKind.Warning : ToastKind.Success, 7000);
        }
        finally
        {
            IsAutoTuningAll = false;
            _isTuneInProgress = false;
            _autoTuneAllCts.Dispose();
            _autoTuneAllCts = null;
        }
    }

    [RelayCommand]
    private void CancelAutoTuneAll() => _autoTuneAllCts?.Cancel();

    private bool IsTuneStale(ModelProfileItemViewModel item)
    {
        var file = new FileInfo(item.ModelId);
        if (!file.Exists)
            return false;

        var profile = LlamaTuneProfileStore.Find(_settings.Settings, item.ModelId);
        return LlamaTuneProfileStore.IsStale(profile, file.Length, file.LastWriteTimeUtc);
    }

    [RelayCommand]
    private async Task OrganizeModelsFolderAsync()
    {
        if (_services.Servers.Any(s => s.IsRunning))
        {
            _toasts.Show("Cannot organize folder", "Stop all managed servers first; Windows locks files that are in use.", ToastKind.Warning, 7000);
            return;
        }

        var assetsRoot = _settings.Settings.DataManagement.LocalAiAssetsRoot;
        var layout = LocalAiAssetLocator.Detect(assetsRoot);
        if (string.IsNullOrWhiteSpace(layout.ModelsDirectory))
        {
            _toasts.Show("Cannot organize folder", "No models folder was detected under the local AI assets root.", ToastKind.Warning);
            return;
        }

        var ggufPaths = LocalAiAssetLocator.FindGgufModels(assetsRoot);
        // r27 04-models-arrive-complete.md 4.4: files whose repository is known
        // from the manifest move into that repository's folder; the rest fall
        // back to their own base name, which is at least stable.
        var provenance = (await _manifest.LoadAsync())
            .Where(e => !string.IsNullOrWhiteSpace(e.FilePath) && !string.IsNullOrWhiteSpace(e.RepoId))
            .GroupBy(e => Path.GetFullPath(e.FilePath), ModelPathSafety.LocalPathComparer)
            .ToDictionary(g => g.Key, g => g.First().RepoId, ModelPathSafety.LocalPathComparer);
        var plan = ModelFolderOrganizer.Plan(layout.ModelsDirectory, ggufPaths, repoIdsByPath: provenance);
        if (plan.Moves.Count == 0)
        {
            _toasts.Show("Organize folder", "Every model is already in its own folder under the LLM folder.", ToastKind.Info);
            return;
        }

        if (RequestOrganizeConfirmation is null || !await RequestOrganizeConfirmation(plan))
            return;

        IsOrganizing = true;
        OrganizeStatus = "Moving files...";
        try
        {
            var result = await ModelFolderOrganizer.ExecuteAsync(plan, _settings, _manifest);
            OrganizeStatus = result.Failed.Count == 0
                ? $"Moved {result.Moved.Count} model(s)."
                : $"Moved {result.Moved.Count} model(s), {result.Failed.Count} failed (first: {result.Failed[0].Error}).";
            _toasts.Show("Organize folder complete", OrganizeStatus, result.Failed.Count > 0 ? ToastKind.Warning : ToastKind.Success, 7000);

            var emptyDirs = ModelFolderOrganizer.FindEmptyDirectories(layout.ModelsDirectory);
            if (emptyDirs.Count > 0 && RequestEmptyDirectoryCleanupConfirmation is not null && await RequestEmptyDirectoryCleanupConfirmation(emptyDirs.Count))
                ModelFolderOrganizer.RemoveEmptyDirectories(emptyDirs);

            ForceRefresh = true;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            OrganizeStatus = ex.Message;
            _toasts.Show("Organize folder failed", ex.Message, ToastKind.Warning, 7000);
        }
        finally
        {
            IsOrganizing = false;
        }
    }

    /// <summary>Prompts (via the view) for an "org/repo" string for a model that was copied in
    /// by hand rather than downloaded through Hermaeus, validates it resolves before saving
    /// (r13 03-hugging-face.md 3.1's manual writer).</summary>
    public Func<ModelProfileItemViewModel, Task<string?>>? RequestRepoIdInput { get; set; }

    [RelayCommand]
    private async Task LinkToHuggingFaceRepoAsync(ModelProfileItemViewModel? item)
    {
        if (item is null || !item.IsLocalGguf || RequestRepoIdInput is null)
            return;

        var repoId = await RequestRepoIdInput(item);
        if (string.IsNullOrWhiteSpace(repoId))
            return;

        repoId = repoId.Trim();
        var card = await _hf.GetModelCardAsync(repoId);
        if (card is null)
        {
            _toasts.Show("Link failed", $"Could not resolve '{repoId}' on Hugging Face. Check the org/repo spelling.", ToastKind.Warning, 7000);
            return;
        }

        var fullPath = Path.GetFullPath(item.ModelId);
        await _manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = fullPath,
            RepoId = repoId,
            RepoFile = Path.GetFileName(fullPath),
            RevisionSha = card.Sha,
            Sha256 = string.Empty,
            SizeBytes = new FileInfo(fullPath).Length,
            Source = "manual"
        });

        item.RepoId = repoId;
        item.UpdateStatus = ModelUpdateStatus.NotLinked; // linked but not checked yet; "Check for updates" picks it up
        _toasts.Show("Linked to Hugging Face", $"{item.EffectiveName} is now linked to {repoId}.", ToastKind.Success);
    }

    [ObservableProperty] private bool _isCheckingUpdates;
    [ObservableProperty] private string _updateCheckStatus = string.Empty;

    /// <summary>"Check for updates": batches every local GGUF with a manifest repo link by
    /// repo (one tree call per distinct repo, sequential, ~10s timeout each), hashing
    /// migration-sourced entries that have no stored hash yet along the way
    /// (r13 03-hugging-face.md 3.2). Never runs automatically - only from this command.</summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        var manifestEntries = await _manifest.LoadAsync();
        var candidates = new List<(ModelProfileItemViewModel Item, ModelManifestEntry Entry)>();
        foreach (var item in _allModels.Where(m => m.IsLocalGguf))
        {
            var normalized = Path.GetFullPath(item.ModelId);
            var entry = manifestEntries.FirstOrDefault(e => ModelPathSafety.AreSameLocalPath(e.FilePath, normalized) && !string.IsNullOrWhiteSpace(e.RepoId));
            if (entry is not null)
                candidates.Add((item, entry));
        }

        if (candidates.Count == 0)
        {
            _toasts.Show("Check for updates", "No models are linked to a Hugging Face repo yet. Use \"Link to Hugging Face repo...\" on a card first.", ToastKind.Info);
            return;
        }

        IsCheckingUpdates = true;
        try
        {
            var groups = candidates.GroupBy(c => c.Entry.RepoId, StringComparer.OrdinalIgnoreCase).ToList();
            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                UpdateCheckStatus = $"Checking {i + 1}/{groups.Count}: {group.Key}";
                foreach (var (item, _) in group)
                    item.IsCheckingUpdate = true;

                foreach (var (item, entry) in group)
                {
                    if (string.IsNullOrWhiteSpace(entry.Sha256) && File.Exists(item.ModelId))
                    {
                        UpdateCheckStatus = $"Hashing {item.EffectiveName} (first check after linking)...";
                        entry.Sha256 = await ComputeSha256Async(item.ModelId);
                        // Hashing a multi-GB model is expensive. Persist it before the
                        // network call so a timeout, cancellation, or failed repo lookup
                        // cannot make the next check hash the exact same bytes again.
                        await _manifest.UpsertAsync(entry);
                    }
                }

                IReadOnlyList<HfTreeEntry>? tree;
                var revision = "main";
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var card = await _hf.GetModelCardAsync(group.Key, cts.Token);
                    revision = !string.IsNullOrWhiteSpace(card?.Sha) ? card.Sha : "main";
                    tree = await _hf.GetTreeAsync(group.Key, revision, cts.Token);
                }
                catch
                {
                    tree = null;
                }

                foreach (var (item, entry) in group)
                {
                    var result = ModelUpdateChecker.Evaluate(entry, tree);
                    item.UpdateStatus = result.Status;
                    entry.LastCheckedAtUtc = DateTime.UtcNow;
                    entry.NoLongerPublished = result.Status == ModelUpdateStatus.NoLongerPublished;
                    if (result.Status == ModelUpdateStatus.UpdateAvailable && result.MatchedEntry is not null)
                    {
                        entry.PendingSha256 = result.MatchedEntry.LfsSha256;
                        entry.PendingSizeBytes = result.MatchedEntry.SizeBytes;
                        entry.PendingRevisionSha = revision;
                    }
                    else
                    {
                        entry.PendingSha256 = null;
                        entry.PendingRevisionSha = null;
                        entry.PendingSizeBytes = null;
                    }
                    ApplyCompanionState(item, entry);
                    await _manifest.UpsertAsync(entry);
                    item.IsCheckingUpdate = false;
                }
            }

            var upToDate = candidates.Count(c => c.Item.UpdateStatus == ModelUpdateStatus.UpToDate);
            var available = candidates.Count(c => c.Item.UpdateStatus == ModelUpdateStatus.UpdateAvailable);
            var gone = candidates.Count(c => c.Item.UpdateStatus == ModelUpdateStatus.NoLongerPublished);
            var failed = candidates.Count(c => c.Item.UpdateStatus == ModelUpdateStatus.CheckFailed);
            var repairableCompanions = candidates.Count(c => c.Item.CanReacquireCompanions);
            var manualCompanions = candidates.Count(c => c.Item.RequiresManualCompanionRepair);
            UpdateCheckStatus = $"{upToDate} up to date, {available} update(s) available, {gone} no longer published on the repo, {failed} check(s) failed."
                + (repairableCompanions == 0 ? string.Empty : $" {repairableCompanions} model(s) have a verified companion repair available: open Configure and choose Reacquire known companions.")
                + (manualCompanions == 0 ? string.Empty : $" {manualCompanions} model(s) need manual companion repair: no verified replacement is available, so browse or clear the affected companion in Services.");
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private bool IsModelInUseByRunningServer(string modelPath)
    {
        var normalized = Path.GetFullPath(modelPath);
        return _services.Servers.Any(s => s.IsRunning && ModelPathSafety.AreSameLocalPath(s.ModelPath, normalized));
    }

    private string ModelsDirectory()
    {
        var layout = LocalAiAssetLocator.Detect(_settings.Settings.DataManagement.LocalAiAssetsRoot);
        return string.IsNullOrWhiteSpace(layout.ModelsDirectory)
            ? Path.Combine(_settings.Settings.DataManagement.LocalAiAssetsRoot, "Models")
            : layout.ModelsDirectory;
    }

    private static string CompanionRole(ModelFileRole role) => role switch
    {
        ModelFileRole.Projector => "projector",
        ModelFileRole.DraftHead => "draft_head",
        _ => string.Empty
    };

    private static List<ModelCompanionManifestEntry> BuildCompanionManifest(
        ModelFileSet fileSet,
        string modelsDirectory,
        string repoId,
        string revision,
        IReadOnlyList<ModelCompanionManifestEntry>? previous = null)
    {
        return fileSet.Entries
            .Where(e => e.Role is ModelFileRole.Projector or ModelFileRole.DraftHead)
            .Select(e => new ModelCompanionManifestEntry
            {
                LocalFilePath = HuggingFaceBrowserSupport.PlanDestination(modelsDirectory, e.RepoPath, repoId).DestinationPath,
                RepoFile = e.RepoPath,
                RevisionSha = revision,
                Role = CompanionRole(e.Role),
                Sha256 = e.LfsSha256 ?? string.Empty,
                SizeBytes = e.SizeBytes,
                RequiresUserConfirmation = !e.SelectedByDefault
                    && (previous?.FirstOrDefault(old => string.Equals(old.RepoFile, e.RepoPath, StringComparison.OrdinalIgnoreCase))
                        ?.RequiresUserConfirmation ?? true)
            })
            .ToList();
    }

    private async Task<(bool Success, string Message)> DownloadOrUpdateCompanionAsync(
        ModelFileSetEntry source,
        string destination,
        string repoId,
        string revision,
        bool replaceExisting,
        CancellationToken ct = default)
    {
        if (!ModelPathSafety.TryResolveFileUnderRoot(_settings.Settings.DataManagement.LocalAiAssetsRoot, destination, out var safeDestination, out var error))
            return (false, error);

        var exists = File.Exists(safeDestination);
        if (exists && !replaceExisting)
            return (false, $"{Path.GetFileName(safeDestination)} exists but its provenance is not known; it was left unchanged.");

        var downloadPath = exists ? safeDestination + ".update.tmp" : safeDestination;
        var installed = false;
        try
        {
            var download = await _downloader.DownloadAsync(
                HuggingFaceClient.ResolveDownloadUrl(repoId, source.RepoPath, revision), downloadPath, ct: ct);
            if (!download.Success)
                return (false, download.Message);

            if (!await _downloader.VerifyHashAsync(downloadPath, source.LfsSha256, ct: ct))
                return (false, "The companion hash did not match the repository metadata; the replacement was discarded.");

            if (exists)
            {
                var swap = ModelUpdateApplier.Swap(safeDestination, downloadPath);
                installed = swap.Success;
                return swap.Success
                    ? (true, string.Empty)
                    : (false, swap.Error ?? "The companion replacement could not be applied.");
            }

            installed = true;
            return (true, string.Empty);
        }
        finally
        {
            if (!installed)
            {
                try { File.Delete(downloadPath); } catch { }
                try { File.Delete(downloadPath + ".tmp"); } catch { }
                if (!exists)
                {
                    try { File.Delete(safeDestination); } catch { }
                }
            }
        }
    }

    private async Task<(int Updated, int Missing, string? Message)> SyncKnownCompanionsAsync(
        ModelManifestEntry primary, bool allowReplaceExisting, bool onlyMissing, CancellationToken ct = default)
    {
        var card = await _hf.GetModelCardAsync(primary.RepoId, ct);
        var revision = !string.IsNullOrWhiteSpace(card?.Sha) ? card.Sha
            : !string.IsNullOrWhiteSpace(primary.RevisionSha) ? primary.RevisionSha
            : "main";
        var tree = await _hf.GetTreeAsync(primary.RepoId, revision, ct);
        if (tree is null)
            return (0, 0, "The linked repository could not be read, so known companions were not changed.");

        var modelMetadata = await Task.Run(() => GgufMetadataReader.TryRead(primary.FilePath), ct);
        var declarations = await _hf.ResolveCompanionDeclarationsAsync(
            primary.RepoId, tree, primary.RepoFile, modelMetadata, revision, ct);
        var fileSet = ModelFileSetResolver.Resolve(primary.RepoId, tree, primary.RepoFile, declarations);
        var known = BuildCompanionManifest(fileSet, ModelsDirectory(), primary.RepoId, revision, primary.Companions);
        if (known.Count == 0)
            return (0, 0, "No hash-verified compatible companions were found in the linked repository.");

        primary.Companions = known;
        primary.RevisionSha = revision;
        await _manifest.UpsertAsync(primary, ct);

        var updated = 0;
        var missing = 0;
        var messages = new List<string>();
        foreach (var companion in known)
        {
            var exists = File.Exists(companion.LocalFilePath);
            if (onlyMissing && exists)
                continue;
            if (companion.RequiresUserConfirmation)
            {
                missing++;
                messages.Add($"{Path.GetFileName(companion.LocalFilePath)} has no verified compatibility evidence; browse or clear the {CompanionRoleLabel(companion.Role)} in Services.");
                continue;
            }

            var current = await _manifest.FindAsync(companion.LocalFilePath, ct);
            var alreadyCurrent = exists && current is not null
                && string.Equals(current.RepoId, primary.RepoId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(current.RepoFile, companion.RepoFile, StringComparison.OrdinalIgnoreCase)
                && string.Equals(current.Sha256, companion.Sha256, StringComparison.OrdinalIgnoreCase);
            if (alreadyCurrent)
                continue;

            var replace = exists && allowReplaceExisting && current is not null
                && string.Equals(current.RepoId, primary.RepoId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(current.RepoFile, companion.RepoFile, StringComparison.OrdinalIgnoreCase);
            var source = fileSet.Entries.First(e => string.Equals(e.RepoPath, companion.RepoFile, StringComparison.OrdinalIgnoreCase));
            var result = await DownloadOrUpdateCompanionAsync(source, companion.LocalFilePath, primary.RepoId, revision, replace, ct);
            if (!result.Success)
            {
                missing++;
                messages.Add($"{Path.GetFileName(companion.LocalFilePath)}: {result.Message}");
                continue;
            }

            await _manifest.UpsertAsync(new ModelManifestEntry
            {
                FilePath = companion.LocalFilePath,
                RepoId = primary.RepoId,
                RepoFile = companion.RepoFile,
                RevisionSha = revision,
                Sha256 = companion.Sha256,
                SizeBytes = companion.SizeBytes ?? new FileInfo(companion.LocalFilePath).Length,
                Source = "hf-browser",
                ParentModelPath = primary.FilePath,
                CompanionRole = companion.Role
            }, ct);
            updated++;
        }

        return (updated, missing, messages.Count == 0 ? null : string.Join("; ", messages));
    }

    [RelayCommand]
    private async Task ReacquireCompanionsAsync(ModelProfileItemViewModel? item)
    {
        if (item is null || !item.HasCompanionRepair)
            return;

        if (!item.CanReacquireCompanions)
        {
            var action = string.IsNullOrWhiteSpace(item.ManualCompanionRepairLabel)
                ? "Review the companion paths in Services."
                : item.ManualCompanionRepairLabel + ".";
            _toasts.Show("Cannot reacquire companions", "No verified compatible replacement is available. " + action, ToastKind.Warning, 8000);
            return;
        }

        var primary = await _manifest.FindAsync(item.ModelId);
        if (primary is null || string.IsNullOrWhiteSpace(primary.RepoId) || string.IsNullOrWhiteSpace(primary.RepoFile))
        {
            _toasts.Show("Cannot reacquire companions", "The model's trusted repository mapping is unavailable. Browse for a model or clear the stale path.", ToastKind.Warning, 7000);
            return;
        }

        var result = await SyncKnownCompanionsAsync(primary, allowReplaceExisting: false, onlyMissing: true);
        ForceRefresh = true;
        await RefreshAsync();
        _toasts.Show(result.Missing == 0 && result.Updated > 0 ? "Companions reacquired" : "Companion recovery incomplete",
            result.Message ?? $"{result.Updated} known companion asset(s) restored.",
            result.Missing == 0 ? ToastKind.Success : ToastKind.Warning, 8000);
    }

    /// <summary>Per-card "Update": downloads to &lt;file&gt;.update.tmp, verifies against the
    /// tree's lfs.oid captured at check time, then atomically swaps
    /// (r13 03-hugging-face.md 3.3). Refuses while the model is running or while any managed
    /// server currently running points its ModelPath at this file.</summary>
    [RelayCommand]
    private async Task UpdateModelAsync(ModelProfileItemViewModel? item)
    {
        if (item is null || item.UpdateStatus != ModelUpdateStatus.UpdateAvailable)
            return;

        if (item.IsRunning || IsModelInUseByRunningServer(item.ModelId))
        {
            _toasts.Show("Cannot update", $"{item.EffectiveName} is currently running. Stop it first.", ToastKind.Warning);
            return;
        }

        var entry = await _manifest.FindAsync(item.ModelId);
        if (entry is null || string.IsNullOrWhiteSpace(entry.PendingSha256))
        {
            _toasts.Show("Cannot update", "No pending update recorded; run \"Check for updates\" again.", ToastKind.Warning);
            return;
        }

        item.IsUpdating = true;
        var tmpPath = item.ModelId + ".update.tmp";
        try
        {
            var revision = !string.IsNullOrWhiteSpace(entry.PendingRevisionSha) ? entry.PendingRevisionSha
                : !string.IsNullOrWhiteSpace(entry.RevisionSha) ? entry.RevisionSha
                : "main";
            var url = HuggingFaceClient.ResolveDownloadUrl(entry.RepoId, entry.RepoFile, revision);
            var progress = new Progress<DownloadProgress>(p => UpdateCheckStatus = $"Downloading {item.EffectiveName}: {p.PercentComplete:0}%");
            var download = await _downloader.DownloadAsync(url, tmpPath, progress);
            if (!download.Success)
            {
                // r28 doc 03 3.3: an update is a download that also replaces a
                // file on disk, so "did that actually work" applies twice over.
                _activity.RecordSafe("models.update", entry.RepoId, ActivityOutcome.Failed,
                    $"{item.EffectiveName} update failed", download.Message);
                _toasts.Show("Update failed", download.Message, ToastKind.Warning, 7000);
                return;
            }

            var verified = await _downloader.VerifyHashAsync(tmpPath, entry.PendingSha256);
            if (!verified)
            {
                const string mismatch = "Downloaded file hash did not match the repo's recorded hash; nothing was changed.";
                _activity.RecordSafe("models.update", entry.RepoId, ActivityOutcome.Failed,
                    $"{item.EffectiveName} update failed", mismatch);
                _toasts.Show("Update failed", mismatch, ToastKind.Warning, 7000);
                return;
            }

            var swap = ModelUpdateApplier.Swap(item.ModelId, tmpPath);
            if (!swap.Success)
            {
                _activity.RecordSafe("models.update", entry.RepoId, ActivityOutcome.Failed,
                    $"{item.EffectiveName} update failed", swap.Error ?? "Unknown error during swap.");
                _toasts.Show("Update failed", swap.Error ?? "Unknown error during swap.", ToastKind.Warning, 7000);
                return;
            }

            var file = new FileInfo(item.ModelId);
            entry.Sha256 = entry.PendingSha256!;
            entry.RevisionSha = revision;
            entry.SizeBytes = entry.PendingSizeBytes ?? file.Length;
            entry.PendingSha256 = null;
            entry.PendingRevisionSha = null;
            entry.PendingSizeBytes = null;
            entry.RecordedAtUtc = DateTime.UtcNow;
            await _manifest.UpsertAsync(entry);

            (int Updated, int Missing, string? Message)? companionResult = null;
            if (item.AutoManageCompanionAssets)
                companionResult = await SyncKnownCompanionsAsync(entry, allowReplaceExisting: true, onlyMissing: false);

            item.UpdateStatus = ModelUpdateStatus.UpToDate;
            item.RetuneRecommended = true;
            _activity.RecordSafe("models.update", entry.RepoId, ActivityOutcome.Succeeded,
                $"Updated {item.EffectiveName}",
                companionResult is { } c
                    ? $"Re-tune recommended; {c.Updated} companion asset(s) updated or restored."
                    : "Re-tune recommended; automatic companion handling is disabled.");
            var companionText = companionResult is { } result && result.Message is not null
                ? $" Companion status: {result.Message}"
                : string.Empty;
            _toasts.Show(
                companionResult is { Missing: > 0 } ? "Model updated, companions incomplete" : "Model updated",
                $"{item.EffectiveName} was updated. Re-tune recommended (the file changed size/mtime).{companionText}",
                companionResult is { Missing: > 0 } ? ToastKind.Warning : ToastKind.Success, 7000);
        }
        catch (Exception ex)
        {
            _activity.RecordSafe("models.update", entry.RepoId, ActivityOutcome.Failed,
                $"{item.EffectiveName} update failed", ex.Message);
            _toasts.Show("Update failed", ex.Message, ToastKind.Warning, 7000);
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); } catch { }
            }
            item.IsUpdating = false;
        }
    }

    // ── r13 03-hugging-face.md 3.4: "Get models" browser ──────────────────────────────────
    [ObservableProperty] private bool _isHfBrowserExpanded;
    [ObservableProperty] private string _hfSearchQuery = string.Empty;
    [ObservableProperty] private bool _isSearchingHf;
    [ObservableProperty] private bool _isLoadingHfFiles;
    [ObservableProperty] private string _hfBrowserStatus = string.Empty;
    [ObservableProperty] private HfRepoResultViewModel? _selectedHfRepo;

    public UiBoundCollection<HfRepoResultViewModel> HfSearchResults { get; } = [];
    public UiBoundCollection<HfFileResultViewModel> HfFiles { get; } = [];

    [RelayCommand]
    private async Task SearchHuggingFaceAsync()
    {
        if (string.IsNullOrWhiteSpace(HfSearchQuery))
            return;

        CancelHfSelection();
        IsSearchingHf = true;
        HfBrowserStatus = string.Empty;
        HfFiles.Clear();
        SelectedHfRepo = null;
        try
        {
            var results = await _hf.SearchAsync(HfSearchQuery.Trim());
            HfSearchResults.Clear();
            foreach (var r in results)
                HfSearchResults.Add(new HfRepoResultViewModel(r.RepoId, r.Downloads));
            HfBrowserStatus = results.Count == 0 ? "No GGUF repos matched that search." : $"{results.Count} repo(s) found.";
        }
        catch (Exception ex)
        {
            HfBrowserStatus = ex.Message;
        }
        finally
        {
            IsSearchingHf = false;
        }
    }

    [RelayCommand]
    private async Task SelectHfRepoAsync(HfRepoResultViewModel? repo)
    {
        if (repo is null)
            return;

        var selectionCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _hfSelectionCts, selectionCts);
        try { previousCts?.Cancel(); }
        catch (ObjectDisposedException) { }
        previousCts?.Dispose();
        try
        {
            await SelectHfRepoCoreAsync(repo, selectionCts.Token);
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _hfSelectionCts, null, selectionCts), selectionCts))
                selectionCts.Dispose();
        }
    }

    private void CancelHfSelection()
    {
        try { _hfSelectionCts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task SelectHfRepoCoreAsync(HfRepoResultViewModel repo, CancellationToken ct)
    {
        SelectedHfRepo = repo;
        HfFiles.Clear();
        IsLoadingHfFiles = true;
        HfBrowserStatus = $"Checking {repo.RepoId} and calculating fit...";
        try
        {
            ct.ThrowIfCancellationRequested();
            var card = await _hf.GetModelCardAsync(repo.RepoId, ct);
            ct.ThrowIfCancellationRequested();
            repo.RevisionSha = card?.Sha ?? string.Empty;
            var revision = string.IsNullOrWhiteSpace(repo.RevisionSha) ? "main" : repo.RevisionSha;
            var tree = await _hf.GetTreeAsync(repo.RepoId, revision, ct);
            ct.ThrowIfCancellationRequested();
            repo.License = card?.License ?? "unknown";

            if (tree is null)
            {
                HfBrowserStatus = $"Could not load the file list for {repo.RepoId}.";
                return;
            }

            var hardware = await _system.GetHardwareProfileAsync();
            ct.ThrowIfCancellationRequested();
            var layout = LocalAiAssetLocator.Detect(_settings.Settings.DataManagement.LocalAiAssetsRoot);
            var modelsDir = string.IsNullOrWhiteSpace(layout.ModelsDirectory)
                ? Path.Combine(_settings.Settings.DataManagement.LocalAiAssetsRoot, "Models")
                : layout.ModelsDirectory;
            var ggufEntries = tree.Where(e => e.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).ToList();

            // r27 04 4.1: a sharded model is listed once, as its first shard,
            // and downloads as a set. It used to be hidden outright, because a
            // single shard is a model that will not load.
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shardedSets = 0;
            var manifestEntries = await _manifest.LoadAsync();
            ct.ThrowIfCancellationRequested();
            var modelEntries = ggufEntries
                .Where(entry =>
                {
                    var name = System.IO.Path.GetFileName(entry.Path);
                    return !name.StartsWith("mmproj-", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            // Show every known model variant immediately. Metadata and
            // companion checks are independent network work, so one slow or
            // unavailable variant must not hold the rest of the repo hostage.
            var rows = new Dictionary<string, HfFileResultViewModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in modelEntries)
            {
                var row = new HfFileResultViewModel(
                    repo.RepoId,
                    entry.Path,
                    entry.SizeBytes,
                    entry.LfsSha256,
                    ModelFitTier.Unknown,
                    "Compatibility check pending.",
                    revisionSha: revision)
                {
                    IsChecking = true
                };
                rows[entry.Path] = row;
                HfFiles.Add(row);
            }

            var enrichments = modelEntries.Select(async entry =>
            {
                try
                {
                    var modelMetadata = await _hf.GetGgufMetadataAsync(repo.RepoId, entry.Path, entry.LfsSha256, revision, ct);
                    ct.ThrowIfCancellationRequested();
                    var companionDeclarations = await _hf.ResolveCompanionDeclarationsAsync(
                        repo.RepoId, tree, entry.Path, modelMetadata, revision, ct);
                    ct.ThrowIfCancellationRequested();
                    var fileSet = ModelFileSetResolver.Resolve(repo.RepoId, tree, entry.Path, companionDeclarations);
                    var setBytes = fileSet.Entries
                        .Where(e => e.Role is ModelFileRole.Model or ModelFileRole.Shard)
                        .Sum(e => e.SizeBytes ?? 0);
                    var fit = ModelFitEstimator.Estimate(setBytes, hardware);
                    return (entry.Path, FileSet: (ModelFileSet?)fileSet, Fit: (ModelFitResult?)fit, Error: (string?)null);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return (entry.Path, FileSet: (ModelFileSet?)null, Fit: (ModelFitResult?)null, Error: ex.Message);
                }
            }).ToList();

            while (enrichments.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var completed = await Task.WhenAny(enrichments);
                enrichments.Remove(completed);
                var result = await completed;
                var row = rows[result.Path];
                if (result.FileSet is { } fileSet && result.Fit is { } fit)
                    row.ApplyResolvedDetails(fileSet, fit.Tier, fit.Reason, revision, modelsDir, manifestEntries);
                else
                    row.MarkCheckUnavailable(result.Error);
            }

            foreach (var entry in modelEntries)
            {
                ct.ThrowIfCancellationRequested();
                var row = rows[entry.Path];
                var modelMembers = row.FileSet.Entries
                    .Where(e => e.Role is ModelFileRole.Model or ModelFileRole.Shard)
                    .Select(e => e.RepoPath)
                    .ToList();
                if (modelMembers.Any(listed.Contains))
                {
                    HfFiles.Remove(row);
                    continue;
                }

                foreach (var member in modelMembers)
                    listed.Add(member);
                if (row.IsSharded)
                    shardedSets++;
            }

            HfBrowserStatus = shardedSets > 0
                ? $"{HfFiles.Count} GGUF model(s) found, {shardedSets} of them sharded; each downloads as a complete set."
                : $"{HfFiles.Count} GGUF file(s) found.";
        }
        finally
        {
            IsLoadingHfFiles = false;
        }
    }

    [RelayCommand]
    private async Task DownloadHfFileAsync(HfFileResultViewModel? file)
    {
        if (file is null)
            return;

        var layout = LocalAiAssetLocator.Detect(_settings.Settings.DataManagement.LocalAiAssetsRoot);
        var modelsDir = string.IsNullOrWhiteSpace(layout.ModelsDirectory)
            ? Path.Combine(_settings.Settings.DataManagement.LocalAiAssetsRoot, "Models")
            : layout.ModelsDirectory;

        // The whole explicitly mapped file set, not only the file that was
        // clicked. Shards are required; trusted companion mappings are optional.
        var entries = file.SelectedEntries();
        var planned = new List<(ModelFileSetEntry Entry, string Destination)>();
        foreach (var entry in entries)
        {
            var (destination, collides) = HuggingFaceBrowserSupport.PlanDestination(modelsDir, entry.RepoPath, file.RepoId);
            if (collides)
            {
                _toasts.Show("Cannot download",
                    $"{entry.FileName} already exists at the destination. Nothing was overwritten.", ToastKind.Warning, 7000);
                return;
            }

            planned.Add((entry, destination));
        }

        if (planned.Count == 0)
            return;

        file.IsDownloading = true;
        file.DownloadState = HfDownloadState.Downloading;
        // r28 doc 03 3.3: a download is one of the four sources r24 named and
        // never wired. r27 made it a file set, so a partial set is Partial and
        // the reason names what is missing.
        _activity.RecordSafe("models.download", file.RepoId, ActivityOutcome.Running,
            $"Downloading {file.FileName}", planned.Count == 1 ? string.Empty : $"{planned.Count} files");
        try
        {
            // Progress is reported across the set, so a three-shard download
            // does not appear to finish three times.
            var totalBytes = planned.Sum(p => p.Entry.SizeBytes ?? 0);
            long completedBytes = 0;
            var missing = new List<string>();
            var savedCount = 0;
            var primaryDestination = planned.First(p => p.Entry.Role == ModelFileRole.Model).Destination;
            var downloadRevision = string.IsNullOrWhiteSpace(file.RevisionSha) ? "main" : file.RevisionSha;

            foreach (var (entry, destination) in planned)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var entryBytes = entry.SizeBytes ?? 0;
                var carried = completedBytes;
                var url = HuggingFaceClient.ResolveDownloadUrl(file.RepoId, entry.RepoPath, downloadRevision);
                var progress = new Progress<DownloadProgress>(p =>
                    file.DownloadPercent = totalBytes > 0
                        ? Math.Clamp((carried + (entryBytes * p.PercentComplete / 100d)) / totalBytes * 100d, 0, 100)
                        : p.PercentComplete);

                var download = await _downloader.DownloadAsync(url, destination, progress);
                if (!download.Success)
                {
                    // A partial failure leaves what succeeded on disk: a 4 GB
                    // shard that downloaded correctly should not be deleted
                    // because a 60 MB companion failed.
                    missing.Add($"{entry.FileName} ({download.Message})");
                    completedBytes += entryBytes;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(entry.LfsSha256))
                {
                    var verified = await _downloader.VerifyHashAsync(destination, entry.LfsSha256);
                    if (!verified)
                    {
                        // Deletes only the file that failed, never the set.
                        try { File.Delete(destination); } catch { }
                        missing.Add($"{entry.FileName} (hash verification failed; the file was removed)");
                        completedBytes += entryBytes;
                        continue;
                    }
                }

                await _manifest.UpsertAsync(new ModelManifestEntry
                {
                    FilePath = destination,
                    RepoId = file.RepoId,
                    RepoFile = entry.RepoPath,
                    RevisionSha = downloadRevision,
                    Sha256 = entry.LfsSha256 ?? string.Empty,
                    SizeBytes = new FileInfo(destination).Length,
                    Source = "hf-browser",
                    ParentModelPath = entry.Role is ModelFileRole.Projector or ModelFileRole.DraftHead ? primaryDestination : string.Empty,
                    CompanionRole = entry.Role switch
                    {
                        ModelFileRole.Projector => "projector",
                        ModelFileRole.DraftHead => "draft_head",
                        _ => string.Empty
                    }
                });

                savedCount++;
                completedBytes += entryBytes;
            }

            var primaryEntry = await _manifest.FindAsync(primaryDestination);
            if (primaryEntry is not null)
            {
                primaryEntry.Companions = file.FileSet.Entries
                    .Where(e => e.Role is ModelFileRole.Projector or ModelFileRole.DraftHead)
                    .Select(e => new ModelCompanionManifestEntry
                    {
                        LocalFilePath = HuggingFaceBrowserSupport.PlanDestination(modelsDir, e.RepoPath, file.RepoId).DestinationPath,
                        RepoFile = e.RepoPath,
                        RevisionSha = downloadRevision,
                        Role = e.Role == ModelFileRole.Projector ? "projector" : "draft_head",
                        Sha256 = e.LfsSha256 ?? string.Empty,
                        SizeBytes = e.SizeBytes,
                        RequiresUserConfirmation = !file.SelectedEntries().Any(selected =>
                            string.Equals(selected.RepoPath, e.RepoPath, StringComparison.OrdinalIgnoreCase))
                    })
                    .ToList();
                await _manifest.UpsertAsync(primaryEntry);

                var profile = _profiles.GetOrCreate(primaryDestination, "local GGUF");
                profile.AutoManageCompanionAssets = file.FileSet.Optional.Count == 0
                    || file.SelectedEntries().Count(e => e.Role is ModelFileRole.Projector or ModelFileRole.DraftHead)
                       == file.FileSet.Optional.Count;
                await _profiles.SaveAsync(profile);
            }

            if (missing.Count > 0)
            {
                _toasts.Show(savedCount > 0 ? "Download incomplete" : "Download failed",
                    $"{savedCount} of {planned.Count} file(s) saved. Missing: {string.Join("; ", missing)}",
                    ToastKind.Warning, 9000);
                _activity.RecordSafe("models.download", file.RepoId,
                    savedCount > 0 ? ActivityOutcome.Partial : ActivityOutcome.Failed,
                    $"{file.FileName} download {(savedCount > 0 ? "incomplete" : "failed")}",
                    $"{savedCount} of {planned.Count} file(s) saved. Missing: {string.Join("; ", missing)}");
            }
            else
            {
                var folder = Path.GetDirectoryName(planned[0].Destination);
                _activity.RecordSafe("models.download", file.RepoId, ActivityOutcome.Succeeded,
                    $"Downloaded {file.FileName}",
                    planned.Count == 1 ? string.Empty : $"{planned.Count} files");
                _toasts.Show("Download complete",
                    planned.Count == 1
                        ? $"{file.FileName} saved to {planned[0].Destination}."
                        : $"{planned.Count} files saved to {folder}.",
                    ToastKind.Success);
            }

            ForceRefresh = true;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _activity.RecordSafe("models.download", file.RepoId, ActivityOutcome.Failed,
                $"{file.FileName} download failed", ex.Message);
            _toasts.Show("Download failed", ex.Message, ToastKind.Warning, 7000);
        }
        finally
        {
            file.IsDownloading = false;
            file.UpdateDownloadState(modelsDir, await _manifest.LoadAsync());
        }
    }
}

public sealed partial class HfRepoResultViewModel : ObservableObject
{
    public string RepoId { get; }
    public long Downloads { get; }
    [ObservableProperty] private string _revisionSha = string.Empty;
    [ObservableProperty] private string _license = string.Empty;

    public HfRepoResultViewModel(string repoId, long downloads)
    {
        RepoId = repoId;
        Downloads = downloads;
    }
}

public enum HfDownloadState { NotDownloaded, Partial, Downloaded, Downloading }

public sealed partial class HfFileResultViewModel : ObservableObject
{
    public string RepoId { get; }
    public string Path { get; }
    public string RevisionSha { get; private set; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public long? SizeBytes { get; }
    public string? LfsSha256 { get; }
    public string SizeDisplay => SizeBytes is { } s ? SystemInfoService.FormatBytes(s) : string.Empty;
    [ObservableProperty] private ModelFitTier _fitTier;
    [ObservableProperty] private string _fitReason;
    public string FitLabel => ModelFitEstimator.Label(FitTier);

    [ObservableProperty] private bool _isChecking;
    public string CheckingLabel => IsChecking ? "Checking compatibility…" : string.Empty;
    public bool CanDownload => !IsChecking && DownloadState == HfDownloadState.NotDownloaded;

    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadPercent;
    [ObservableProperty] private HfDownloadState _downloadState = HfDownloadState.NotDownloaded;
    public string DownloadStateLabel => DownloadState switch
    {
        HfDownloadState.Downloaded => "On disk",
        HfDownloadState.Partial => "Complete set",
        HfDownloadState.Downloading => $"Downloading {DownloadPercent:F0}%",
        _ => "Download"
    };
    partial void OnDownloadStateChanged(HfDownloadState value)
    {
        OnPropertyChanged(nameof(DownloadStateLabel));
        OnPropertyChanged(nameof(CanDownload));
    }
    partial void OnDownloadPercentChanged(double value) => OnPropertyChanged(nameof(DownloadStateLabel));
    partial void OnIsCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(CheckingLabel));
        OnPropertyChanged(nameof(CanDownload));
    }

    // ── r27 04-models-arrive-complete.md 4.1: a model is a file set ─────────

    /// <summary>Every file this download will fetch, resolved from the repository tree.</summary>
    public ModelFileSet FileSet { get; private set; }

    /// <summary>Shards are not a checkbox: a partial shard set is a model that does not load.</summary>
    public bool IsSharded => FileSet.IsSharded;

    public bool HasProjector => FileSet.Entries.Any(e => e.Role == ModelFileRole.Projector);
    public bool HasDraftHead => FileSet.Entries.Any(e => e.Role == ModelFileRole.DraftHead);
    public bool HasCompanions => HasProjector || HasDraftHead;

    public string CompanionSizeDisplay
    {
        get
        {
            var bytes = FileSet.Optional.Sum(e => e.SizeBytes ?? 0);
            return bytes > 0 ? $"{SystemInfoService.FormatBytes(bytes)} additional" : "size unknown";
        }
    }

    public bool HasReviewRequiredCompanions => FileSet.Entries.Any(e =>
        (e.Role is ModelFileRole.Projector or ModelFileRole.DraftHead) && !e.SelectedByDefault);
    public string ProjectorSelectionLabel => $"Vision projector ({CompanionSize(ModelFileRole.Projector)})";
    public string DraftHeadSelectionLabel => $"MTP draft head ({CompanionSize(ModelFileRole.DraftHead)})";
    public string CompanionReviewLabel
    {
        get
        {
            if (!HasReviewRequiredCompanions)
                return string.Empty;

            var names = FileSet.Entries
                .Where(e => (e.Role is ModelFileRole.Projector or ModelFileRole.DraftHead) && !e.SelectedByDefault)
                .Select(e => e.FileName)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return $"Review required before selecting: {string.Join(", ", names)}";
        }
    }

    private string CompanionSize(ModelFileRole role)
    {
        var bytes = FileSet.Entries.Where(e => e.Role == role).Sum(e => e.SizeBytes ?? 0);
        return bytes > 0 ? SystemInfoService.FormatBytes(bytes) : "size unknown";
    }

    /// <summary>Offered, on by default. A multimodal model without its projector quietly cannot see.</summary>
    [ObservableProperty] private bool _includeProjector = true;

    /// <summary>Offered, on by default. This is the file doc 03's speculative decoding needs.</summary>
    [ObservableProperty] private bool _includeDraftHead = true;

    public string SetSummary
    {
        get
        {
            var parts = new List<string>();
            var modelFiles = FileSet.Entries.Count(e => e.Role is ModelFileRole.Model or ModelFileRole.Shard);
            if (modelFiles > 1)
                parts.Add($"{modelFiles} shards");
            if (HasProjector)
                parts.Add("projector");
            if (HasDraftHead)
                parts.Add("MTP draft head");
            if (HasCompanions)
                parts.Add($"companions {CompanionSizeDisplay}");
            return parts.Count == 0 ? string.Empty : $"Set: {string.Join(", ", parts)}, {SystemInfoService.FormatBytes(SelectedBytes)} selected";
        }
    }

    /// <summary>Total size of what is currently ticked, so the number on screen is the number that downloads.</summary>
    public long SelectedBytes => SelectedEntries().Sum(e => e.SizeBytes ?? 0);

    public void UpdateDownloadState(string modelsDirectory, IReadOnlyList<ModelManifestEntry> manifest)
    {
        if (IsDownloading) { DownloadState = HfDownloadState.Downloading; return; }
        var selected = SelectedEntries();
        var paths = selected.Select(e => HuggingFaceBrowserSupport.PlanDestination(modelsDirectory, e.RepoPath, RepoId).DestinationPath).ToList();
        var present = paths.Count(p => File.Exists(p));
        DownloadState = present == paths.Count
            ? HfDownloadState.Downloaded
            : present > 0 ? HfDownloadState.Partial : HfDownloadState.NotDownloaded;
    }

    public IReadOnlyList<ModelFileSetEntry> SelectedEntries() =>
    [
        .. FileSet.Entries.Where(e => e.Required
            || (e.Role == ModelFileRole.Projector && IncludeProjector)
            || (e.Role == ModelFileRole.DraftHead && IncludeDraftHead))
    ];

    partial void OnIncludeProjectorChanged(bool value) => OnPropertyChanged(nameof(SetSummary));
    partial void OnIncludeDraftHeadChanged(bool value) => OnPropertyChanged(nameof(SetSummary));

    public HfFileResultViewModel(
        string repoId,
        string path,
        long? sizeBytes,
        string? lfsSha256,
        ModelFitTier fitTier,
        string fitReason,
        ModelFileSet? fileSet = null,
        string revisionSha = "")
    {
        RepoId = repoId;
        Path = path;
        RevisionSha = revisionSha;
        SizeBytes = sizeBytes;
        LfsSha256 = lfsSha256;
        FitTier = fitTier;
        FitReason = fitReason;
        FileSet = fileSet ?? new ModelFileSet(repoId, [new ModelFileSetEntry(path, sizeBytes, lfsSha256, ModelFileRole.Model, true, true)]);
        IncludeProjector = FileSet.Entries.Any(e => e.Role == ModelFileRole.Projector)
            && FileSet.Entries.Where(e => e.Role == ModelFileRole.Projector).All(e => e.SelectedByDefault);
        IncludeDraftHead = FileSet.Entries.Any(e => e.Role == ModelFileRole.DraftHead)
            && FileSet.Entries.Where(e => e.Role == ModelFileRole.DraftHead).All(e => e.SelectedByDefault);
    }

    public void ApplyResolvedDetails(
        ModelFileSet fileSet,
        ModelFitTier fitTier,
        string fitReason,
        string revisionSha,
        string modelsDirectory,
        IReadOnlyList<ModelManifestEntry> manifest)
    {
        FileSet = fileSet;
        FitTier = fitTier;
        FitReason = fitReason;
        RevisionSha = revisionSha;
        IncludeProjector = FileSet.Entries.Any(e => e.Role == ModelFileRole.Projector)
            && FileSet.Entries.Where(e => e.Role == ModelFileRole.Projector).All(e => e.SelectedByDefault);
        IncludeDraftHead = FileSet.Entries.Any(e => e.Role == ModelFileRole.DraftHead)
            && FileSet.Entries.Where(e => e.Role == ModelFileRole.DraftHead).All(e => e.SelectedByDefault);
        IsChecking = false;
        OnPropertyChanged(nameof(FitLabel));
        NotifyResolvedSetChanged();
        UpdateDownloadState(modelsDirectory, manifest);
    }

    public void MarkCheckUnavailable(string? error)
    {
        FitTier = ModelFitTier.Unknown;
        FitReason = string.IsNullOrWhiteSpace(error)
            ? "Compatibility check unavailable."
            : $"Compatibility check unavailable: {error}";
        IsChecking = false;
        OnPropertyChanged(nameof(FitLabel));
    }

    private void NotifyResolvedSetChanged()
    {
        OnPropertyChanged(nameof(IsSharded));
        OnPropertyChanged(nameof(HasProjector));
        OnPropertyChanged(nameof(HasDraftHead));
        OnPropertyChanged(nameof(HasCompanions));
        OnPropertyChanged(nameof(CompanionSizeDisplay));
        OnPropertyChanged(nameof(HasReviewRequiredCompanions));
        OnPropertyChanged(nameof(ProjectorSelectionLabel));
        OnPropertyChanged(nameof(DraftHeadSelectionLabel));
        OnPropertyChanged(nameof(CompanionReviewLabel));
        OnPropertyChanged(nameof(SetSummary));
        OnPropertyChanged(nameof(SelectedBytes));
    }
}

public partial class ModelProfileItemViewModel : ObservableObject
{
    public static IReadOnlyList<string> KvCacheTypeOptions { get; } =
        ["f16", "bf16", "q8_0", "q5_1", "q5_0", "q4_1", "q4_0", "iq4_nl"];
    private readonly string _originalDisplayName;
    private readonly string _originalDescription;
    private readonly string _originalTagsText;
    private readonly double? _originalTemperature;
    private readonly int? _originalContextSize;
    private readonly string? _originalKvCacheType;
    private readonly bool? _originalPreserveReasoning;
    private readonly int? _originalMaxTokens;
    private readonly double? _originalTopP;
    private readonly int? _originalTopK;
    private readonly double? _originalMinP;
    private readonly double? _originalRepeatPenalty;
    private readonly double? _originalFrequencyPenalty;
    private readonly double? _originalPresencePenalty;
    private readonly bool _originalIsVisible;
    private readonly string _originalAvatar;
    private readonly bool _originalAutoManageCompanionAssets;

    public string ModelId { get; }
    public string RawName { get; }
    public string Provider { get; }
    public string SizeDisplay { get; }
    public string ModifiedDisplay { get; }
    public int? ProbedContextLength { get; }
    public bool IsRunning { get; }
    public string RunningLabel => IsRunning ? "Running" : "Not running";
    public string ContextWatermark => ProbedContextLength is { } n ? $"Detected: {n}" : "Default context";

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _tagsText;
    [ObservableProperty] private double? _defaultTemperature;
    [ObservableProperty] private int? _defaultContextSize;
    [ObservableProperty] private string? _defaultKvCacheType;
    [ObservableProperty] private bool? _defaultPreserveReasoning;
    [ObservableProperty] private int? _defaultMaxTokens;
    [ObservableProperty] private double? _defaultTopP;
    [ObservableProperty] private int? _defaultTopK;
    [ObservableProperty] private double? _defaultMinP;
    [ObservableProperty] private double? _defaultRepeatPenalty;
    [ObservableProperty] private double? _defaultFrequencyPenalty;
    [ObservableProperty] private double? _defaultPresencePenalty;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _avatar;
    [ObservableProperty] private bool _autoManageCompanionAssets;

    [ObservableProperty] private bool _hasMissingCompanions;
    [ObservableProperty] private bool _hasStaleCompanions;
    [ObservableProperty] private bool _hasVerifiedCompanionReplacement;
    [ObservableProperty] private string _companionStatus = string.Empty;
    [ObservableProperty] private string _manualCompanionRepairLabel = string.Empty;

    public bool HasCompanionRepair => HasMissingCompanions || HasStaleCompanions;
    public bool CanReacquireCompanions => HasCompanionRepair && HasVerifiedCompanionReplacement;
    public bool RequiresManualCompanionRepair => HasCompanionRepair && !string.IsNullOrWhiteSpace(ManualCompanionRepairLabel);

    partial void OnHasMissingCompanionsChanged(bool value) => NotifyCompanionRepairState();
    partial void OnHasStaleCompanionsChanged(bool value) => NotifyCompanionRepairState();
    partial void OnHasVerifiedCompanionReplacementChanged(bool value) => NotifyCompanionRepairState();
    partial void OnManualCompanionRepairLabelChanged(string value) => NotifyCompanionRepairState();

    private void NotifyCompanionRepairState()
    {
        OnPropertyChanged(nameof(HasCompanionRepair));
        OnPropertyChanged(nameof(CanReacquireCompanions));
        OnPropertyChanged(nameof(RequiresManualCompanionRepair));
    }

    /// <summary>Per-item UI expansion state, not persisted (r13 02-model-library.md 2.1).</summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>Summary of the stored LlamaTuneProfile for this model, e.g. "24/32 GPU
    /// layers, 8 threads"; empty when never tuned (r13 02-model-library.md 2.3).</summary>
    [ObservableProperty] private string _tuneSummary = string.Empty;

    private LlamaTuneProfile? _tuneProfile;
    [ObservableProperty] private int _tunedGpuLayers;
    [ObservableProperty] private int _tunedThreads;
    [ObservableProperty] private int _tunedContextSize;

    public bool HasTuneProfile => _tuneProfile is not null;
    public string TuneProfileContextWatermark => _tuneProfile is null
        ? "No saved tune"
        : "Saved context";

    internal void SetTuneProfile(LlamaTuneProfile? profile)
    {
        _tuneProfile = profile;
        TunedGpuLayers = profile?.GpuLayers ?? 0;
        TunedThreads = profile?.Threads ?? 0;
        TunedContextSize = profile?.ContextSize ?? 0;
        OnPropertyChanged(nameof(HasTuneProfile));
        OnPropertyChanged(nameof(TuneProfileContextWatermark));
    }

    internal void ApplyTuneProfileChanges()
    {
        if (_tuneProfile is null)
            return;
        if (TunedThreads < 1 || TunedContextSize < 128)
            throw new InvalidOperationException("A saved tune profile needs at least 1 thread and 128 context tokens.");

        _tuneProfile.GpuLayers = TunedGpuLayers;
        _tuneProfile.Threads = TunedThreads;
        _tuneProfile.ContextSize = TunedContextSize;
        _tuneProfile.TunedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// The card shows this summary directly so a tuned model's effective
    /// runtime profile is visible without opening the configuration flyout.
    /// </summary>
    public string AutoTuneTooltip => string.IsNullOrWhiteSpace(TuneSummary)
        ? AutoTuneExplanation
        : $"{AutoTuneExplanation}\n\nCurrent tune: {TuneSummary}";

    private const string AutoTuneExplanation =
        "Probes GPU layer counts against the first configured managed llama-server executable "
        + "and saves the result to the shared tune-profile store.";

    partial void OnTuneSummaryChanged(string value) => OnPropertyChanged(nameof(AutoTuneTooltip));

    [ObservableProperty] private bool _isTuning;

    /// <summary>Only rows discovered from disk (not reported live by a running provider) can
    /// be probed by Auto tune (r13 02-model-library.md 2.3). A running
    /// llama-server can report the local GGUF by its path while retaining the
    /// llama.cpp provider tag, so the path is part of the identity check too.</summary>
    public bool IsLocalGguf => Provider == "local GGUF"
        || (ModelId.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) && File.Exists(ModelId));

    [ObservableProperty] private ModelFitTier _fitTier = ModelFitTier.Unknown;
    [ObservableProperty] private string _fitReason = string.Empty;

    /// <summary>Short chip text; empty for Unknown so the UI renders nothing rather than
    /// guessing (r13 02-model-library.md 2.5).</summary>
    public string FitLabel => ModelFitEstimator.Label(FitTier);

    partial void OnFitTierChanged(ModelFitTier value) => OnPropertyChanged(nameof(FitLabel));

    // ── r13 03-hugging-face.md: provenance/update state ───────────────────────────────────
    [ObservableProperty] private string _repoId = string.Empty;

    /// <summary>
    /// r29 doc 02 2.1: where this model came from, derived rather than bound
    /// straight to <see cref="RepoId"/>, so a second download provider is a new
    /// enum value instead of a new binding in the tile template.
    /// </summary>
    public ModelSourceKind SourceKind =>
        !string.IsNullOrWhiteSpace(RepoId) ? ModelSourceKind.HuggingFace
        : IsLocalGguf ? ModelSourceKind.LocalFile
        : ModelSourceKind.Unknown;

    /// <summary>Stable display text for the source badge. Never model-written.</summary>
    public string SourceLabel => SourceKind switch
    {
        ModelSourceKind.HuggingFace => "Hugging Face",
        ModelSourceKind.LocalFile => "Local file",
        _ => string.Empty
    };

    /// <summary>Short badge text; the tile has no room for the full label.</summary>
    public string SourceBadgeText => SourceKind switch
    {
        ModelSourceKind.HuggingFace => "HF",
        ModelSourceKind.LocalFile => "Local",
        _ => string.Empty
    };

    /// <summary>
    /// For Hugging Face the repo id itself is more useful than the words
    /// "Hugging Face", which the badge already carries.
    /// </summary>
    public string SourceTooltip =>
        SourceKind == ModelSourceKind.HuggingFace && !string.IsNullOrWhiteSpace(RepoId)
            ? $"Downloaded from Hugging Face: {RepoId}"
            : SourceLabel;

    public bool HasKnownSource => SourceKind != ModelSourceKind.Unknown;

    /// <summary>
    /// RepoId is populated by an async manifest refresh after the row already
    /// exists, and again after a repo link, so the badge has to be told.
    /// </summary>
    partial void OnRepoIdChanged(string value)
    {
        OnPropertyChanged(nameof(HasRepoLink));
        OnPropertyChanged(nameof(SourceKind));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(SourceBadgeText));
        OnPropertyChanged(nameof(SourceTooltip));
        OnPropertyChanged(nameof(HasKnownSource));
    }
    [ObservableProperty] private ModelUpdateStatus _updateStatus = ModelUpdateStatus.NotLinked;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private bool _retuneRecommended;

    public bool HasRepoLink => !string.IsNullOrWhiteSpace(RepoId);

    /// <summary>Empty for NotLinked/CheckFailed so the UI shows nothing rather than a
    /// permanently-stuck error chip; CheckFailed is surfaced via the page-level status line
    /// instead (r13 03-hugging-face.md 3.2).</summary>
    public string UpdateLabel => UpdateStatus switch
    {
        ModelUpdateStatus.UpToDate => "Up to date",
        ModelUpdateStatus.UpdateAvailable => "Update available",
        ModelUpdateStatus.NoLongerPublished => "No longer published",
        _ => string.Empty
    };

    partial void OnUpdateStatusChanged(ModelUpdateStatus value) => OnPropertyChanged(nameof(UpdateLabel));

    public ModelProfileItemViewModel(LlmModel model, ModelProfile profile, bool isRunning = false)
    {
        ModelId = model.Id;
        RawName = model.Name;
        Provider = model.Provider;
        SizeDisplay = model.SizeDisplay;
        ModifiedDisplay = model.ModifiedAt?.ToString("d MMM yyyy") ?? string.Empty;
        ProbedContextLength = model.ProbedContextLength;
        IsRunning = isRunning;
        _displayName = profile.DisplayName;
        _description = profile.Description;
        _tagsText = string.Join(", ", profile.Tags);
        _defaultTemperature = profile.DefaultTemperature;
        _defaultContextSize = profile.DefaultContextSize;
        _defaultKvCacheType = profile.DefaultKvCacheType;
        _defaultPreserveReasoning = profile.DefaultPreserveReasoning;
        _defaultMaxTokens = profile.DefaultMaxTokens;
        _defaultTopP = profile.DefaultTopP;
        _defaultTopK = profile.DefaultTopK;
        _defaultMinP = profile.DefaultMinP;
        _defaultRepeatPenalty = profile.DefaultRepeatPenalty;
        _defaultFrequencyPenalty = profile.DefaultFrequencyPenalty;
        _defaultPresencePenalty = profile.DefaultPresencePenalty;
        _isVisible = profile.IsVisible;
        _avatar = profile.Avatar;
        _autoManageCompanionAssets = profile.AutoManageCompanionAssets;

        _originalDisplayName = DisplayName;
        _originalDescription = Description;
        _originalTagsText = TagsText;
        _originalTemperature = DefaultTemperature;
        _originalContextSize = DefaultContextSize;
        _originalKvCacheType = DefaultKvCacheType;
        _originalPreserveReasoning = DefaultPreserveReasoning;
        _originalMaxTokens = DefaultMaxTokens;
        _originalTopP = DefaultTopP;
        _originalTopK = DefaultTopK;
        _originalMinP = DefaultMinP;
        _originalRepeatPenalty = DefaultRepeatPenalty;
        _originalFrequencyPenalty = DefaultFrequencyPenalty;
        _originalPresencePenalty = DefaultPresencePenalty;
        _originalIsVisible = IsVisible;
        _originalAvatar = Avatar;
        _originalAutoManageCompanionAssets = AutoManageCompanionAssets;
    }

    public string EffectiveName => string.IsNullOrWhiteSpace(DisplayName) ? RawName : DisplayName.Trim();
    public string TagsDisplay => string.Join("  ", Tags);

    public ModelProfile ToProfile() => new()
    {
        ModelId = ModelId,
        DisplayName = DisplayName,
        Description = Description,
        Tags = Tags,
        DefaultTemperature = DefaultTemperature,
        DefaultContextSize = DefaultContextSize,
        DefaultKvCacheType = DefaultKvCacheType,
        DefaultPreserveReasoning = DefaultPreserveReasoning,
        DefaultMaxTokens = DefaultMaxTokens,
        DefaultTopP = DefaultTopP,
        DefaultTopK = DefaultTopK,
        DefaultMinP = DefaultMinP,
        DefaultRepeatPenalty = DefaultRepeatPenalty,
        DefaultFrequencyPenalty = DefaultFrequencyPenalty,
        DefaultPresencePenalty = DefaultPresencePenalty,
        Backend = Provider,
        IsVisible = IsVisible,
        Avatar = Avatar,
        AutoManageCompanionAssets = AutoManageCompanionAssets
    };

    public void ApplySavedState()
    {
        OnPropertyChanged(nameof(EffectiveName));
        OnPropertyChanged(nameof(TagsDisplay));
    }

    public void Reset()
    {
        DisplayName = string.Empty;
        Description = string.Empty;
        TagsText = string.Empty;
        DefaultTemperature = null;
        DefaultContextSize = null;
        DefaultKvCacheType = null;
        DefaultPreserveReasoning = null;
        DefaultMaxTokens = null;
        DefaultTopP = null;
        DefaultTopK = null;
        DefaultMinP = null;
        DefaultRepeatPenalty = null;
        DefaultFrequencyPenalty = null;
        DefaultPresencePenalty = null;
        IsVisible = true;
        Avatar = string.Empty;
        AutoManageCompanionAssets = true;
        ApplySavedState();
    }

    public void Revert()
    {
        DisplayName = _originalDisplayName;
        Description = _originalDescription;
        TagsText = _originalTagsText;
        DefaultTemperature = _originalTemperature;
        DefaultContextSize = _originalContextSize;
        DefaultKvCacheType = _originalKvCacheType;
        DefaultPreserveReasoning = _originalPreserveReasoning;
        DefaultMaxTokens = _originalMaxTokens;
        DefaultTopP = _originalTopP;
        DefaultTopK = _originalTopK;
        DefaultMinP = _originalMinP;
        DefaultRepeatPenalty = _originalRepeatPenalty;
        DefaultFrequencyPenalty = _originalFrequencyPenalty;
        DefaultPresencePenalty = _originalPresencePenalty;
        IsVisible = _originalIsVisible;
        Avatar = _originalAvatar;
        AutoManageCompanionAssets = _originalAutoManageCompanionAssets;
        ApplySavedState();
    }

    private List<string> Tags => TagsText
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(t => t.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();

    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(EffectiveName));
    partial void OnTagsTextChanged(string value) => OnPropertyChanged(nameof(TagsDisplay));
}
