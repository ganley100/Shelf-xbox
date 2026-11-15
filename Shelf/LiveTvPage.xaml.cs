using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    /// <summary>One row in the channel list.</summary>
    public sealed class ChannelRow : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public Channel Channel;
        public TvProgram Now;
        public string Number => Channel.Number;
        public string Name => Channel.Name;
        public string NowText => Now == null ? "No guide information" : Now.FullTitle + (Now.IsSports ? "   ·  SPORTS" : "");
        public double Progress => Now == null ? 0 : 100.0 * (LiveTv.Now - Now.Start) / Math.Max(1, Now.End - Now.Start);
        public Visibility ProgressVisibility => Now == null ? Visibility.Collapsed : Visibility.Visible;
        public string CardText => Now?.Title ?? Channel.Name;
        public SolidColorBrush CardColor => new SolidColorBrush(Palette(Now != null && Now.IsSports ? "sports" : Channel.Name));
        /// <summary>The show's picture; for a game, only a real poster made for it (otherwise the drawn team-colour poster).</summary>
        public string ImageUrl => Game != null ? (GamePicture ? Pictures.Wide(Now.Image) : "")
            : !string.IsNullOrEmpty(Now?.Image) ? Pictures.Wide(Now.Image) : Channel.Logo;

        // ---- games: matchup card ----
        public Tuple<string, string, string> Game => Now != null && Now.IsSports ? (LiveSports.Teams(Now.Subtitle) ?? LiveSports.Teams(Now.Title)) : null;
        /// <summary>A real wide picture of this game (ESPN's photo or preview, Sky's, Gracenote's); else the drawn one.</summary>
        bool GamePicture => Now != null && Pictures.IsWide(Pictures.Wide(Now.Image)) && !Pictures.IsGenericSportsArt(Now.Image) &&
            (Pictures.IsGamePoster(Pictures.Wide(Now.Image)) || Now.Image.Contains("espncdn.com") || Now.Image.Contains("espnmedia") || Now.Image.Contains("thesportsdb.com"));
        public Visibility MatchupVisibility => Game != null && !GamePicture ? Visibility.Visible : Visibility.Collapsed;
        public string TeamA => Game?.Item1 ?? "";
        public string TeamB => Game?.Item3 ?? "";
        public string SepUpper => (Game?.Item2 ?? "vs").ToUpperInvariant();
        public string League => (Game != null && LiveSports.Teams(Now.Subtitle) != null ? Now.Title : "LIVE SPORTS").ToUpperInvariant();
        public string ChannelLabel => ((Channel.Number.Length > 0 ? Channel.Number + "  " : "") + Channel.Name).ToUpperInvariant();
        BitmapImage logoA, logoB;
        public BitmapImage LogoA { get => logoA; set { logoA = value; Notify(nameof(LogoA)); Notify(nameof(LogosVisibility)); Notify(nameof(NamesVisibility)); } }
        public BitmapImage LogoB { get => logoB; set { logoB = value; Notify(nameof(LogoB)); Notify(nameof(LogosVisibility)); Notify(nameof(NamesVisibility)); } }
        public Visibility LogosVisibility => logoA != null && logoB != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NamesVisibility => logoA != null && logoB != null ? Visibility.Collapsed : Visibility.Visible;
        void Notify(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));

        /// <summary>Team logos already known from TheSportsDB (looked up in the background after each guide load).</summary>
        public async Task LoadLogosAsync()
        {
            var g = Game;
            if (g == null || (logoA != null && logoB != null)) return;
            var a = TeamLogos.LogoUrl(g.Item1); var b = TeamLogos.LogoUrl(g.Item3);
            if (a == null || b == null) return;
            var la = await LoadAsync(Pictures.DarkLogo(a), 256); var lb = await LoadAsync(Pictures.DarkLogo(b), 256);
            if (la != null && lb != null) { LogoA = la; LogoB = lb; }
        }
        BitmapImage thumb;
        public BitmapImage Thumb
        {
            get => thumb;
            set { thumb = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Thumb))); }
        }

        public static Color Palette(string s)
        {
            if (s == "sports") return Color.FromArgb(255, 0x1E, 0x56, 0x31);
            var colors = new[] { Color.FromArgb(255, 0x1F, 0x3B, 0x57), Color.FromArgb(255, 0x4A, 0x2C, 0x55), Color.FromArgb(255, 0x5A, 0x3A, 0x1E), Color.FromArgb(255, 0x23, 0x3F, 0x44), Color.FromArgb(255, 0x4D, 0x24, 0x24) };
            unchecked { int h = 0; foreach (var c in s ?? "") h = h * 31 + c; return colors[Math.Abs(h % colors.Length)]; }
        }

        /// <summary>A show's picture as a wide thumbnail: a portrait picture (a poster) is never shown; null instead.</summary>
        public static async Task<BitmapImage> LoadThumbAsync(string url, int width)
        {
            var b = await LoadAsync(url, width);
            return Covers.IsPortrait(b) ? null : b;
        }

        public static async Task<BitmapImage> LoadAsync(string url, int width)
        {
            var bytes = await LiveTv.ImageBytesAsync(url);
            if (bytes == null) return null;
            try
            {
                var b = new BitmapImage { DecodePixelWidth = width };
                using (var ms = new InMemoryRandomAccessStream())
                {
                    await ms.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes));
                    ms.Seek(0);
                    await b.SetSourceAsync(ms);
                }
                return b;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Channels with what's on now: a big panel for the highlighted channel (picture, time, description, next),
    /// filters for channels with guide info and for sports, and Find channels. A opens the channel.
    /// </summary>
    public sealed partial class LiveTvPage : Page
    {
        List<ChannelRow> rows = new List<ChannelRow>();
        string filter = "all";
        DispatcherTimer tick;

        public LiveTvPage() { InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Required; }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await LiveTv.InitAsync();
            LiveTv.Changed -= OnChanged; LiveTv.Changed += OnChanged;
            TeamLogos.Changed -= OnChanged; TeamLogos.Changed += OnChanged;
            Fill();
            // Stale (or nothing yet)? Reload in the background; the list updates as it goes.
            if (LiveTv.HasSources && (LiveTv.Channels.Count == 0 || LiveTv.Now - LiveTv.LastRefresh > 4 * 3_600_000L)) LiveTv.RefreshInBackground();
            if (tick == null) { tick = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) }; tick.Tick += (s, a) => Fill(); }
            tick.Start();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            tick?.Stop();
            LiveTv.Changed -= OnChanged;
            TeamLogos.Changed -= OnChanged;
            base.OnNavigatedFrom(e);
        }

        void OnChanged() { var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, Fill); }

        void Fill()
        {
            var all = LiveTv.Channels.Select(c => new ChannelRow { Channel = c, Now = LiveTv.NowPlaying(c.Id) }).ToList();
            IEnumerable<ChannelRow> shown = all;
            if (filter == "guide") shown = all.Where(r => r.Now != null);
            if (filter == "sports")
            {
                var soon = LiveTv.Now + 3 * 3_600_000L;
                shown = all.Where(r => r.Channel.IsSportsChannel || LiveTv.Upcoming(r.Channel.Id).Any(p => p.Start < soon && p.IsSports));
            }
            // Channels with guide information first.
            rows = shown.OrderBy(r => r.Now == null ? 1 : 0).ToList();
            var focusedId = (FocusManager.GetFocusedElement() as ListViewItem)?.Content is ChannelRow fr ? fr.Channel.Id : null;
            ChannelList.ItemsSource = rows;
            Count.Text = (LiveTv.Status.Length > 0 ? LiveTv.Status + "  ·  " : "") +
                         $"{rows.Count} channels" + (LiveTv.LastError.Length > 0 && LiveTv.Status.Length == 0 ? "  ·  " + LiveTv.LastError : "");
            Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Empty.Text = !LiveTv.HasSources
                ? "No channels yet. On your phone, open the address shown on Shelf's home screen and add a channel playlist (for example the free US list: " +
                  LiveTv.UsPlaylist + " or the UK list: " + LiveTv.UkPlaylist + ") or your HDHomeRun tuner's address. Or press Find channels to look for a tuner."
                : LiveTv.Busy ? "Loading channels…" : "No channels match this filter.";
            var keep = rows.FirstOrDefault(r => r.Channel.Id == focusedId) ?? rows.FirstOrDefault();
            if (keep != null) ShowHero(keep);
        }

        async void ShowHero(ChannelRow r)
        {
            var c = r.Channel; var p = r.Now;
            HeroChannel.Text = (c.Number.Length > 0 ? c.Number + "  " : "") + c.Name;
            HeroTitle.Text = p?.Title ?? c.Name;
            HeroCardTitle.Text = p?.Title ?? c.Name;
            if (p != null)
            {
                var left = (int)Math.Max(0, (p.End - LiveTv.Now) / 60000);
                HeroTime.Text = $"{Local(p.Start):h:mm}–{Local(p.End):h:mm tt}  ·  {left} min left" + (p.Extra.Length > 0 ? "  ·  " + p.Extra : "");
                HeroProgress.Value = r.Progress; HeroProgress.Visibility = Visibility.Visible;
                HeroDetail.Text = p.Detail;
                var next = LiveTv.Upcoming(c.Id).FirstOrDefault(x => x.Start >= p.End);
                HeroNext.Text = next == null ? "" : $"Next  {Local(next.Start):h:mm tt}  {next.FullTitle}";
            }
            else
            {
                HeroTime.Text = "No guide information for this channel"; HeroProgress.Visibility = Visibility.Collapsed;
                HeroDetail.Text = c.Group; HeroNext.Text = "";
            }
            HeroImage.Source = null; HeroImageBack.Source = null;
            HeroMatchup.Visibility = r.MatchupVisibility;
            HeroMatchupContent.Content = r;
            if (r.Game != null) await r.LoadLogosAsync();
            var url = r.ImageUrl;
            var img = await ChannelRow.LoadThumbAsync(url, 640);
            if (img != null && (HeroChannel.Text.EndsWith(c.Name))) { HeroImage.Source = img; HeroImageBack.Source = img; }
        }

        static DateTime Local(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

        async void List_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue || !(args.Item is ChannelRow r)) return;
            if (r.Game != null) await r.LoadLogosAsync();
            if (r.Thumb == null && r.ImageUrl.Length > 0) r.Thumb = await ChannelRow.LoadThumbAsync(r.ImageUrl, 320);
        }

        void List_GotFocus(object sender, RoutedEventArgs e)
        {
            if ((e.OriginalSource as ListViewItem)?.Content is ChannelRow r) ShowHero(r);
        }

        void List_ItemClick(object sender, ItemClickEventArgs e)
        {
            var r = (ChannelRow)e.ClickedItem;
            Frame.Navigate(typeof(LivePlayerPage), Tuple.Create(r.Channel.Id, rows.Select(x => x.Channel.Id).ToList()));
        }

        void Filter_Click(object sender, RoutedEventArgs e)
        {
            filter = sender == FilterGuide ? "guide" : sender == FilterSports ? "sports" : "all";
            FilterAll.IsChecked = filter == "all"; FilterGuide.IsChecked = filter == "guide"; FilterSports.IsChecked = filter == "sports";
            Fill();
        }

        void Find_Click(object sender, RoutedEventArgs e)
        {
            LiveTv.RefreshInBackground(discover: true, freshGuides: true);
            Count.Text = "Looking for tuners and channels, and getting the latest guide…";
        }
    }
}
