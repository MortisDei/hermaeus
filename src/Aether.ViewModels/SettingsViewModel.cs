using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Aether.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _svc;
    private readonly IToastService _toasts;
    private readonly XttsProcessManager _xttsProcess;
    private readonly KokoroProcessManager _kokoroProcess;
    private readonly LocalApiProcessManager _localApiProcess;
    private readonly ServicesViewModel? _servicesView;

    [ObservableProperty] private bool _isSaved;
    [ObservableProperty] private string _settingsError = string.Empty;

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
        ITtsService tts,
        IVoiceProviderRegistry voiceProviderRegistry,
        IToastService toasts,
        BackupService backups,
        ISecretStore secrets,
        XttsProcessManager xttsProcess,
        KokoroProcessManager kokoroProcess,
        LocalApiProcessManager localApiProcess,
        LocalAiSetupService localAiSetup,
        TrustService trust,
        ServicesViewModel? services = null,
        IVoiceOrchestrator? voiceOrchestrator = null)
    {
        _svc = svc;
        _toasts = toasts;
        _xttsProcess = xttsProcess;
        _kokoroProcess = kokoroProcess;
        _localApiProcess = localApiProcess;
        _servicesView = services;

        Llm = new LlmDefaultsSettingsViewModel(secrets);
        Rag = new RagSettingsViewModel(ResolveDataRoot);
        Data = new DataManagementSettingsViewModel(_svc, backups, _toasts, ResolveDataRoot);
        Ui = new UiSettingsViewModel();
        Memory = new MemorySettingsViewModel();
        Mcp = new McpSettingsViewModel();
        LocalApi = new LocalApiSettingsViewModel(secrets, _svc);
        LocalApi.ProcessStatusLabel = _localApiProcess.StatusLabel;
        _localApiProcess.StatusChanged += () => RunOnUi(() => LocalApi.ProcessStatusLabel = _localApiProcess.StatusLabel);
        Tts = new TtsSettingsViewModel(tts, voiceProviderRegistry, _toasts, xttsProcess, kokoroProcess, secrets, _svc, voiceOrchestrator);
        LocalAiSetup = new LocalAiSetupSettingsViewModel(_svc, localAiSetup, _toasts, Tts, Data, Rag, SaveAsync);
        Trust = new TrustSettingsViewModel(_svc, trust, _toasts, Tts, Data, Rag);

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
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        var settings = _svc.Settings;
        var previousDataRoot = settings.DataManagement.DataRootDirectory;
        var previousEmbedding = settings.Rag.EmbeddingModel;
        SettingsError = string.Empty;
        Data.SettingsError = string.Empty;
        LocalAiSetup.SettingsError = string.Empty;
        Trust.SettingsError = string.Empty;

        await Llm.ApplyToAsync(settings);
        Rag.ApplyTo(settings);
        Data.ApplyTo(settings);
        Ui.ApplyTo(settings);
        Memory.ApplyTo(settings);
        Mcp.ApplyTo(settings);
        await LocalApi.ApplyToAsync(settings);
        ApplyTtsTo(settings);

        try
        {
            var result = await _svc.SaveAsync(previousDataRoot);
            if (result.DataMigrated)
            {
                var message = $"Moved {result.FilesMoved} database file(s) to {result.CurrentDataRoot}. Backup: {result.BackupDirectory}";
                _toasts.Show("Aether data moved", message, ToastKind.Success, 7000);
            }
        }
        catch (Exception ex)
        {
            settings.DataManagement.DataRootDirectory = previousDataRoot;
            Data.DataRootDirectory = previousDataRoot;
            SettingsError = ex.Message;
            _toasts.Show("Settings not saved", ex.Message, ToastKind.Error);
            return;
        }

        IsSaved = true;
        _toasts.Show("Settings saved", "Aether settings were updated.", ToastKind.Success);
        await ApplyEmbeddingModelChangeAsync(previousEmbedding);
        await EnsureLocalApiRunningStateAsync();
        await Task.Delay(2000);
        IsSaved = false;
    }

    [RelayCommand]
    private void Reset() => Reload();

    public void Shutdown()
    {
        Tts.Dispose();
        _xttsProcess.Stop();
        _kokoroProcess.Stop();
        _localApiProcess.Stop();
    }

    /// <summary>
    /// Starts or stops the Aether.LocalApi child process to match
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
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
            : Path.GetFullPath(configured);
    }

    private async Task ApplyEmbeddingModelChangeAsync(string previousEmbedding)
    {
        try
        {
            if (string.Equals(previousEmbedding, Rag.EmbeddingModel, StringComparison.OrdinalIgnoreCase))
                return;

            var modelPath = ResolveLocalEmbeddingModelPath(Rag.EmbeddingModel, Data.LocalAiAssetsRoot);
            if (string.IsNullOrWhiteSpace(modelPath))
                return;

            var server = _svc.Settings.ManagedServers.FirstOrDefault(s => s.EmbeddingsMode);
            if (server is null)
                return;

            server.ModelPath = modelPath;
            await _svc.SaveAsync();
            if (_servicesView is null)
                return;

            var embedServer = _servicesView.Servers.FirstOrDefault(x => x.EmbeddingsMode);
            if (embedServer is not null)
            {
                await _servicesView.RestartServersAsync([embedServer.Id]);
                _toasts.Show("Embedding server restarted", "Embedding server restarted with the new model.", ToastKind.Info);
            }
        }
        catch (Exception ex)
        {
            _toasts.Show("Embedding model apply failed", ex.Message, ToastKind.Warning);
        }
    }

    private static string ResolveLocalEmbeddingModelPath(string modelId, string root)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(modelId)) return string.Empty;
            var candidates = LocalAiAssetLocator.FindEmbeddingModels(root)
                .Where(p => Path.GetFileNameWithoutExtension(p).IndexOf(modelId, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p.Length)
                .ToList();
            return candidates.FirstOrDefault() ?? string.Empty;
        }
        catch { return string.Empty; }
    }
}
