using System;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    /// <summary>
    /// Movies, music, podcasts and audiobooks. MKV and MP4 (4K HEVC needs Microsoft's HEVC Video Extensions).
    /// Remembers where you stopped and picks up from there.
    /// </summary>
    public sealed partial class PlayerPage : Page
    {
        FileStream stream;
        Item item;
        MediaPlayer player;
        DispatcherTimer saver, hideNote;
        double resumeAt;

        public PlayerPage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            item = (Item)e.Parameter;
            Message.Text = "";
            resumeAt = await Progress.GetAsync(item);
            try { stream = Fs.OpenRead(item.Path); }
            catch (Exception ex) { Message.Text = "Couldn't open this file: " + ex.Message; return; }
            player = new MediaPlayer { Source = MediaSource.CreateFromStream(stream.AsRandomAccessStream(), Library.ContentType(item.Extension)), AutoPlay = true };
            player.MediaOpened += (s, a) => { var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Resume); };
            player.MediaFailed += (s, a) => { var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Failed(a)); };
            Player.SetMediaPlayer(player);
            Player.Focus(FocusState.Programmatic);
            saver = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            saver.Tick += (s, a) => SavePosition();
            saver.Start();
        }

        /// <summary>Pick up where you stopped (not in the first 30 seconds or the last few minutes).</summary>
        void Resume()
        {
            var length = player?.PlaybackSession.NaturalDuration.TotalSeconds ?? 0;
            if (resumeAt > 30 && (length <= 0 || resumeAt < length - 120))
            {
                player.PlaybackSession.Position = TimeSpan.FromSeconds(resumeAt);
                ResumeText.Text = "Resuming at " + TimeSpan.FromSeconds(resumeAt).ToString(resumeAt >= 3600 ? @"h\:mm\:ss" : @"m\:ss") + "   ·   press ← on the timeline to start over";
                ResumeNote.Visibility = Visibility.Visible;
                hideNote = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                hideNote.Tick += (s, a) => { ResumeNote.Visibility = Visibility.Collapsed; hideNote.Stop(); };
                hideNote.Start();
            }
        }

        void Failed(MediaPlayerFailedEventArgs a)
        {
            var hevc = item.Extension == ".mkv" || item.Extension == ".mp4" || item.Extension == ".m4v" || item.Extension == ".ts";
            Message.Text = "This video didn't play (" + a.Error + ")." +
                (hevc ? "\n\nIf it's 4K / HDR / HEVC (H.265), install Microsoft's free \"HEVC Video Extensions from Device Manufacturer\" from the Microsoft Store on the Xbox, then try again." : "");
        }

        void SavePosition()
        {
            if (player == null) return;
            var pos = player.PlaybackSession.Position.TotalSeconds;
            var length = player.PlaybackSession.NaturalDuration.TotalSeconds;
            if (pos <= 0) return;
            // Finished (last few minutes, e.g. credits): next time starts from the beginning.
            var _ = Progress.SetAsync(item, length > 0 && pos > length - 180 ? 0 : pos);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            saver?.Stop(); hideNote?.Stop();
            SavePosition();
            Player.SetMediaPlayer(null);
            player?.Dispose(); player = null;
            stream?.Dispose();
            base.OnNavigatedFrom(e);
        }
    }
}
