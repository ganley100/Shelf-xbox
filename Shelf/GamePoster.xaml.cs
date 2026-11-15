using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Shelf
{
    /// <summary>
    /// A game drawn like a cable guide's sports poster: each team's colours (from ESPN) split on a slant, the teams'
    /// logos large, "AT" (or the score) in a disc on the divider, the league on top and the matchup underneath.
    /// Without logos, the team names stand in for them.
    /// </summary>
    public sealed partial class GamePoster : UserControl
    {
        public GamePoster() { InitializeComponent(); Update(); }

        static DependencyProperty Prop(string name, System.Type t) =>
            DependencyProperty.Register(name, t, typeof(GamePoster), new PropertyMetadata(null, (d, e) => ((GamePoster)d).Update()));

        public static readonly DependencyProperty LeagueProperty = Prop(nameof(League), typeof(string));
        public static readonly DependencyProperty TeamAProperty = Prop(nameof(TeamA), typeof(string));
        public static readonly DependencyProperty TeamBProperty = Prop(nameof(TeamB), typeof(string));
        public static readonly DependencyProperty MiddleProperty = Prop(nameof(Middle), typeof(string));
        public static readonly DependencyProperty FooterProperty = Prop(nameof(Footer), typeof(string));
        public static readonly DependencyProperty LogoAProperty = Prop(nameof(LogoA), typeof(ImageSource));
        public static readonly DependencyProperty LogoBProperty = Prop(nameof(LogoB), typeof(ImageSource));

        public static readonly DependencyProperty ShowLeagueProperty =
            DependencyProperty.Register(nameof(ShowLeague), typeof(bool), typeof(GamePoster), new PropertyMetadata(true, (d, e) => ((GamePoster)d).Update()));
        /// <summary>The league's logo in the middle (newer thumbnails); false keeps "AT" (things saved before it).</summary>
        public bool ShowLeague { get => (bool)GetValue(ShowLeagueProperty); set => SetValue(ShowLeagueProperty, value); }

        public static readonly DependencyProperty LiveProperty =
            DependencyProperty.Register(nameof(Live), typeof(bool), typeof(GamePoster), new PropertyMetadata(false, (d, e) => ((GamePoster)d).Update()));
        /// <summary>On now: a red "LIVE" before the footer.</summary>
        public bool Live { get => (bool)GetValue(LiveProperty); set => SetValue(LiveProperty, value); }

        public string League { get => (string)GetValue(LeagueProperty); set => SetValue(LeagueProperty, value); }
        public string TeamA { get => (string)GetValue(TeamAProperty); set => SetValue(TeamAProperty, value); }
        public string TeamB { get => (string)GetValue(TeamBProperty); set => SetValue(TeamBProperty, value); }
        public string Middle { get => (string)GetValue(MiddleProperty); set => SetValue(MiddleProperty, value); }
        public string Footer { get => (string)GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
        public ImageSource LogoA { get => (ImageSource)GetValue(LogoAProperty); set => SetValue(LogoAProperty, value); }
        public ImageSource LogoB { get => (ImageSource)GetValue(LogoBProperty); set => SetValue(LogoBProperty, value); }

        string loadedLeague; ImageSource leagueLogo;
        async void LoadLeague(string url)
        {
            var img = await Covers.FromBytesAsync(await LiveTv.ImageBytesAsync(url), 200);
            if (url == loadedLeague && img != null) { leagueLogo = img; Update(); }
        }

        void Update()
        {
            if (LeagueText == null) return;
            var a = TeamA ?? ""; var b = TeamB ?? "";
            var colors = TeamColors.Pair(a, b);
            SideA.Fill = new SolidColorBrush(colors.Item1);
            SideB.Fill = new SolidColorBrush(colors.Item2);

            LeagueText.Text = (League ?? "").ToUpperInvariant();
            var middle = (Middle ?? "").Trim();
            MiddleText.Text = middle.Length == 0 ? "AT" : middle.ToUpperInvariant();
            MiddleText.FontSize = middle.Length > 3 ? 20 : 30;   // a score ("24 – 17") is smaller

            // The league's logo in the middle instead of "AT" (not over a score).
            var leagueUrl = ShowLeague ? LeagueLogos.Url(League) : null;
            if (leagueUrl != loadedLeague) { loadedLeague = leagueUrl; leagueLogo = null; if (leagueUrl != null) LoadLeague(leagueUrl); }
            var showLeague = leagueLogo != null && middle.Length <= 3 && b.Length > 0;
            LeagueImage.Source = showLeague ? leagueLogo : null;
            LeagueDisc.Visibility = showLeague ? Visibility.Visible : Visibility.Collapsed;

            var logos = LogoA != null && LogoB != null;
            // (a show with one logo is handled below)
            LogoAImage.Source = logos ? LogoA : null; LogoBImage.Source = logos ? LogoB : null;
            NameAText.Text = logos ? "" : a.ToUpperInvariant(); NameBText.Text = logos ? "" : b.ToUpperInvariant();
            NameAText.FontSize = a.Length > 16 ? 30 : 40; NameBText.FontSize = b.Length > 16 ? 30 : 40;
            // The matchup underneath, when the logos stand for the teams.
            var sep = middle.Length == 0 || middle.Length > 3 ? "at" : middle.ToLowerInvariant();
            TitleText.Text = logos && a.Length > 0 && b.Length > 0 ? $"{a} {sep} {b}" : "";
            // A show (no second team): one colour, its name across the middle.
            var solo = b.Length == 0;
            Divider.Visibility = solo ? Visibility.Collapsed : Visibility.Visible;
            Disc.Visibility = solo || showLeague ? Visibility.Collapsed : Visibility.Visible;
            if (solo)
            {
                // One logo (NFL RedZone's shield): big in the middle, the name under it; else just the name.
                SideB.Fill = SideA.Fill; NameAText.Text = ""; TitleText.Text = "";
                LogoAImage.Source = LogoA;
                LogoAGrid.Margin = new Thickness(0, LogoA != null ? 110 : 80, 0, 0);
                SoloText.VerticalAlignment = LogoA != null ? VerticalAlignment.Bottom : VerticalAlignment.Center;
                SoloText.Margin = LogoA != null ? new Thickness(30, 0, 30, 90) : new Thickness(30, 0, 30, 40);
            }
            else LogoAGrid.Margin = new Thickness(0, 80, 0, 0);
            SoloText.Text = solo ? a : "";
            FooterText.Inlines.Clear();
            if (Live) FooterText.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = "\u25CF LIVE" + ((Footer ?? "").Length > 0 ? "   " : ""), Foreground = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0x4B, 0x4B)) });
            FooterText.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = (Footer ?? "").ToUpperInvariant() });
        }
    }

    /// <summary>League logos (ESPN) for the middle of game thumbnails; null for leagues without one (college).</summary>
    public static class LeagueLogos
    {
        public static string Url(string league)
        {
            var l = (league ?? "").ToLowerInvariant();
            if (l.Contains("college") || l.Contains("ncaa")) return null;
            string Pro(string k) => "https://a.espncdn.com/i/teamlogos/leagues/500/" + k + ".png";
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"\bwnba\b")) return Pro("wnba");
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"\bnfl\b|^football$|redzone")) return Pro("nfl");
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"\bnba\b")) return Pro("nba");
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"\bnhl\b")) return Pro("nhl");
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"\bmlb\b")) return Pro("mlb");
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"\bmls\b")) return Pro("mls");
            if (l.Contains("premier league")) return "https://a.espncdn.com/i/leaguelogos/soccer/500/23.png";
            if (l.Contains("champions league")) return "https://a.espncdn.com/i/leaguelogos/soccer/500/2.png";
            return null;
        }
    }
}
