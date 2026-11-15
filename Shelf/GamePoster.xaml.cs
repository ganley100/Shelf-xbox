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
            MiddleText.FontSize = middle.Length > 3 ? 19 : 28;   // a score ("24 – 17") is smaller

            var logos = LogoA != null && LogoB != null;
            LogoAImage.Source = logos ? LogoA : null; LogoBImage.Source = logos ? LogoB : null;
            NameAText.Text = logos ? "" : a.ToUpperInvariant(); NameBText.Text = logos ? "" : b.ToUpperInvariant();
            NameAText.FontSize = a.Length > 16 ? 26 : 34; NameBText.FontSize = b.Length > 16 ? 26 : 34;
            // The matchup underneath, when the logos stand for the teams.
            var sep = middle.Length == 0 || middle.Length > 3 ? "at" : middle.ToLowerInvariant();
            TitleText.Text = logos && a.Length > 0 && b.Length > 0 ? $"{a} {sep} {b}" : "";
            FooterText.Inlines.Clear();
            if (Live) FooterText.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = "\u25CF LIVE" + ((Footer ?? "").Length > 0 ? "   " : ""), Foreground = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0x4B, 0x4B)) });
            FooterText.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = (Footer ?? "").ToUpperInvariant() });
        }
    }
}
