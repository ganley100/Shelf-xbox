using System;
using System.Collections.Generic;
using System.Linq;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    /// <summary>
    /// Sports: every game on your channels now and in the next 12 hours (from the guide), and today's games
    /// and scores from TheSportsDB (marked with your channel when one shows it). A watches, Y records.
    /// </summary>
    public sealed partial class SportsPage : Page
    {
        List<SportsCard> guideCards = new List<SportsCard>(), dbCards = new List<SportsCard>();
        SportsCard focused;

        public SportsPage() { InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Required; }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Window.Current.CoreWindow.KeyDown -= OnKey; Window.Current.CoreWindow.KeyDown += OnKey;
            TeamLogos.Changed -= OnLogos; TeamLogos.Changed += OnLogos;
            await LiveTv.InitAsync();
            guideCards = SportsDb.FromGuide();
            GuideGrid.ItemsSource = guideCards;
            LookUpTeams(guideCards);
            GuideEmpty.Visibility = guideCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            GuideEmpty.Text = LiveTv.HasSources
                ? "No games in your guide right now or in the next 12 hours."
                : "Add channels in Live TV first (antenna or playlists): their games show up here.";
            if (guideCards.Count > 0)
            {
                var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => (GuideGrid.ContainerFromIndex(0) as Control)?.Focus(FocusState.Programmatic));
            }

            DbNote.Text = "Loading today's schedule…";
            // Today's games from ESPN: scores, logos, and the networks showing them (matched to your channels).
            var games = await Espn.GamesAsync();
            var enriched = Espn.Enrich(games);
            dbCards = games.Select(Espn.Card).ToList();
            // Games ESPN gives no network for: try the guide (same sport, same time).
            foreach (var c in dbCards.Where(c => c.Channel == null && !c.Line.Contains("not in your channels"))) enriched |= SportsDb.LinkToGuide(c, dbCards);
            // On your channels first, then the rest by time.
            dbCards = dbCards.OrderBy(c => c.Channel == null ? 1 : 0).ThenBy(c => c.LiveNow ? 0 : 1).ThenBy(c => c.Start).ToList();
            DbGrid.ItemsSource = dbCards;
            if (enriched)
            {
                // A "College Football" listing now names its teams: show it as that game, and keep it in the guide.
                await LiveTv.SaveGuideAsync();
                guideCards = SportsDb.FromGuide();
                GuideGrid.ItemsSource = guideCards;
                LookUpTeams(guideCards);
            }
            LookUpTeams(dbCards.Where(c => c.LogoUrlA == null || c.LogoUrlB == null));
            var onYours = dbCards.Count(c => c.Channel != null);
            DbNote.Text = dbCards.Count == 0
                ? "No games found today (NFL, college football, NBA, WNBA, college basketball, NHL, MLB, MLS, Premier League)."
                : $"{dbCards.Count} games today, {onYours} on your channels. Scores and TV networks from ESPN.";
            if (guideCards.Count == 0 && dbCards.Count > 0)
            {
                var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => (DbGrid.ContainerFromIndex(0) as Control)?.Focus(FocusState.Programmatic));
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown -= OnKey;
            TeamLogos.Changed -= OnLogos;
            base.OnNavigatedFrom(e);
        }

        /// <summary>Team logos for the cards on screen are looked up first, ahead of the rest of the guide.</summary>
        static void LookUpTeams(IEnumerable<SportsCard> cards) =>
            TeamLogos.LookUpNow(cards.Where(c => c.TeamB.Length > 0).SelectMany(c => new[] { (c.TeamA, c.League), (c.TeamB, c.League) }));

        /// <summary>A logo arrived: cards still without logos try again.</summary>
        void OnLogos()
        {
            var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
            {
                foreach (var c in guideCards.Concat(dbCards).ToList()) await c.LoadArtAsync();
            });
        }

        async void Card_Changing(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (!args.InRecycleQueue && args.Item is SportsCard c) await c.LoadArtAsync();
        }

        void Card_GotFocus(object sender, RoutedEventArgs e)
        {
            if ((e.OriginalSource as GridViewItem)?.Content is SportsCard c)
            {
                if (focused != null && focused != c) focused.Focused = false;
                focused = c; c.Focused = true;
                (e.OriginalSource as GridViewItem)?.StartBringIntoView();
            }
        }

        void Card_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((e.OriginalSource as GridViewItem)?.Content is SportsCard c) c.Focused = false;
        }

        async void Card_Click(object sender, ItemClickEventArgs e)
        {
            var c = (SportsCard)e.ClickedItem;
            if (c.Channel == null && c.Maybe != null && c.Maybe.Count > 0)
            {
                // Several channels show this sport at this time: pick one.
                var pick = await PickChannelAsync(c);
                if (pick == null) return;
                var list = c.Maybe.Select(x => x.Id).ToList();
                Frame.Navigate(typeof(LivePlayerPage), Tuple.Create(pick.Id, list));
                return;
            }
            if (c.Channel == null)
            {
                await new MessageDialog("None of your channels shows this game.", $"{c.TeamA} {c.MiddleText.ToLower()} {c.TeamB}").ShowAsync();
                return;
            }
            var order = guideCards.Concat(dbCards).Where(x => x.Channel != null).Select(x => x.Channel.Id).Distinct().ToList();
            Frame.Navigate(typeof(LivePlayerPage), Tuple.Create(c.Channel.Id, order));
        }

        async System.Threading.Tasks.Task<Channel> PickChannelAsync(SportsCard c)
        {
            Channel chosen = null;
            var dialog = new ContentDialog
            {
                Title = $"{c.TeamA} {c.MiddleText.ToLower()} {c.TeamB}",
                CloseButtonText = "Cancel",
            };
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = "These channels show " + c.League + " at this time. Which one?", TextWrapping = TextWrapping.Wrap, FontSize = 20 });
            for (int i = 0; i < c.Maybe.Count; i++)
            {
                var ch = c.Maybe[i];
                var prog = c.MaybePrograms != null && i < c.MaybePrograms.Count ? c.MaybePrograms[i] : null;
                var b = new Button
                {
                    Content = (ch.Number.Length > 0 ? ch.Number + "  " : "") + ch.Name + (prog != null ? "  ·  " + prog.FullTitle : ""),
                    HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 20, Padding = new Thickness(16, 10, 16, 10),
                };
                b.Click += (s, a) => { chosen = ch; dialog.Hide(); };
                panel.Children.Add(b);
            }
            dialog.Content = new ScrollViewer { Content = panel, MaxHeight = 600 };
            await dialog.ShowAsync();
            return chosen;
        }

        async void OnKey(CoreWindow sender, KeyEventArgs e)
        {
            if (e.VirtualKey != VirtualKey.GamepadY || focused == null || Frame?.Content != this || RecordOptions.Showing) return;
            e.Handled = true;
            var c = focused;
            string msg;
            if (c.Channel == null) msg = "None of your channels shows this game.";
            else if (!c.LiveNow) msg = "This game hasn't started yet. Record it once it's on.";
            else msg = await RecordOptions.ShowAsync(c.Channel, c.Program ?? LiveTv.NowPlaying(c.Channel.Id));
            if (msg == null) return;
            await new MessageDialog(msg, $"{c.TeamA} {c.MiddleText.ToLower()} {c.TeamB}").ShowAsync();
        }
    }
}
