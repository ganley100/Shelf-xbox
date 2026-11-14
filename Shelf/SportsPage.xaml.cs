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
            await LiveTv.InitAsync();
            guideCards = SportsDb.FromGuide();
            GuideGrid.ItemsSource = guideCards;
            GuideEmpty.Visibility = guideCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            GuideEmpty.Text = LiveTv.HasSources
                ? "No games in your guide right now or in the next 12 hours."
                : "Add channels in Live TV first (antenna or playlists): their games show up here.";
            if (guideCards.Count > 0)
            {
                var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => (GuideGrid.ContainerFromIndex(0) as Control)?.Focus(FocusState.Programmatic));
            }

            DbNote.Text = "Loading today's schedule…";
            dbCards = await SportsDb.TodayAsync();
            foreach (var c in dbCards) SportsDb.LinkToGuide(c);
            DbGrid.ItemsSource = dbCards;
            DbNote.Text = (dbCards.Count == 0 ? "No games found for today in the NFL, college football, NBA, college basketball, NHL, MLB, WNBA, MLS or Premier League." : "") +
                (SportsDb.HasOwnKey ? "" : (dbCards.Count == 0 ? " " : "") + "Free TheSportsDB access shows only a few games per sport. Supporters can enter their own key on the phone page for every game.");
            if (guideCards.Count == 0 && dbCards.Count > 0)
            {
                var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => (DbGrid.ContainerFromIndex(0) as Control)?.Focus(FocusState.Programmatic));
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown -= OnKey;
            base.OnNavigatedFrom(e);
        }

        async void Card_Changing(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (!args.InRecycleQueue && args.Item is SportsCard c) await c.LoadLogosAsync();
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
            if (c.Channel == null)
            {
                await new MessageDialog("None of your channels shows this game.", $"{c.TeamA} {c.MiddleText.ToLower()} {c.TeamB}").ShowAsync();
                return;
            }
            var order = guideCards.Concat(dbCards).Where(x => x.Channel != null).Select(x => x.Channel.Id).Distinct().ToList();
            Frame.Navigate(typeof(LivePlayerPage), Tuple.Create(c.Channel.Id, order));
        }

        async void OnKey(CoreWindow sender, KeyEventArgs e)
        {
            if (e.VirtualKey != VirtualKey.GamepadY || focused == null || Frame?.Content != this) return;
            e.Handled = true;
            var c = focused;
            string msg;
            if (c.Channel == null) msg = "None of your channels shows this game.";
            else if (!c.LiveNow) msg = "This game hasn't started yet. Record it once it's on.";
            else if (Recorder.IsRecording(c.Channel.Id)) msg = "Already recording.";
            else msg = await Recorder.StartAsync(c.Channel, c.Program) ?? "Recording. It'll be in your library under Recorded.";
            await new MessageDialog(msg, $"{c.TeamA} {c.MiddleText.ToLower()} {c.TeamB}").ShowAsync();
        }
    }
}
