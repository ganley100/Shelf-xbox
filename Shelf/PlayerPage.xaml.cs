using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    /// <summary>Movies, music, podcasts and audiobooks.</summary>
    public sealed partial class PlayerPage : Page
    {
        System.IO.FileStream stream;
        public PlayerPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var it = (Item)e.Parameter;
            stream = Fs.OpenRead(it.Path);
            var p = new MediaPlayer { Source = MediaSource.CreateFromStream(stream.AsRandomAccessStream(), Library.ContentType(it.Extension)), AutoPlay = true };
            Player.SetMediaPlayer(p);
            Player.Focus(Windows.UI.Xaml.FocusState.Programmatic);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            var p = Player.MediaPlayer;
            Player.SetMediaPlayer(null);
            p?.Dispose();
            stream?.Dispose();
            base.OnNavigatedFrom(e);
        }
    }
}
