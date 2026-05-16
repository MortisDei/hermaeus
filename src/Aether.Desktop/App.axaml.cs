using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Aether.Agent.Services;
using Aether.Core.Services;
using Aether.Desktop.Views;
using Aether.Rag;
using Aether.Rag.Embeddings;
using Aether.Rag.Eval;
using Aether.Rag.Pipeline;
using Aether.Rag.Retrieval;
using Aether.Rag.Storage;
using Aether.Services;
using Aether.Services.ProcessManagement;
using Aether.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;
    private DesktopIntegrationService? _desktopIntegration;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        var sp = services.BuildServiceProvider();
        _services = sp;
        Ioc.Default.ConfigureServices(sp);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm     = sp.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = vm };
            _desktopIntegration = new DesktopIntegrationService(vm);
            window.DesktopIntegration = _desktopIntegration;
            _desktopIntegration.Attach(window);
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await InitializeAppAsync(sp, vm);
            desktop.Exit += (_, _) =>
            {
                _desktopIntegration?.Dispose();
                vm.Shutdown();
                sp.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeAppAsync(ServiceProvider sp, MainWindowViewModel vm)
    {
        try
        {
            await sp.GetRequiredService<ISettingsService>().LoadAsync();
            await sp.GetRequiredService<IConversationStore>().InitializeAsync();
            await sp.GetRequiredService<IMemoryStore>().InitializeAsync();
            await sp.GetRequiredService<SqliteRagStore>().InitializeAsync();
            await sp.GetRequiredService<IAgentTaskStateStore>().InitializeAsync();
            await sp.GetRequiredService<IBenchmarkService>().InitializeAsync();
            sp.GetRequiredService<IAutomationScheduler>().Start();
            // Probe active voice provider health at startup to detect externally-running services
            try
            {
                await vm.Settings.Tts.ProbeActiveProviderHealthAsync();
            }
            catch { }
            await vm.InitializeAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Aether startup initialization failed: {ex}");
        }
    }

    private static void ConfigureServices(IServiceCollection s)
    {
        s.AddSingleton<ISettingsService,   SettingsService>();
        s.AddSingleton<ISecretStore,       SecretStore>();
        s.AddSingleton<IRedactionService,  RedactionService>();
        s.AddSingleton<IRuntimeLogService, RuntimeLogService>();
        s.AddSingleton<PythonHealthValidator>();
        s.AddSingleton<IBackupService,     BackupService>();
        s.AddSingleton<ILocalAiSetupService, LocalAiSetupService>();
        s.AddSingleton<ITrustService,      TrustService>();
        s.AddSingleton<IDoctorService,     DoctorService>();
        s.AddSingleton<ISystemInfoService, SystemInfoService>();
        s.AddSingleton<IBenchmarkService,  BenchmarkService>();
        s.AddSingleton<IConversationStore, ConversationStore>();
        s.AddSingleton<IMemoryStore, MemoryStore>();
        s.AddSingleton<IMemoryExtractionService, MemoryExtractionService>();
        s.AddSingleton<IMemoryInjectionService, MemoryInjectionService>();
        s.AddSingleton<IConversationMemoryService, ConversationMemoryService>();
        s.AddSingleton<LlamaCppService>();
        s.AddSingleton<OpenAiService>();
        s.AddSingleton<IRuntimeProfileService, RuntimeProfileService>();
        s.AddSingleton<OllamaService>();
        s.AddSingleton<ILlmService,        CompositeLlmService>();
        s.AddSingleton<IModelProfileService, ModelProfileService>();
        s.AddSingleton<XttsV2VoiceProvider>();
        s.AddSingleton<KokoroVoiceProvider>();
        s.AddSingleton<F5TtsVoiceProvider>();
        s.AddSingleton<OpenAiVoiceProvider>();
        s.AddSingleton<IVoiceProviderRegistry, VoiceProviderRegistry>();
        s.AddSingleton<ITtsService, VoiceRoutingTtsService>();
        s.AddSingleton<XttsProcessManager>();
        s.AddSingleton<KokoroProcessManager>();
        s.AddSingleton<IToastService,      ToastService>();
        s.AddSingleton<IAutomationScheduler, AutomationScheduler>();
        s.AddSingleton<SqliteRagStore>();
        s.AddSingleton<IEmbeddingService,  LlamaCppEmbeddingService>();
        s.AddSingleton<IReranker,          OnnxCrossEncoderReranker>();
        s.AddSingleton<RagPipeline>();
        s.AddSingleton<RagQueryService>();
        s.AddSingleton<RagEvalService>();
        s.AddSingleton<IAgentTaskStateStore, FileAgentTaskStateStore>();
        s.AddSingleton<IAgentWorkspaceMemoryStore, FileAgentWorkspaceMemoryStore>();
        s.AddSingleton<IAgentWorkspaceTools, AgentWorkspaceTools>();
        s.AddSingleton<IAgentSafetyGate, AgentSafetyGate>();
        s.AddSingleton<IAgentContextBuilder, AgentContextBuilder>();
        s.AddSingleton<IAgentService, AgentService>();
        s.AddSingleton<IPatchDiffService, PatchDiffService>();
        s.AddSingleton<ChatViewModel>();
        s.AddSingleton<AgentViewModel>();
        s.AddSingleton<SettingsViewModel>();
        s.AddSingleton<ModelManagementViewModel>();
        s.AddSingleton<RagViewModel>();
        s.AddSingleton<ServicesViewModel>();
        s.AddSingleton<TasksViewModel>();
        s.AddSingleton<BenchmarkViewModel>();
        s.AddSingleton<SystemOverviewViewModel>();
        s.AddSingleton<DoctorViewModel>();
        s.AddSingleton<MemoriesViewModel>();
        s.AddSingleton<SessionUsageViewModel>();
        s.AddSingleton<SessionUsageDetailViewModel>();
        s.AddSingleton<DraftPatchDiffViewModel>();
        s.AddSingleton<LogsViewModel>();
        s.AddSingleton<SetupWizardViewModel>();
        s.AddSingleton<MainWindowViewModel>();
    }
}
