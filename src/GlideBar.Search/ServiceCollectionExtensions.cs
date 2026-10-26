using GlideBar.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlideBar.Search;

/// <summary>DI registration for the GlideBar search + launcher services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISearchProvider"/> (the aggregator: Everything if present, otherwise
    /// Windows Search) and <see cref="ILauncher"/> (<see cref="ShellLauncher"/>) as singletons.
    /// Uses TryAdd so a host that registered its own implementation first wins.
    /// </summary>
    public static IServiceCollection AddGlideBarSearch(this IServiceCollection services)
    {
        services.TryAddSingleton<ILauncher, ShellLauncher>();
        services.TryAddSingleton<ISearchProvider>(_ => new AggregateSearchProvider(
            complete: new EverythingSearchProvider(),           // used alone when installed (spans all drives)
            new WindowsSearchProvider(),                        // indexed system drive (fast)
            new DriveSearchProvider()));                        // every other drive (direct, bounded walk)
        return services;
    }
}
