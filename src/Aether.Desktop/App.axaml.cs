using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Aether.Core.Services;
using Aether.Desktop.Views;
using Aether.Rag;
using Aether.Rag.Embeddings;
using Aether.Rag.Pipeline;
using Aether.Rag.Storage;
using Aether.Services;
using Aether.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        var sp = services.BuildServiceProvider();
        Ioc.Default.ConfigureServices(sp);

        await sp.GetRequiredService<ISettingsService>().LoadAsync();
        await sp.GetRequiredService<IConversationStore>().InitializeAsync();
        await sp.GetRequiredService<SqliteRagStore>().InitializeAsync();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm     = sp.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection s)
    {
        s.AddSingleton<ISettingsService,   SettingsService>();
        s.AddSingleton<IConversationStore, ConversationStore>();
        s.AddSingleton<LlamaCppService>();
        s.AddSingleton<OpenAiService>();
        s.AddSingleton<ILlmService,        CompositeLlmService>();
        s.AddSingleton<SqliteRagStore>();
        s.AddSingleton<IEmbeddingService,  LlamaCppEmbeddingService>();
        s.AddSingleton<RagPipeline>();
        s.AddSingleton<RagQueryService>();
        s.AddSingleton<ChatViewModel>();
        s.AddSingleton<SettingsViewModel>();
        s.AddSingleton<ModelManagementViewModel>();
        s.AddSingleton<RagViewModel>();
        s.AddSingleton<ServicesViewModel>();
        s.AddSingleton<MainWindowViewModel>();
    }
}
