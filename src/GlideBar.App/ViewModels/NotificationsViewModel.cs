using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GlideBar.App.ViewModels;

/// <summary>One notification row (app, title, body, time). Scaffolded for the Control Center's
/// Notifications section; not yet fed by a live source.</summary>
public sealed partial class NotificationItemViewModel : ObservableObject
{
    public string AppName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string TimeText { get; init; } = "";
}

/// <summary>
/// The Control Center's Notifications section. Reading the live Windows notification list needs a
/// packaged (MSIX) identity + the <c>userNotificationListener</c> capability + user consent, which an
/// unpackaged WPF app can't obtain — so for now this exposes an explicit "not available" state and an
/// empty collection. The UI binds to it today; wiring a real feed later only changes this class.
/// See <c>docs/design-spec-nowplaying.md</c>.
/// </summary>
public sealed partial class NotificationsViewModel : ObservableObject
{
    public ObservableCollection<NotificationItemViewModel> Items { get; } = new();

    /// <summary>False until live mirroring (MSIX) lands; drives the empty/unsupported state in the UI.</summary>
    public bool IsSupported => false;

    public bool HasItems => Items.Count > 0;

    public string StatusText =>
        "Notification mirroring isn't available yet — it needs a packaged build. Media controls above are live.";

    [RelayCommand]
    private void ClearAll()
    {
        Items.Clear();
        OnPropertyChanged(nameof(HasItems));
    }
}
