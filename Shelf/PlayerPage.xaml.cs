using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    /// <summary>Movies, music, podcasts and audiobooks.</summary>
    public sealed partial class PlayerPage : Page
    {
        public PlayerPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var it = (Item)e.Parameter;
            var p = new MediaPlayer { Source = MediaSource.CreateFromStorageFile(it.File), AutoPlay = true };
            Player.SetMediaPlayer(p);
            Player.Focus(Windows.UI.Xaml.FocusState.Programmatic);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            var p = Player.MediaPlayer;
            Player.SetMediaPlayer(null);
            p?.Dispose();
            base.OnNavigatedFrom(e);
        }
    }
}
