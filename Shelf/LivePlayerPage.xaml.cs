using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    /// <summary>Watching a channel. Up/Down change channel, A shows what's on, Y records, B goes back.</summary>
    public sealed partial class LivePlayerPage : Page
    {
        List<string> order;
        int index;
        MediaPlayer player;
        DispatcherTimer hideInfo;
        int tuneCount;

        public LivePlayerPage()
        {
            InitializeComponent();
            hideInfo = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            hideInfo.Tick += (s, e) => { Info.Visibility = Visibility.Collapsed; hideInfo.Stop(); };
            Recorder.Changed += () => { var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, ShowRec); };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var arg = (Tuple<string, List<string>>)e.Parameter;
            order = arg.Item2;
            index = Math.Max(0, order.IndexOf(arg.Item1));
            player = new MediaPlayer { AutoPlay = true, RealTimePlayback = true };
            player.MediaFailed += (s, a) => { var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Message.Text = "This channel isn't working right now (" + a.Error + "). Try another one."); };
            Player.SetMediaPlayer(player);
            Window.Current.CoreWindow.KeyDown += OnKey;
            Tune();
            ShowRec();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown -= OnKey;
            Player.SetMediaPlayer(null);
            player?.Dispose();
            player = null;
            base.OnNavigatedFrom(e);
        }

        Channel Current => LiveTv.Channels.FirstOrDefault(c => c.Id == order[index]);

        async void Tune()
        {
            var c = Current;
            if (c == null) return;
            var my = ++tuneCount;
            Message.Text = "";
            ShowInfo();
            MediaSource source = null;
            try
            {
                if (c.Url.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0 || c.Url.Contains("jmp2.uk"))
                {
                    var r = await AdaptiveMediaSource.CreateFromUriAsync(new Uri(c.Url));
                    if (r.Status == AdaptiveMediaSourceCreationStatus.Success) source = MediaSource.CreateFromAdaptiveMediaSource(r.MediaSource);
                }
            }
            catch { }
            if (my != tuneCount || player == null) return; // changed channel meanwhile
            player.Source = source ?? MediaSource.CreateFromUri(new Uri(c.Url));
        }

        void ShowInfo()
        {
            var c = Current; if (c == null) return;
            var p = LiveTv.NowPlaying(c.Id);
            InfoChannel.Text = (c.Number.Length > 0 ? c.Number + "  " : "") + c.Name;
            InfoTitle.Text = p?.FullTitle ?? "No guide information";
            InfoTime.Text = p == null ? "" :
                $"{DateTimeOffset.FromUnixTimeMilliseconds(p.Start).LocalDateTime:h:mm}–{DateTimeOffset.FromUnixTimeMilliseconds(p.End).LocalDateTime:h:mm tt}  ·  {Math.Max(0, (p.End - LiveTv.Now) / 60000)} min left" +
                (p.Detail.Length > 0 ? "\n" + p.Detail : "");
            Info.Visibility = Visibility.Visible;
            hideInfo.Stop(); hideInfo.Start();
        }

        void ShowRec()
        {
            var c = Current;
            var on = c != null && Recorder.IsRecording(c.Id);
            RecBadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            RecText.Text = on ? "● REC  " + Recorder.Elapsed(c.Id) : "";
        }

        async void OnKey(CoreWindow sender, KeyEventArgs e)
        {
            switch (e.VirtualKey)
            {
                case VirtualKey.GamepadDPadUp: case VirtualKey.GamepadLeftThumbstickUp: case VirtualKey.Up: case VirtualKey.PageUp:
                    e.Handled = true; index = (index + order.Count - 1) % order.Count; Tune(); ShowRec(); break;
                case VirtualKey.GamepadDPadDown: case VirtualKey.GamepadLeftThumbstickDown: case VirtualKey.Down: case VirtualKey.PageDown:
                    e.Handled = true; index = (index + 1) % order.Count; Tune(); ShowRec(); break;
                case VirtualKey.GamepadA: case VirtualKey.Enter:
                    e.Handled = true; ShowInfo(); break;
                case VirtualKey.GamepadY: case VirtualKey.R:
                    e.Handled = true;
                    var c = Current; if (c == null) return;
                    if (Recorder.IsRecording(c.Id)) { Recorder.Stop(c.Id); Message.Text = "Recording saved to Recordings. It's in your library under Movies."; }
                    else
                    {
                        Message.Text = "Starting the recording…";
                        var why = await Recorder.StartAsync(c, LiveTv.NowPlaying(c.Id));
                        Message.Text = why ?? "";
                    }
                    ShowRec();
                    break;
            }
        }
    }
}
