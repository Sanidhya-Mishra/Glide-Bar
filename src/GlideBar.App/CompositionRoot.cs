using GlideBar.App.Services;
using GlideBar.App.ViewModels;
using GlideBar.App.Views;
using GlideBar.HardwareMonitor;
using GlideBar.Search;
using GlideBar.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GlideBar.App;

/// <summary>
/// ======================================================================================
///  COMPOSITION ROOT — the single place where services are wired into DI.
/// ======================================================================================
/// This is the ONE integration seam. Today everything runs on the <c>GlideBar.Core.Fakes</c>
/// so the UI builds and runs standalone. When the real service projects are ready, swap the
/// block marked INTEGRATION below.
/// </summary>
internal static class CompositionRoot
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // -------------------------------------------------------------------------------
        //  INTEGRATION SEAM — real services wired in (dev fakes removed 2026-10-14).
        //
        //  Each extension registers via TryAddSingleton:
        //    AddGlideBarHardwareMonitor -> IMetricsProvider  (Win32/perf counters + LHM temps)
        //    AddGlideBarSearch          -> ISearchProvider (aggregator) + ILauncher (ShellLauncher)
        //    AddGlideBarStorage         -> IStorage + ISettingsService + IAutostartService
        //
        //  To go back to standalone fakes, replace these three calls with the
        //  services.AddSingleton<I..., Fake...>() registrations from GlideBar.Core.Fakes.
        // -------------------------------------------------------------------------------
        services.AddGlideBarHardwareMonitor();
        services.AddGlideBarSearch();
        services.AddGlideBarStorage();
        // -------------------------------------------------------------------------------
        //  END INTEGRATION SEAM
        // -------------------------------------------------------------------------------

        // ---- App-owned UI services (never swapped) ------------------------------------
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<InstalledAppsProvider>();

        // ---- View models --------------------------------------------------------------
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<PinsViewModel>();
        services.AddSingleton<VitalsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AppPickerViewModel>();
        services.AddSingleton<BarViewModel>();

        // ---- Windows ------------------------------------------------------------------
        services.AddSingleton<BarWindow>();

        return services.BuildServiceProvider();
    }
}
