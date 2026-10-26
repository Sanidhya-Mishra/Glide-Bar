using System.Windows.Controls;

namespace GlideBar.App.Views;

/// <summary>One Now Playing media card. DataContext is a
/// <see cref="ViewModels.MediaSessionViewModel"/>; all behavior lives there.</summary>
public partial class NowPlayingCard : UserControl
{
    public NowPlayingCard() => InitializeComponent();
}
