using GlideBar.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GlideBar.Storage;

/// <summary>DI registration for the storage / settings / autostart services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IStorage"/> (<see cref="JsonStorage"/>),
    /// <see cref="ISettingsService"/> (<see cref="SettingsService"/>) and
    /// <see cref="IAutostartService"/> (<see cref="AutostartService"/>) as singletons.
    /// </summary>
    public static IServiceCollection AddGlideBarStorage(this IServiceCollection services)
    {
        services.AddSingleton<IStorage, JsonStorage>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAutostartService, AutostartService>();
        return services;
    }
}
