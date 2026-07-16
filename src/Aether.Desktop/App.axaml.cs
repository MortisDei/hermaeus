using System.Diagnostics;
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
        UiThreadGuard.Arm();
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
                    // ServiceProvider.Dispose() throws for any registered
                    // singleton that is IAsyncDisposable-only (e.g.
                    // McpToolBridge). The app is exiting and there is no UI
                    // thread work left to deadlock on, so a bounded blocking
                    // wait on the async path is the honest version; a hung
                    // MCP child is abandoned to the job object on timeout.
                    try
                    {
                        sp.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error disposing services during shutdown: {ex}");
                    }
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeAppAsync(ServiceProvider sp, MainWindowViewModel vm)
    {
        var totalTimer = Stopwatch.StartNew();
        var phases = new List<(string Phase, long Ms)>();
        IRuntimeLogService? logs = null;
        try
        {
            logs = sp.GetRequiredService<IRuntimeLogService>();

            var phaseTimer = Stopwatch.StartNew();
            await sp.GetRequiredService<ISettingsService>().LoadAsync();
            sp.GetRequiredService<AppLifecycleJournalService>().RecordStartup();
            // Constructed purely for its side effect: subscribes to toasts and
            // forwards Warning/Error ones onto the Notification voice channel.
            sp.GetRequiredService<VoiceNotificationBridge>();
            phases.Add(("settings", phaseTimer.ElapsedMilliseconds));

            phaseTimer.Restart();
            await Task.WhenAll(
                sp.GetRequiredService<IConversationStore>().InitializeAsync(),
                sp.GetRequiredService<IMemoryStore>().InitializeAsync(),
                sp.GetRequiredService<SqliteRagStore>().InitializeAsync(),
                sp.GetRequiredService<IAgentTaskStateStore>().InitializeAsync(),
                sp.GetRequiredService<BenchmarkService>().InitializeAsync(),
                sp.GetRequiredService<IEvalStore>().InitializeAsync());
            phases.Add(("stores", phaseTimer.ElapsedMilliseconds));

            // Probe active voice provider health at startup to detect externally-running services
            phaseTimer.Restart();
            try
            {
                await vm.Settings.Tts.ProbeActiveProviderHealthAsync();
            }
            catch (Exception ex)
            {
                logs.Add(new RuntimeLogEntry(
                    DateTime.UtcNow,
                    RuntimeLogLevel.Warning,
                    RuntimeLogCategory.Service,
                    $"Voice provider startup probe failed: {ex.Message}"));
            }
            phases.Add(("voice probe", phaseTimer.ElapsedMilliseconds));

            phaseTimer.Restart();
            await vm.InitializeAsync();
            phases.Add(("viewmodels", phaseTimer.ElapsedMilliseconds));

            phases.Add(("total", totalTimer.ElapsedMilliseconds));
            logs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Info,
                RuntimeLogCategory.Startup,
                StartupTimingFormatter.Format(phases)));

            // Warm up the embedding model to prevent "cold-start" delay on
            // first chat, but off the critical path: conversations and
            // panels must not wait behind an ONNX model load that only
            // matters once memory injection is actually used.
            _ = Task.Run(async () =>
            {
                var warmupTimer = Stopwatch.StartNew();
                try
                {
                    var embeddings = sp.GetService<IEmbeddingService>();
                    if (embeddings is not null)
                    {
                        await embeddings.EmbedAsync("warmup", CancellationToken.None);
                        logs.Add(new RuntimeLogEntry(
                            DateTime.UtcNow,
                            RuntimeLogLevel.Info,
                            RuntimeLogCategory.Startup,
                            $"Embedding warm-up completed in {warmupTimer.ElapsedMilliseconds} ms"));
                    }
                }
                catch (Exception ex)
                {
                    logs.Add(new RuntimeLogEntry(
                        DateTime.UtcNow,
                        RuntimeLogLevel.Warning,
                        RuntimeLogCategory.Service,
                        $"Embedding model warm-up failed: {ex.Message}"));
                }

                // Backfill runs off the send path (r9 01-send-path-latency.md
                // 1.2): once here, after the warm-up above, and again after
                // memory writes (MemoryStore.SaveAsync), never inside a chat
                // send.
                try
                {
                    var memoryStore = sp.GetService<IMemoryStore>();
                    if (memoryStore is not null)
                        await memoryStore.RunEmbeddingBackfillAsync();
                }
                catch (Exception ex)
                {
                    logs.Add(new RuntimeLogEntry(
                        DateTime.UtcNow,
                        RuntimeLogLevel.Warning,
                        RuntimeLogCategory.Service,
                        $"Startup embedding backfill failed: {ex.Message}"));
                }
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Aether startup initialization failed: {ex}");
        }
    }

    internal static void ConfigureServices(IServiceCollection s)
    {
        s.AddAetherCoreServices();
        s.AddSingleton<ChatViewModel>();
        s.AddSingleton<AgentScenarioSuiteViewModel>();
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
