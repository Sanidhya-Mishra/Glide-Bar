using GlideBar.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlideBar.HardwareMonitor;

/// <summary>DI wiring for the hardware monitoring service.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="HardwareMetricsProvider"/> as the singleton <see cref="IMetricsProvider"/>.
    /// Singleton because it owns a sampling timer and (optionally) the LibreHardwareMonitor handle,
    /// which must have exactly one lifetime across the app.
    /// </summary>
    public static IServiceCollection AddGlideBarHardwareMonitor(this IServiceCollection services)
    {
        services.TryAddSingleton<IMetricsProvider, HardwareMetricsProvider>();
        return services;
    }
}
