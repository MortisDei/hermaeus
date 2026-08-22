using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hermaeus.Agent.Services;
using Hermaeus.Composition;
using Hermaeus.Desktop.Controls;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Desktop.Views;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Hermaeus.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;
    private DesktopIntegrationService? _desktopIntegration;
    // r19 1.4: Avalonia raises Window.Opened every time the window is shown,
    // and DesktopIntegrationService re-shows it on every tray restore, which
    // used to re-run the entire InitializeAppAsync (including
    // AppLifecycleJournalService.RecordStartup) on every restore. That made
    // RecordStartup install the CURRENT session, mid-run, as PreviousSession -
    // the root cause of a false "did not shut down cleanly" warning naming
    // whatever startup breadcrumb happened to be last.
    private int _initialized;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        UiThreadGuard.Arm();
        // Replaces Avalonia's ToolTipService for every window in the process;
        // see Controls/OverlayToolTip.cs for why.
        OverlayToolTip.Install();
        // Keeps the cursor a hand across the gaps in a row of icon buttons;
        // see Controls/IconBarCursor.cs for why those gaps flicker.
        IconBarCursor.Install();
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
            window.Opened += async (_, _) =>
            {
                if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
                await InitializeAppAsync(sp, vm);
            };
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
        var phases = new List<StartupPhase>();
        IRuntimeLogService? logs = null;
        try
        {
            logs = sp.GetRequiredService<IRuntimeLogService>();

            var phaseTimer = Stopwatch.StartNew();
            var settingsService = sp.GetRequiredService<ISettingsService>();
            await settingsService.LoadAsync();
            var ui = settingsService.Settings.Ui;
            AppFontService.Apply(ui.HeadingFontFamily, ui.BodyFontFamily, ui.MonoFontFamily, ui.FontSize);
            AppThemeService.Apply(ui.Theme);
            sp.GetRequiredService<AppLifecycleJournalService>().RecordStartup();
            // Constructed purely for its side effect: subscribes to toasts and
            // forwards Warning/Error ones onto the Notification voice channel.
            sp.GetRequiredService<VoiceNotificationBridge>();
            phases.Add(new StartupPhase("settings", phaseTimer.ElapsedMilliseconds));

            phaseTimer.Restart();
            await Task.WhenAll(
                sp.GetRequiredService<IConversationStore>().InitializeAsync(),
                sp.GetRequiredService<IMemoryStore>().InitializeAsync(),
                sp.GetRequiredService<SqliteRagStore>().InitializeAsync(),
                sp.GetRequiredService<IAgentTaskStateStore>().InitializeAsync(),
                sp.GetRequiredService<BenchmarkService>().InitializeAsync(),
                sp.GetRequiredService<IEvalStore>().InitializeAsync());
            phases.Add(new StartupPhase("stores", phaseTimer.ElapsedMilliseconds));

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
            phases.Add(new StartupPhase("voice probe", phaseTimer.ElapsedMilliseconds));

            phaseTimer.Restart();
            await vm.InitializeAsync();
            // r27 01 1.5: "viewmodels" used to absorb the whole post-setup chain
            // including a five-minute-capable server wait. It now carries its own
            // breakdown, and auto-start is no longer inside it at all.
            phases.Add(new StartupPhase("viewmodels", phaseTimer.ElapsedMilliseconds, vm.StartupPhases));

            var totalMs = totalTimer.ElapsedMilliseconds;
            phases.Add(new StartupPhase("total", totalMs));
            logs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Info,
                RuntimeLogCategory.Startup,
                StartupTimingFormatter.Format(phases)));
            sp.GetRequiredService<IStartupTimingService>()
                .Record(new StartupBreakdown(DateTime.UtcNow, phases, totalMs, []));

            // r19 1.5: brackets the risky startup window tightly so a crash
            // hours later blames "running" (no specific operation in
            // flight) instead of misleadingly naming whatever ONNX loader
            // happened to run at startup.
            sp.GetRequiredService<AppLifecycleJournalService>().RecordOperation("running");

            ScheduleEmbeddingWarmup(sp, vm, settingsService.Settings, logs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Hermaeus startup initialization failed: {ex}");
        }
    }

    /// <summary>
    /// A managed localhost embedding server is expected to refuse connections
    /// until it reaches Running. Wait for that state instead of logging a
    /// misleading startup warning. Remote or otherwise unmanaged endpoints are
    /// probed immediately because no local lifecycle event can make them ready.
    /// </summary>
    internal static void ScheduleEmbeddingWarmup(
        IServiceProvider services,
        MainWindowViewModel vm,
        AppSettings settings,
        IRuntimeLogService logs)
    {
        if (!settings.SetupWizardCompleted)
            return;

        var endpoint = Uri.TryCreate(settings.Rag.EmbeddingBaseUrl, UriKind.Absolute, out var parsed)
            ? parsed
            : null;
        var managed = endpoint is { IsLoopback: true }
            ? vm.Services.Servers.FirstOrDefault(server => server.EmbeddingsMode && server.Port == endpoint.Port)
            : null;

        if (managed is null)
        {
            _ = Task.Run(() => WarmEmbeddingsAndBackfillAsync(services, logs));
            return;
        }

        var scheduled = 0;
        EventHandler? availabilityChanged = null;
        availabilityChanged = (_, _) => TrySchedule();
        void TrySchedule()
        {
            if (!managed.IsRunning || Interlocked.Exchange(ref scheduled, 1) != 0)
                return;

            vm.Services.ServerAvailabilityChanged -= availabilityChanged;
            _ = Task.Run(() => WarmEmbeddingsAndBackfillAsync(services, logs));
        }

        vm.Services.ServerAvailabilityChanged += availabilityChanged;
        TrySchedule();
    }

    private static async Task WarmEmbeddingsAndBackfillAsync(IServiceProvider services, IRuntimeLogService logs)
    {
        var warmupTimer = Stopwatch.StartNew();
        try
        {
            var embeddings = services.GetService<IEmbeddingService>();
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

        try
        {
            var memoryStore = services.GetService<IMemoryStore>();
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
    }

    internal static void ConfigureServices(IServiceCollection s)
    {
        s.AddHermaeusCoreServices();
        s.AddSingleton<ChatViewModel>();
        s.AddSingleton<AgentScenarioSuiteViewModel>();
        s.AddSingleton<AgentViewModel>();
        // Shared between SettingsViewModel (Voice orchestration/channels) and
        // ServicesViewModel (Voice providers card) so both pages see the same live state.
        s.AddSingleton<TtsSettingsViewModel>();
        s.AddSingleton<SttSettingsViewModel>();
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
        s.AddSingleton<ProjectViewModel>();
        s.AddSingleton<PaletteViewModel>();
        s.AddSingleton<ActivityViewModel>();
        s.AddSingleton<MainWindowViewModel>();
    }
}
