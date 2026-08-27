using System.ComponentModel;
using System.Collections.Specialized;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _svc;
    private readonly IToastService _toasts;
    private readonly XttsProcessManager _xttsProcess;
    private readonly KokoroProcessManager _kokoroProcess;
    private readonly LocalApiProcessManager _localApiProcess;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _autoSaveGate = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _autoSaveDelay;
    private readonly Action? _autoSaveLifecycleCompleted;
    private CancellationTokenSource? _autoSaveCts;
    private bool _isReloading;
    private bool _isShuttingDown;

    [ObservableProperty] private bool _isSaved;
    [ObservableProperty] private string _settingsError = string.Empty;
    [ObservableProperty] private string _persistenceStatus = "Saved";

    public LlmDefaultsSettingsViewModel Llm { get; }
    public RagSettingsViewModel Rag { get; }
    public DataManagementSettingsViewModel Data { get; }
    public LocalAiSetupSettingsViewModel LocalAiSetup { get; }
    public UiSettingsViewModel Ui { get; }
    public MemorySettingsViewModel Memory { get; }
    public McpSettingsViewModel Mcp { get; }
    public LocalApiSettingsViewModel LocalApi { get; }
    public TrustSettingsViewModel Trust { get; }
    public TtsSettingsViewModel Tts { get; }

    public Action? RequestShowSetupWizard { get; set; }

    public bool EnableGlobalHotkeys
    {
        get => Ui.EnableGlobalHotkeys;
        set
        {
            if (Ui.EnableGlobalHotkeys == value) return;
            Ui.EnableGlobalHotkeys = value;
            OnPropertyChanged();
        }
    }

    public string GlobalHotkeyStatus
    {
        get => Ui.GlobalHotkeyStatus;
        set
        {
            if (Ui.GlobalHotkeyStatus == value) return;
            Ui.GlobalHotkeyStatus = value;
            OnPropertyChanged();
        }
    }

    public bool ShowQuickChat
    {
        get => Ui.ShowQuickChat;
        set
        {
            if (Ui.ShowQuickChat == value) return;
            Ui.ShowQuickChat = value;
            OnPropertyChanged();
        }
    }

    public bool StartMinimized
    {
        get => Ui.StartMinimized;
        set
        {
            if (Ui.StartMinimized == value) return;
            Ui.StartMinimized = value;
            OnPropertyChanged();
        }
    }

    public bool MinimizeToTray
    {
        get => Ui.MinimizeToTray;
        set
        {
            if (Ui.MinimizeToTray == value) return;
            Ui.MinimizeToTray = value;
            OnPropertyChanged();
        }
    }

    public bool CloseToTray
    {
        get => Ui.CloseToTray;
        set
        {
            if (Ui.CloseToTray == value) return;
            Ui.CloseToTray = value;
            OnPropertyChanged();
        }
    }

    public bool EnableTrayIcon
    {
        get => Ui.EnableTrayIcon;
        set
        {
            if (Ui.EnableTrayIcon == value) return;
            Ui.EnableTrayIcon = value;
            OnPropertyChanged();
        }
    }

    public bool EnableLocalHotkeys
    {
        get => Ui.EnableLocalHotkeys;
        set
        {
            if (Ui.EnableLocalHotkeys == value) return;
            Ui.EnableLocalHotkeys = value;
            OnPropertyChanged();
        }
    }

    public string OpenAiApiKey
    {
        get => Llm.OpenAiApiKey;
        set
        {
            if (Llm.OpenAiApiKey == value) return;
            Llm.OpenAiApiKey = value;
            OnPropertyChanged();
        }
    }

    public SettingsViewModel(
        ISettingsService svc,
        TtsSettingsViewModel tts,
        IToastService toasts,
        BackupService backups,
        ISecretStore secrets,
        XttsProcessManager xttsProcess,
        KokoroProcessManager kokoroProcess,
        LocalApiProcessManager localApiProcess,
        LocalAiSetupService localAiSetup,
        TrustService trust,
        Hermaeus.Services.Recall.RecallIndexingService? recallIndexing = null,
        IActivityRecorder? activity = null,
        Func<TimeSpan, CancellationToken, Task>? autoSaveDelay = null,
        Action? autoSaveLifecycleCompleted = null)
    {
        _svc = svc;
        _toasts = toasts;
        _xttsProcess = xttsProcess;
        _kokoroProcess = kokoroProcess;
        _localApiProcess = localApiProcess;
        _autoSaveDelay = autoSaveDelay ?? Task.Delay;
        _autoSaveLifecycleCompleted = autoSaveLifecycleCompleted;

        Llm = new LlmDefaultsSettingsViewModel(secrets);
        Rag = new RagSettingsViewModel(ResolveDataRoot);
        Data = new DataManagementSettingsViewModel(_svc, backups, _toasts, ResolveDataRoot, activity);
        Ui = new UiSettingsViewModel();
        Memory = new MemorySettingsViewModel(recallIndexing, _toasts);
        Mcp = new McpSettingsViewModel();
        LocalApi = new LocalApiSettingsViewModel(secrets, _svc);
        LocalApi.ProcessStatusLabel = _localApiProcess.StatusLabel;
        _localApiProcess.StatusChanged += () => RunOnUi(() => LocalApi.ProcessStatusLabel = _localApiProcess.StatusLabel);
        // Voice providers/process management now live on the Services page; this VM
        // is shared (DI singleton) so both pages reflect the same live state - see
        // ServicesViewModel.Tts.
        Tts = tts;
        LocalAiSetup = new LocalAiSetupSettingsViewModel(_svc, localAiSetup, _toasts, Tts, Data, Rag, SaveAsync);
        Trust = new TrustSettingsViewModel(_svc, trust, _toasts, Tts, Data, Rag);

        SubscribeToAutoSave(Llm);
        SubscribeToAutoSave(Rag);
        SubscribeToAutoSave(Ui);
        SubscribeToAutoSave(Memory);
        SubscribeToAutoSave(Mcp);
        SubscribeToAutoSave(LocalApi);
        SubscribeToAutoSave(Tts);
        Tts.VoiceChannels.CollectionChanged += OnTtsCollectionChanged;
        Tts.AudioFeedbackEvents.CollectionChanged += OnTtsCollectionChanged;
        HookTtsChildren();

        Ui.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UiSettingsViewModel.EnableGlobalHotkeys))
                OnPropertyChanged(nameof(EnableGlobalHotkeys));
            if (e.PropertyName == nameof(UiSettingsViewModel.GlobalHotkeyStatus))
                OnPropertyChanged(nameof(GlobalHotkeyStatus));
            if (e.PropertyName == nameof(UiSettingsViewModel.ShowQuickChat))
                OnPropertyChanged(nameof(ShowQuickChat));
            if (e.PropertyName == nameof(UiSettingsViewModel.StartMinimized))
                OnPropertyChanged(nameof(StartMinimized));
            if (e.PropertyName == nameof(UiSettingsViewModel.MinimizeToTray))
                OnPropertyChanged(nameof(MinimizeToTray));
            if (e.PropertyName == nameof(UiSettingsViewModel.EnableTrayIcon))
                OnPropertyChanged(nameof(EnableTrayIcon));
            if (e.PropertyName == nameof(UiSettingsViewModel.EnableLocalHotkeys))
                OnPropertyChanged(nameof(EnableLocalHotkeys));
        };
        Data.LocalAiAssetsRootChanged += () => Rag.RefreshLocalAiAssetOptions(Data.LocalAiAssetsRoot);

        Reload();
    }

    [RelayCommand]
    private async Task ReRunSetupWizardAsync()
    {
        _svc.Settings.SetupWizardCompleted = false;
        await _svc.SaveAsync();
        RequestShowSetupWizard?.Invoke();
    }

    public void Reload()
    {
        CancelAutoSave();
        _isReloading = true;
        try
        {
            var settings = _svc.Settings;
            Llm.ReloadFrom(settings);
            Data.ReloadFrom(settings);
            Rag.ReloadFrom(settings, Data.LocalAiAssetsRoot);
            Tts.ReloadFrom(settings);
            Ui.ReloadFrom(settings);
            Memory.ReloadFrom(settings);
            Mcp.ReloadFrom(settings);
            LocalApi.ReloadFrom(settings);
            SettingsError = string.Empty;
            // r12 01-settings-lifecycle.md 1.7: Reset previously left stale
            // Trust/LocalAiSetup error text behind since only Save cleared it.
            Data.SettingsError = string.Empty;
            LocalAiSetup.SettingsError = string.Empty;
            Trust.SettingsError = string.Empty;
            PersistenceStatus = "Saved";
        }
        finally
        {
            _isReloading = false;
        }
    }

    /// <summary>
    /// r12 01-settings-lifecycle.md 1.2: every tab's edits are applied onto a
    /// deep copy of <see cref="ISettingsService.Settings"/>, never the live
    /// object. <see cref="ISettingsService.SaveAsync(AppSettings, string)"/>
    /// only swaps the copy in once validation and any data-root migration
    /// actually succeed, so a failed save leaves the live settings (and every
    /// other in-flight edit riding along with this save) exactly as they
    /// were - no partial edit survives in memory to be silently persisted by
    /// a later, unrelated save.
    /// </summary>
    [RelayCommand]
    public Task SaveAsync() => SaveCoreAsync(showToast: true, CancellationToken.None);

    private async Task SaveCoreAsync(bool showToast, CancellationToken ct)
    {
        await _saveGate.WaitAsync(ct);
        try
        {
            await SaveCoreLockedAsync(showToast, ct);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task SaveCoreLockedAsync(bool showToast, CancellationToken ct)
    {
        var candidate = _svc.Settings.Clone();
        var previousDataRoot = _svc.Settings.DataManagement.DataRootDirectory;
        SettingsError = string.Empty;
        Data.SettingsError = string.Empty;
        LocalAiSetup.SettingsError = string.Empty;
        Trust.SettingsError = string.Empty;

        await Llm.ApplyToAsync(candidate);
        Rag.ApplyTo(candidate);
        Data.ApplyTo(candidate);
        Ui.ApplyTo(candidate);
        Memory.ApplyTo(candidate);
        Mcp.ApplyTo(candidate);
        await LocalApi.ApplyToAsync(candidate);
        ApplyTtsTo(candidate);

        try
        {
            PersistenceStatus = "Saving";
            var result = await _svc.SaveAsync(candidate, previousDataRoot);
            if (result.DataMigrated && showToast)
            {
                var message = $"Moved {result.FilesMoved} database file(s) to {result.CurrentDataRoot}. Backup: {result.BackupDirectory}";
                _toasts.Show("Hermaeus data moved", message, ToastKind.Success, 7000);
            }
        }
        catch (Exception ex)
        {
            // The live settings object was never touched, so the data-root
            // box just needs to match it again; nothing else to roll back.
            Data.DataRootDirectory = previousDataRoot;
            SettingsError = ex.Message;
            PersistenceStatus = "Failed";
            if (showToast)
                _toasts.Show("Settings not saved", ex.Message, ToastKind.Error);
            return;
        }

        PersistenceStatus = "Saved";
        IsSaved = true;
        if (showToast)
            _toasts.Show("Settings saved", "Hermaeus settings were updated.", ToastKind.Success);
        await EnsureLocalApiRunningStateAsync();
        // r12 01-settings-lifecycle.md 1.7: reset the flag after a short
        // delay without keeping the async command "executing" for it.
        _ = ResetIsSavedAfterDelayAsync();
    }

    private void SubscribeToAutoSave(INotifyPropertyChanged source) =>
        source.PropertyChanged += OnEditablePropertyChanged;

    private void HookTtsChildren()
    {
        foreach (var channel in Tts.VoiceChannels)
            SubscribeToAutoSave(channel);
        foreach (var toggle in Tts.AudioFeedbackEvents)
            SubscribeToAutoSave(toggle);
    }

    private void OnTtsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<INotifyPropertyChanged>())
                SubscribeToAutoSave(item);
        }
        if (!_isReloading)
            ScheduleAutoSave();
    }

    private void OnEditablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isReloading)
            return;
        ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        CancellationTokenSource? previous;
        CancellationTokenSource cts;
        CancellationToken token;
        lock (_autoSaveGate)
        {
            if (_isShuttingDown)
                return;

            previous = _autoSaveCts;
            cts = new CancellationTokenSource();
            token = cts.Token;
            _autoSaveCts = cts;
        }

        CancelAndDispose(previous);
        PersistenceStatus = "Saving";
        _ = AutoSaveAfterDelayAsync(cts, token);
    }

    private async Task AutoSaveAfterDelayAsync(CancellationTokenSource cts, CancellationToken token)
    {
        try
        {
            await _autoSaveDelay(TimeSpan.FromMilliseconds(600), token);
            await SaveCoreAsync(showToast: false, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentAutoSave(cts))
            {
                PersistenceStatus = "Failed";
                SettingsError = ex.Message;
            }
        }
        finally
        {
            CompleteAutoSave(cts);
            _autoSaveLifecycleCompleted?.Invoke();
        }
    }

    private async Task ResetIsSavedAfterDelayAsync()
    {
        await Task.Delay(2000);
        IsSaved = false;
    }

    [RelayCommand]
    private void Reset() => Reload();

    public void Shutdown()
    {
        lock (_autoSaveGate)
            _isShuttingDown = true;
        CancelAutoSave();
        Tts.Dispose();
        _xttsProcess.Stop();
        _kokoroProcess.Stop();
        _localApiProcess.Stop();
    }

    private void CancelAutoSave()
    {
        CancellationTokenSource? current;
        lock (_autoSaveGate)
        {
            current = _autoSaveCts;
            _autoSaveCts = null;
        }

        CancelAndDispose(current);
    }

    private void CompleteAutoSave(CancellationTokenSource completed)
    {
        var ownsSource = false;
        lock (_autoSaveGate)
        {
            if (ReferenceEquals(_autoSaveCts, completed))
            {
                _autoSaveCts = null;
                ownsSource = true;
            }
        }

        if (ownsSource)
            completed.Dispose();
    }

    private bool IsCurrentAutoSave(CancellationTokenSource candidate)
    {
        lock (_autoSaveGate)
            return ReferenceEquals(_autoSaveCts, candidate);
    }

    private static void CancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts is null)
            return;

        cts.Cancel();
        cts.Dispose();
    }

    /// <summary>
    /// Starts or stops the Hermaeus.LocalApi child process to match
    /// <c>LocalApi.Enabled</c>. Called after every settings save (so toggling
    /// the checkbox takes effect immediately) and once at app startup.
    /// </summary>
    public async Task EnsureLocalApiRunningStateAsync()
    {
        try
        {
            if (_svc.Settings.LocalApi.Enabled)
            {
                if (!_localApiProcess.IsRunning)
                    await _localApiProcess.StartAsync(_svc.Settings);
            }
            else
            {
                _localApiProcess.Stop();
            }
        }
        catch (Exception ex)
        {
            _toasts.Show("Local API did not start", ex.Message, ToastKind.Warning);
        }
    }

    private void ApplyTtsTo(AppSettings settings)
    {
        settings.Tts.Enabled = Tts.TtsEnabled;
        settings.Tts.ServiceUrl = Tts.TtsServiceUrl;
        settings.Tts.Speaker = Tts.TtsSpeaker;
        settings.Tts.PythonPath = Tts.TtsPythonPath.Trim();
        settings.Tts.ScriptPath = Tts.TtsScriptPath.Trim();
        settings.Tts.ModelDirectory = Tts.TtsModelDirectory.Trim();
        settings.Tts.OutputDirectory = Tts.TtsOutputDirectory.Trim();
        settings.Tts.VoiceDirectory = Tts.TtsVoiceDirectory.Trim();
        settings.Tts.Device = Tts.TtsDevice;
        settings.Tts.ModelVersion = Tts.TtsModelVersion.Trim();
        settings.Tts.Speed = Tts.TtsSpeed;
        settings.Tts.Preload = Tts.TtsPreload;
        settings.Tts.VoiceProvider = Tts.SelectedVoiceProvider;
        Tts.ApplyVoiceOrchestrationTo(settings.Tts);
    }

    private string ResolveDataRoot()
    {
        var configured = _svc.Settings.DataManagement.DataRootDirectory?.Trim();
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermaeus")
            : Path.GetFullPath(configured);
    }

}
