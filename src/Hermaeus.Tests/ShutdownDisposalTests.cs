using Hermaeus.Composition;
using Hermaeus.Desktop;
using Hermaeus.Mcp;
using Hermaeus.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// docs/review/03-field-follow-ups.md 3.1: ServiceProvider.Dispose() throws
/// for any singleton whose implementation type is IAsyncDisposable but not
/// IDisposable (McpToolBridge hit this on the window-close path). App.axaml.cs
/// disposes via DisposeAsync() instead; this guard makes sure the next
/// async-only service does not reintroduce a sync-dispose crash silently.
/// </summary>
public sealed class ShutdownDisposalTests
{
    /// <summary>
    /// Singleton implementation types that are IAsyncDisposable but not
    /// IDisposable. Add to this list the same commit you register a new one;
    /// its presence here is a signal that App.axaml.cs's dispose path was
    /// deliberately reviewed against it.
    /// </summary>
    private static readonly Type[] KnownAsyncOnlyDisposableSingletons =
    [
        typeof(McpToolBridge),
        typeof(LabExperimentService)
    ];

    private static ServiceCollection BuildFullServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddHermaeusCoreServices();
        App.ConfigureServices(services);
        return services;
    }

    [Fact]
    public void Every_async_only_disposable_singleton_is_documented()
    {
        var offenders = BuildFullServiceCollection()
            .Where(d => d.Lifetime == ServiceLifetime.Singleton && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .Distinct()
            .Where(t => typeof(IAsyncDisposable).IsAssignableFrom(t) && !typeof(IDisposable).IsAssignableFrom(t))
            .Where(t => !KnownAsyncOnlyDisposableSingletons.Contains(t))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Newly registered async-only-disposable singleton(s) are missing from " +
            "KnownAsyncOnlyDisposableSingletons; App.axaml.cs's shutdown dispose path must " +
            $"account for them (async-only Dispose crashes the sync path): {string.Join(", ", offenders.Select(t => t.FullName))}");
    }

    [Fact]
    public async Task Full_service_collection_disposes_asynchronously_without_throwing()
    {
        var provider = BuildFullServiceCollection().BuildServiceProvider();

        await provider.DisposeAsync();
    }

    /// <summary>
    /// r13 caught a real bug this way during implementation: ModelManagementViewModel grew a
    /// required ModelDownloadService constructor parameter, but ModelDownloadService was never
    /// registered in HermaeusServiceRegistration (only ever constructed ad-hoc at call sites), so
    /// the app crashed on startup with "Unable to resolve service for type
    /// ModelDownloadService". BuildServiceProvider() alone does not catch this - it is lazy and
    /// only validates a service's dependency graph when something actually resolves it.
    /// ValidateOnBuild forces that resolution for every registration up front.
    /// </summary>
    [Fact]
    public void Every_registered_services_dependency_graph_resolves()
    {
        var services = BuildFullServiceCollection();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
