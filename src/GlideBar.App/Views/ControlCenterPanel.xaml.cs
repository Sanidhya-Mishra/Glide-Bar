using System.Windows.Controls;

namespace GlideBar.App.Views;

/// <summary>The Control Center flyout: apps/folders quick line + Now Playing + Notifications.
/// DataContext is the <see cref="ViewModels.BarViewModel"/>; it's hosted in a popup by
/// <see cref="BarWindow"/>.</summary>
public partial class ControlCenterPanel : UserControl
{
    public ControlCenterPanel() => InitializeComponent();
}
