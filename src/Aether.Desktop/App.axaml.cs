using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Aether.Agent.Services;
using Aether.Composition;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag.Embeddings;
using Aether.Desktop.Views;
using Aether.Rag.Storage;
using Aether.Services;
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
            var window = new MainWindow
            {
                DataContext = vm,
                PatchDiffService = sp.GetRequiredService<IPatchDiffService>()
            };
            _desktopIntegration = new DesktopIntegrationService(vm);
            window.DesktopIntegration = _desktopIntegration;
            _desktopIntegration.Attach(window);
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await InitializeAppAsync(sp, vm);
            desktop.Exit += (_, _) =>
            {
                try
                {
                    sp.GetRequiredService<AppLifecycleJournalService>().RecordCleanExit();
                    _desktopIntegration?.Dispose();
                    vm.Shutdown();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error during shutdown: {ex}");
                }
                finally
                {
                    sp.Dispose();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeAppAsync(ServiceProvider sp, MainWindowViewModel vm)
    {
        try
        {
            await sp.GetRequiredService<ISettingsService>().LoadAsync();
            sp.GetRequiredService<AppLifecycleJournalService>().RecordStartup();
            // Constructed purely for its side effect: subscribes to toasts and
            // forwards Warning/Error ones onto the Notification voice channel.
            sp.GetRequiredService<VoiceNotificationBridge>();
            await Task.WhenAll(
                sp.GetRequiredService<IConversationStore>().InitializeAsync(),
                sp.GetRequiredService<IMemoryStore>().InitializeAsync(),
                sp.GetRequiredService<SqliteRagStore>().InitializeAsync(),
                sp.GetRequiredService<IAgentTaskStateStore>().InitializeAsync(),
                sp.GetRequiredService<BenchmarkService>().InitializeAsync(),
                sp.GetRequiredService<IEvalStore>().InitializeAsync());

            // Warm up the embedding model at startup to prevent "cold-start" delay on first chat
            try
            {
                var embeddings = sp.GetService<IEmbeddingService>();
                if (embeddings is not null)
                {
                    await embeddings.EmbedAsync("warmup", CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                sp.GetRequiredService<IRuntimeLogService>().Add(new RuntimeLogEntry(
                    DateTime.UtcNow,
                    RuntimeLogLevel.Warning,
                    RuntimeLogCategory.Service,
                    $"Embedding model warm-up failed: {ex.Message}"));
            }

            // Probe active voice provider health at startup to detect externally-running services
            try
            {
                await vm.Settings.Tts.ProbeActiveProviderHealthAsync();
            }
            catch (Exception ex)
            {
                sp.GetRequiredService<IRuntimeLogService>().Add(new RuntimeLogEntry(
                    DateTime.UtcNow,
                    RuntimeLogLevel.Warning,
                    RuntimeLogCategory.Service,
                    $"Voice provider startup probe failed: {ex.Message}"));
            }
            await vm.InitializeAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Aether startup initialization failed: {ex}");
        }
    }

    private static void ConfigureServices(IServiceCollection s)
    {
        s.AddAetherCoreServices();
        s.AddSingleton<ChatViewModel>();
        s.AddSingleton<AgentViewModel>();
        s.AddSingleton<SettingsViewModel>();
        s.AddSingleton<ModelManagementViewModel>();
        s.AddSingleton<RagViewModel>();
        s.AddSingleton<ServicesViewModel>();
        s.AddSingleton<BenchmarkViewModel>();
        s.AddSingleton<SystemOverviewViewModel>();
        s.AddSingleton<DoctorViewModel>();
        s.AddSingleton<MemoriesViewModel>();
        s.AddSingleton<LogsViewModel>();
        s.AddSingleton<SetupWizardViewModel>();
        s.AddSingleton<MainWindowViewModel>();
    }
}
