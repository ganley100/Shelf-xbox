using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Shelf
{
    /// <summary>
    /// What Y offers when you record: until the game ends (when ESPN knows the game on that channel), until the
    /// show ends, or a set time.
    /// </summary>
    public static class RecordOptions
    {
        /// <summary>While the menu is open, Y and the D-pad belong to it.</summary>
        public static bool Showing { get; private set; }
        /// <summary>The game this channel is showing now, from ESPN (null if none or not known).</summary>
        public static async Task<EspnGame> GameOnNowAsync(Channel c)
        {
            try
            {
                var now = LiveTv.Now;
                var games = await Espn.GamesAsync();
                var g = games.FirstOrDefault(x => x.State != "post" && x.Channels.Any(ch => ch.Id == c.Id) && now >= x.Start - 30 * 60_000L && now <= x.Start + 6 * 3_600_000L);
                return g ?? await Espn.GameOnAsync(c.Name, now, null);
            }
            catch { return null; }
        }

        /// <summary>Asks how long, then starts. Returns a message to show, or null if cancelled.</summary>
        public static async Task<string> ShowAsync(Channel c, TvProgram now)
        {
            if (Recorder.IsRecording(c.Id)) { Recorder.Stop(c.Id); return "Recording stopped and saved to your library."; }
            var game = await GameOnNowAsync(c);
            if (game != null && game.State == "post") game = null;

            int choice = -1;
            var dialog = new ContentDialog { Title = "Record " + (game != null ? game.Away + " at " + game.Home : now?.FullTitle ?? c.Name), CloseButtonText = "Cancel" };
            var panel = new StackPanel { Spacing = 10, MinWidth = 520 };
            var options = new System.Collections.Generic.List<(string Text, int Value)>();
            if (game != null) options.Add(("Until the game ends (follows ESPN, overtime included)", -2));
            if (now != null && now.End > LiveTv.Now)
                options.Add(($"Until the show ends ({DateTimeOffset.FromUnixTimeMilliseconds(now.End).LocalDateTime:h:mm tt})", 0));
            options.Add(("30 minutes", 30)); options.Add(("1 hour", 60)); options.Add(("2 hours", 120)); options.Add(("3 hours", 180));
            Button first = null;
            foreach (var (text, value) in options)
            {
                var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 20, Padding = new Thickness(16, 10, 16, 10) };
                b.Click += (s, a) => { choice = value; dialog.Hide(); };
                panel.Children.Add(b);
                if (first == null) first = b;
            }
            dialog.Content = panel;
            dialog.Opened += (s, a) => first?.Focus(FocusState.Programmatic);
            Showing = true;
            try { await dialog.ShowAsync(); } finally { Showing = false; }
            if (choice == -1) return null;

            var why = choice == -2
                ? await Recorder.StartAsync(c, now, 0, game)
                : await Recorder.StartAsync(c, now, choice);
            return why ?? (choice == -2 ? "Recording until the game ends. It'll be in your library under Recorded Sports."
                                        : "Recording. It'll be in your library under Recorded.");
        }
    }
}
