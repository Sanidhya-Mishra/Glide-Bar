using GlideBar.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlideBar.Media;

/// <summary>DI wiring for the media (Now Playing) service.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SystemMediaController"/> as the singleton <see cref="IMediaController"/>.
    /// Singleton because it owns the SMTC session-manager subscription and a refresh timer, which
    /// must have exactly one lifetime across the app.
    /// </summary>
    public static IServiceCollection AddGlideBarMedia(this IServiceCollection services)
    {
        services.TryAddSingleton<IMediaController, SystemMediaController>();
        return services;
    }
}
