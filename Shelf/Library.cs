using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;

namespace Shelf
{
    public enum Kind { Book, Pdf, Text, Comic, Manga, Audiobook, Music, Podcast, Movie, Recording, SportsRecording, Photo, Game, TvShow, SportsEvent }

    public sealed class Item : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public string Title { get; set; }

        // ---- recorded games: shown as matchup cards ----
        public string Away { get; set; } = "";
        public string Home { get; set; } = "";
        public string Sep { get; set; } = "at";
        public string League { get; set; } = "";
        public string LogoUrlA { get; set; }
        public string LogoUrlB { get; set; }
        public string SepUpper => (Sep ?? "at").ToUpperInvariant();
        /// <summary>The newer game thumbnail (league logo in the middle): recordings and games added from now on.</summary>
        public bool LeagueStyle { get; set; }
        /// <summary>A season of a TV show or a sports series (RedZone 2024, Raw 1999): one poster holding its episodes.</summary>
        public bool IsGroup { get; set; }
        public List<Item> Children { get; set; }
        /// <summary>Your own poster file is shown (no edition banner over it).</summary>
        public bool OwnPosterShown { get; set; }
        /// <summary>A movie edition's banner over its poster: DIRECTOR'S CUT, NOIR EDITION…</summary>
        public string EditionBanner => Kind == Kind.Movie && Episode.Length > 0 && Episode != "Theatrical Cut" ? Episode.ToUpperInvariant() : "";
        public Windows.UI.Xaml.Visibility EditionBannerVisibility => EditionBanner.Length > 0 && !OwnPosterShown && cover != null ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
        BitmapImage logoArt;
        /// <summary>A movie's title logo, over the bottom of its thumbnail.</summary>
        public BitmapImage LogoArt { get => logoArt; set { logoArt = value; Raise(nameof(LogoArt)); Raise(nameof(LogoArtVisibility)); } }
        public Windows.UI.Xaml.Visibility LogoArtVisibility => logoArt != null && cover != null ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
        /// <summary>TV shows: the show (Title) and "Season 1 · Episode 2".</summary>
        public string Episode { get; set; } = "";
        public string LeagueUpper => (League ?? "").ToUpperInvariant();
        /// <summary>Under the matchup on a recorded game's poster: the channel it was recorded from.</summary>
        public string PosterFooter => Channel ?? "";
        BitmapImage logoA, logoB;
        public BitmapImage LogoA { get => logoA; set { logoA = value; Raise(nameof(LogoA)); Raise(nameof(LogosVisibility)); Raise(nameof(NamesVisibility)); } }
        public BitmapImage LogoB { get => logoB; set { logoB = value; Raise(nameof(LogoB)); Raise(nameof(LogosVisibility)); Raise(nameof(NamesVisibility)); } }
        void Raise(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
        /// <summary>The drawn matchup card: for recorded games without a TheSportsDB thumbnail.</summary>
        public Windows.UI.Xaml.Visibility MatchupVisibility => (Kind == Kind.SportsRecording || Kind == Kind.SportsEvent) && cover == null ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
        public Windows.UI.Xaml.Visibility LogosVisibility => logoA != null && logoB != null ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
        public Windows.UI.Xaml.Visibility NamesVisibility => logoA != null && logoB != null ? Windows.UI.Xaml.Visibility.Collapsed : Windows.UI.Xaml.Visibility.Visible;
        /// <summary>Recordings: the channel and when it was recorded.</summary>
        public string Channel { get; set; } = "";
        public string When { get; set; } = "";
        /// <summary>Full path, e.g. D:\DevelopmentFiles\Shelf\Books\x.epub.</summary>
        public string Path { get; set; }
        public long Size { get; set; }
        public Kind Kind { get; set; }
        public string Badge { get; set; }
        BitmapImage cover;
        public BitmapImage Cover
        {
            get => cover;
            set
            {
                cover = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Cover)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(TileVisibility)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(BookTileVisibility)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CartridgeVisibility)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(MatchupVisibility)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CoverFillVisibility)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CoverFitVisibility)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(EditionBannerVisibility)));
            }
        }
        /// <summary>
        /// Art that isn't poster-shaped (a wide picture, a channel logo): shown whole in the middle of the poster over a
        /// dimmed copy of itself with the title underneath, instead of being cropped.
        /// </summary>
        bool CoverIsWide => cover != null && cover.PixelHeight > 0 && cover.PixelWidth > cover.PixelHeight * 0.8;
        public Windows.UI.Xaml.Visibility CoverFillVisibility => cover != null && !CoverIsWide ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
        public Windows.UI.Xaml.Visibility CoverFitVisibility => CoverIsWide ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
        /// <summary>The name under a fitted picture: the show (or game) and, for recordings, its channel.</summary>
        public string FitChannel => (Channel ?? "").ToUpperInvariant();
        public string FitTitle => Kind == Kind.SportsRecording || Kind == Kind.SportsEvent ? (Away.Length > 0 && Home.Length > 0 ? $"{Away} {Sep} {Home}" : Title) : Title;
        bool focused;
        /// <summary>Highlighted by the controller: a gold outline, as on Android.</summary>
        public bool Focused
        {
            get => focused;
            set { focused = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(FocusVisibility))); }
        }
        public Windows.UI.Xaml.Visibility FocusVisibility => focused ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;

        /// <summary>The drawn tile shows until (unless) real art arrives.</summary>
        public Windows.UI.Xaml.Visibility TileVisibility => cover == null ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
        public Windows.UI.Xaml.Media.Brush TileBrush
        {
            get
            {
                var c = ChannelRow.Palette(Title);
                var dark = Windows.UI.Color.FromArgb(255, (byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 3));
                var b = new Windows.UI.Xaml.Media.LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
                b.GradientStops.Add(new Windows.UI.Xaml.Media.GradientStop { Color = c, Offset = 0 });
                b.GradientStops.Add(new Windows.UI.Xaml.Media.GradientStop { Color = dark, Offset = 1 });
                return b;
            }
        }
        // ---- Android-style tiles ----
        static Windows.UI.Xaml.Visibility Vis(bool b) => b ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
        public bool IsGame => Kind == Kind.Game;
        /// <summary>The drawn book-style tile (no art, not a game).</summary>
        public Windows.UI.Xaml.Visibility BookTileVisibility => Vis(cover == null && !IsGame && Kind != Kind.SportsRecording && Kind != Kind.SportsEvent);
        /// <summary>The drawn cartridge (games).</summary>
        public Windows.UI.Xaml.Visibility CartridgeVisibility => Vis(cover == null && IsGame);
        /// <summary>Drawn tiles take the shape of the thing: books tall, music square, video wide.</summary>
        public double TileWidth => Kind == Kind.Movie || Kind == Kind.Recording || Kind == Kind.SportsRecording || Kind == Kind.Photo ? 260 : Kind == Kind.Game ? 250 : Kind == Kind.Music || Kind == Kind.Podcast || Kind == Kind.Audiobook ? 240 : 213;
        public double TileHeight => Kind == Kind.Movie || Kind == Kind.Recording || Kind == Kind.SportsRecording || Kind == Kind.Photo ? 146 : Kind == Kind.Game ? 200 : Kind == Kind.Music || Kind == Kind.Podcast || Kind == Kind.Audiobook ? 240 : 320;

        string Ext => System.IO.Path.GetExtension(Path ?? "").ToLowerInvariant();
        Systems.Sys sysInfo;
        Systems.Sys Sys => sysInfo ?? (sysInfo = Systems.For(Path ?? "") ?? new Systems.Sys { Short = "GAME", Name = "Game", Color = 0xFF424242 });
        public string SystemShort => Sys.Short;
        public string SystemName => Sys.Name;
        public string SystemLabel => SystemName.ToUpperInvariant();
        static Windows.UI.Xaml.Media.SolidColorBrush Brush(uint argb) =>
            new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        public Windows.UI.Xaml.Media.SolidColorBrush SystemColor => Brush(Sys.Color);
        /// <summary>Light grey cartridges for the grey consoles, dark for the rest; discs and arcade boards dark too.</summary>
        public Windows.UI.Xaml.Media.SolidColorBrush CartridgeColor =>
            new[] { "NES", "GB", "SNES", "2600" }.Contains(SystemShort) ? Brush(0xFFB9B9BE) : Brush(0xFF3C3C44);

        /// <summary>Corner badge, as on Android: the system for games, else the kind.</summary>
        public string BadgeText => IsGroup ? $"{Children.Count} EPISODES" : IsGame ? SystemShort : Kind == Kind.Recording ? "RECORDED" : Kind == Kind.SportsRecording ? "SPORTS" : Kind == Kind.Movie ? (Episode.Length > 0 ? Episode.ToUpperInvariant() : "MOVIE") : Kind == Kind.TvShow ? "TV" : Kind == Kind.SportsEvent ? (Home.Length == 0 && Episode.StartsWith("Week ") ? Episode.Split('·')[0].Trim().ToUpperInvariant()
                : Episode.Length > 0 && Episode.Length <= 16 && Home.Length == 0 ? Episode.ToUpperInvariant() : "SPORTS") : Badge;

        /// <summary>The line above the covers: "Title · Super Nintendo · Game".</summary>
        public string Detail
        {
            get
            {
                switch (Kind)
                {
                    case Kind.Game: return $"{Title}  ·  {SystemName}  ·  Game";
                    case Kind.SportsRecording:
                        return $"{Away} {Sep} {Home}" + (League.Length > 0 && League != "Sports" ? "  ·  " + League : "") + (Channel.Length > 0 ? "  ·  " + Channel : "") + (When.Length > 0 ? "  ·  Recorded " + When : "");
                    case Kind.Recording:
                        return Title + (Channel.Length > 0 ? "  ·  " + Channel : "") + (When.Length > 0 ? "  ·  Recorded " + When : "  ·  Recorded from Live TV");
                    case Kind.Book: return $"{Title}  ·  Book";
                    case Kind.Text: return $"{Title}  ·  Text";
                    case Kind.Pdf: return $"{Title}  ·  PDF";
                    case Kind.Comic: return $"{Title}  ·  Comic";
                    case Kind.Manga: return $"{Title}  ·  Manga  ·  right to left";
                    case Kind.Audiobook: return $"{Title}  ·  Audiobook";
                    case Kind.Music: return $"{Title}  ·  Music";
                    case Kind.Podcast: return $"{Title}  ·  Podcast";
                    case Kind.Photo: return $"{Title}  ·  Photo";
                    case Kind.TvShow: return Title + (Episode.Length > 0 ? "  ·  " + Episode : "") + "  ·  TV show";
                    case Kind.SportsEvent:
                        return (Away.Length > 0 ? $"{Away} {Sep} {Home}" : Title) + (League.Length > 0 ? "  ·  " + League : "") + (When.Length > 0 ? "  ·  " + When : "") + (Episode.Length > 0 ? "  ·  " + Episode : "");
                    default:
                        var mi = Kind == Kind.Movie ? Movies.Cached(Path) : null;
                        return mi != null ? $"{mi.Title}" + (mi.Year.Length > 0 ? $" ({mi.Year})" : "") + (Episode.Length > 0 ? "  ·  " + Episode : "") + "  ·  Movie" + (mi.Overview.Length > 0 ? "  ·  " + (mi.Overview.Length > 90 ? mi.Overview.Substring(0, 90) + "…" : mi.Overview) : "")
                                          : $"{Title}" + (Episode.Length > 0 ? "  ·  " + Episode : "") + "  ·  Video";
                }
            }
        }

        /// <summary>Segoe MDL2 symbol for the kind of item, on drawn tiles.</summary>
        public string Glyph
        {
            get
            {
                switch (Kind)
                {
                    case Kind.Music: return "\uE8D6";
                    case Kind.Audiobook: return "\uE7F6";
                    case Kind.Podcast: return "\uE720";
                    case Kind.Movie: return "\uE714";
                    case Kind.TvShow: return "\uE7F4";
                    case Kind.Recording: return "\uE7F4";
                    case Kind.SportsRecording: return "\uE7FC";
                    case Kind.Photo: return "\uEB9F";
                    case Kind.Game: return "\uE7FC";
                    case Kind.Pdf: case Kind.Text: return "\uE8A5";
                    case Kind.Comic: case Kind.Manga: return "\uE8B9";
                    default: return "\uE736";
                }
            }
        }
        public string Initials => string.IsNullOrEmpty(Title) ? "?" : Title;
        public DateTimeOffset Added { get; set; }
        public string Extension => System.IO.Path.GetExtension(Path).ToLowerInvariant();
    }

    public static class Library
    {
        static readonly string[] Audio = { ".mp3", ".m4a", ".m4b", ".aac", ".flac", ".ogg", ".opus", ".wav", ".wma" };
        static readonly string[] Video = { ".mp4", ".m4v", ".mkv", ".webm", ".mov", ".avi", ".wmv", ".ts", ".m2ts" };
        static readonly string[] Images = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic", ".jxr" };
        static readonly string[] Roms = { ".gb", ".gbc", ".gba", ".nes", ".sfc", ".smc", ".n64", ".z64", ".v64" };

        /// <summary>Chris Video Library: videos only (movies, TV shows, recordings from Live TV).</summary>
        public static readonly (string Name, Kind[] Kinds)[] Categories =
        {
            ("All", null),
            ("Movies", new[] { Kind.Movie }),
            ("TV Shows", new[] { Kind.TvShow }),
            ("Recorded", new[] { Kind.Recording, Kind.SportsRecording }),
            ("Recorded Sports", new[] { Kind.SportsRecording }),
            ("Sports", new[] { Kind.SportsEvent }),
        };

        /// <summary>The folders the library shows (other folders and their files are left as they are).</summary>
        static readonly string[] VideoFolders = { "Movies", "TV Shows", "Recordings", "Sports" };

        static Kind? KindFor(string folder, string ext)
        {
            ext = ext.ToLowerInvariant();
            if (ext == ".epub") return Kind.Book;
            if (ext == ".txt") return Kind.Text;
            if (ext == ".pdf") return Kind.Pdf;
            if (ext == ".cbz") return folder == "Manga" ? Kind.Manga : Kind.Comic;
            if (Images.Contains(ext)) return Kind.Photo;
            if (Video.Contains(ext)) return Kind.Movie;
            if (Audio.Contains(ext))
                return folder == "Audiobooks" ? Kind.Audiobook : folder == "Podcasts" ? Kind.Podcast : Kind.Music;
            return null;
        }

        public static string BadgeFor(Kind k)
        {
            switch (k)
            {
                case Kind.Book: return "BOOK";
                case Kind.Pdf: return "PDF";
                case Kind.Text: return "TXT";
                case Kind.Comic: return "COMIC";
                case Kind.Manga: return "MANGA";
                case Kind.Audiobook: return "AUDIOBOOK";
                case Kind.Music: return "MUSIC";
                case Kind.Podcast: return "PODCAST";
                case Kind.Movie: return "MOVIE";
                case Kind.TvShow: return "TV";
                case Kind.SportsEvent: return "SPORTS";
                case Kind.Recording: return "RECORDED";
                case Kind.SportsRecording: return "SPORTS";
                case Kind.Photo: return "PHOTO";
                default: return "GAME";
            }
        }

        public static string ContentType(string ext)
        {
            switch (ext)
            {
                case ".mp3": return "audio/mpeg";
                case ".m4a": case ".m4b": return "audio/mp4";
                case ".aac": return "audio/aac";
                case ".flac": return "audio/flac";
                case ".ogg": case ".opus": return "audio/ogg";
                case ".wav": return "audio/wav";
                case ".wma": return "audio/x-ms-wma";
                case ".mkv": return "video/x-matroska";
                case ".webm": return "video/webm";
                case ".mov": return "video/quicktime";
                case ".avi": return "video/x-msvideo";
                case ".wmv": return "video/x-ms-wmv";
                case ".ts": case ".m2ts": return "video/mp2t";
                default: return "video/mp4";
            }
        }

        /// <summary>
        /// Every file in the Shelf folders as its own item (each song, episode, photo, chapter file and part
        /// of an audiobook separately, in subfolders too), newest first.
        /// </summary>
        static bool scraping;
        /// <summary>The library changed in the background (sports events found on TheSportsDB).</summary>
        public static event Action Changed;

        public static Task<List<Item>> ScanAsync() => Task.Run(async () =>
        {
            var items = new List<Item>();
            foreach (var name in VideoFolders)
            {
                foreach (var f in Fs.Walk(Storage.Folder(name)))
                {
                    if (f.Name.StartsWith(".") || f.Name.EndsWith(".part")) continue;
                    Kind? k;
                    if (name == "Games")
                    {
                        // Games: any system Shelf knows (BIOS files are for the emulators, not the library).
                        if (f.Path.IndexOf("\\BIOS\\", StringComparison.OrdinalIgnoreCase) >= 0 || Systems.For(f.Path) == null) continue;
                        k = Kind.Game;
                    }
                    else k = KindFor(name, Path.GetExtension(f.Name));
                    if (k == null) continue;
                    if (k != Kind.Movie) continue; // videos only
                    if (name == "Recordings") k = Kind.Recording;
                    else if (name == "TV Shows") k = Kind.TvShow;
                    else if (name == "Sports") k = Kind.SportsEvent;
                    var title = Path.GetFileNameWithoutExtension(f.Name).Replace('_', ' ');
                    string channel = "", when = "";
                    if (k == Kind.Recording)
                    {
                        // "Channel - Show - 2026-11-11 1300" -> show "Show", with channel and date in the details.
                        var parts = Path.GetFileNameWithoutExtension(f.Name).Split(new[] { " - " }, StringSplitOptions.None);
                        if (parts.Length >= 3 && DateTime.TryParseExact(parts.Last(), "yyyy-MM-dd HHmm", System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.AssumeLocal, out var t))
                        {
                            channel = parts[0];
                            title = string.Join(" - ", parts.Skip(1).Take(parts.Length - 2)).Replace("_ ", ": ").Replace('_', ' ').Trim();
                            when = t.ToString("ddd MMM d, h:mm tt");
                        }
                    }
                    var item = new Item
                    {
                        Title = title, Channel = channel, When = when,
                        Path = f.Path, Size = f.Size, Kind = k.Value, Badge = BadgeFor(k.Value),
                        Added = f.Created > f.Modified ? f.Created : f.Modified,
                    };
                    if (k == Kind.Recording) SportsFromRecording(item);
                    if (k == Kind.TvShow) TvShows.Name(item, Storage.Folder("TV Shows"));
                    if (k == Kind.SportsEvent) SportsEvents.Apply(item);
                    if (k == Kind.Movie)
                    {
                        // The movie's real title (from TMDB, once looked up) rather than the file name.
                        var mi = Movies.Cached(f.Path);
                        item.Episode = Movies.Edition(Path.GetFileNameWithoutExtension(f.Name)); // Director's Cut, Black & White…
                        if (mi != null) item.Title = mi.Title + (mi.Year.Length > 0 ? " (" + mi.Year + ")" : "");
                        else { var (t, y) = Movies.FromFileName(Path.GetFileNameWithoutExtension(f.Name)); if (t.Length > 0) item.Title = t + (y.Length > 0 ? " (" + y + ")" : ""); }
                    }
                    items.Add(item);
                }
            }
            // Recordings of a game whose listing didn't name the teams ("College Football" on TNT): ask ESPN which
            // game that channel was showing, and remember the answer with the recording.
            foreach (var it in items.Where(i => i.Kind == Kind.Recording).ToList())
                try { await SportsFromEspnAsync(it); } catch { }
            // Sports events (older games in the Sports folder): TheSportsDB finds each one from its file name, once,
            // in the background; the library refreshes when they're done.
            var todo = items.Where(i => i.Kind == Kind.SportsEvent && !SportsEvents.Checked(i)).Take(20).ToList();
            if (todo.Count > 0 && !scraping)
            {
                scraping = true;
                var _ = Task.Run(async () =>
                {
                    try { foreach (var it in todo) try { await SportsEvents.LookUpAsync(it); } catch { } }
                    finally { scraping = false; }
                    Changed?.Invoke();
                });
            }
            return items.OrderByDescending(i => i.Added).ToList();
        });

        /// <summary>
        /// A recorded game becomes a Recorded Sports item: its matchup from what was saved when it was recorded,
        /// or, for older recordings, from a title like "College Football: Iowa State at Cincinnati".
        /// </summary>
        static void SportsFromRecording(Item it)
        {
            try
            {
                var metaPath = Recorder.MetaPath(it.Path);
                if (Fs.Exists(metaPath))
                {
                    var m = Windows.Data.Json.JsonObject.Parse(Fs.ReadAllText(metaPath));
                    if (m.GetNamedBoolean("sports", false))
                    {
                        it.Away = m.GetNamedString("away", ""); it.Home = m.GetNamedString("home", "");
                        it.Sep = m.GetNamedString("sep", "at"); it.League = m.GetNamedString("league", "");
                        it.LogoUrlA = m.GetNamedString("awayLogo", ""); it.LogoUrlB = m.GetNamedString("homeLogo", "");
                        it.LeagueStyle = m.GetNamedNumber("style", 0) >= 2;
                        TeamColors.Remember(it.Away, m.GetNamedString("awayColor", ""), m.GetNamedString("awayAlt", ""));
                        TeamColors.Remember(it.Home, m.GetNamedString("homeColor", ""), m.GetNamedString("homeAlt", ""));
                    }
                    else
                    {
                        var ep = m.GetNamedString("episode", ""); var title = m.GetNamedString("title", "");
                        var t = LiveSports.Teams(ep);
                        if (t != null && LiveSports.LooksLikeSports(title, ep)) { it.Away = t.Item1; it.Sep = t.Item2; it.Home = t.Item3; it.League = title; }
                    }
                }
                if (it.Away.Length == 0)
                {
                    var name = it.Title; var colon = name.IndexOf(": ", StringComparison.Ordinal);
                    var t = LiveSports.Teams(colon > 0 ? name.Substring(colon + 2) : name);
                    if (t != null && LiveSports.LooksLikeSports(colon > 0 ? name.Substring(0, colon) : name, name)) { it.Away = t.Item1; it.Sep = t.Item2; it.Home = t.Item3; it.League = colon > 0 ? name.Substring(0, colon) : "Sports"; }
                }
                if (it.Away.Length > 0 && it.Home.Length > 0)
                {
                    it.Kind = Kind.SportsRecording; it.Badge = BadgeFor(Kind.SportsRecording);
                    it.Title = $"{it.Away} {it.Sep} {it.Home}";
                }
            }
            catch { }
        }

        static async Task SportsFromEspnAsync(Item it)
        {
            var metaPath = Recorder.MetaPath(it.Path);
            Windows.Data.Json.JsonObject meta = null;
            try { if (Fs.Exists(metaPath)) meta = Windows.Data.Json.JsonObject.Parse(Fs.ReadAllText(metaPath)); } catch { }
            if (meta != null && meta.ContainsKey("espnChecked")) return; // asked before
            var title = meta?.GetNamedString("title", "") ?? it.Title;
            var sport = SportsDb.SportKey(title);
            if (sport == null) return; // not a game
            var channel = meta?.GetNamedString("channel", "") ?? it.Channel;
            long start = meta != null ? (long)meta.GetNamedNumber("start", 0) : 0;
            if (start == 0 && DateTime.TryParse(it.When, out var w)) start = new DateTimeOffset(w).ToUnixTimeMilliseconds();
            if (start == 0) start = it.Added.ToUnixTimeMilliseconds();
            if (string.IsNullOrEmpty(channel)) return;
            var g = await Espn.GameOnAsync(channel, start, sport);
            if (meta == null) meta = new Windows.Data.Json.JsonObject { ["title"] = Windows.Data.Json.JsonValue.CreateStringValue(title), ["channel"] = Windows.Data.Json.JsonValue.CreateStringValue(channel) };
            meta["espnChecked"] = Windows.Data.Json.JsonValue.CreateBooleanValue(true);
            if (g != null)
            {
                meta["sports"] = Windows.Data.Json.JsonValue.CreateBooleanValue(true);
                meta["league"] = Windows.Data.Json.JsonValue.CreateStringValue(g.League);
                meta["away"] = Windows.Data.Json.JsonValue.CreateStringValue(g.Away);
                meta["sep"] = Windows.Data.Json.JsonValue.CreateStringValue("at");
                meta["home"] = Windows.Data.Json.JsonValue.CreateStringValue(g.Home);
                meta["awayLogo"] = Windows.Data.Json.JsonValue.CreateStringValue(g.AwayLogo ?? "");
                meta["homeLogo"] = Windows.Data.Json.JsonValue.CreateStringValue(g.HomeLogo ?? "");
            }
            try { Fs.WriteAllText(metaPath, meta.Stringify()); } catch { }
            if (g != null) SportsFromRecording(it);
        }

        /// <summary>
        /// A recorded game's thumbnail: saved with it (from TheSportsDB) or looked up once now. Null = none.
        /// </summary>
        static async Task<byte[]> GameThumbAsync(Item it)
        {
            var metaPath = Recorder.MetaPath(it.Path);
            Windows.Data.Json.JsonObject meta = null;
            try { if (Fs.Exists(metaPath)) meta = Windows.Data.Json.JsonObject.Parse(Fs.ReadAllText(metaPath)); } catch { }
            // TheSportsDB's picture of the game (saved with the recording, or looked up once); none = matchup card.
            var thumb = meta?.GetNamedString("thumb", "") ?? "";
            if (thumb.Length == 0 && meta != null && !meta.GetNamedBoolean("thumbChecked", false))
            {
                var day = meta.ContainsKey("start") ? DateTimeOffset.FromUnixTimeMilliseconds((long)meta.GetNamedNumber("start")).LocalDateTime : it.Added.LocalDateTime;
                thumb = await SportsDb.ThumbAsync(it.Away, it.Home, day) ?? "";
                meta["thumbChecked"] = Windows.Data.Json.JsonValue.CreateBooleanValue(true);
                if (thumb.Length > 0) meta["thumb"] = Windows.Data.Json.JsonValue.CreateStringValue(thumb);
                try { Fs.WriteAllText(metaPath, meta.Stringify()); } catch { }
                if (thumb.Length > 0)
                {
                    // Found now: it replaces whatever picture was saved when it was recorded (e.g. the channel logo).
                    var fresh = await LiveTv.ImageBytesAsync(thumb);
                    if (fresh != null) { try { Fs.WriteAllBytes(Recorder.ArtPath(it.Path), fresh); } catch { } return fresh; }
                }
            }
            // Then ESPN's photo from the game (recap / highlights), asked once; none = the matchup card stays.
            if (thumb.Length == 0 && meta != null && !meta.GetNamedBoolean("espnPhotoChecked", false))
            {
                var day = meta.ContainsKey("start") ? DateTimeOffset.FromUnixTimeMilliseconds((long)meta.GetNamedNumber("start")).LocalDateTime : it.Added.LocalDateTime;
                EspnGame eg = null;
                try { eg = await Espn.FindAsync(it.Away, it.Home, day); } catch { }
                // Only once the game is over (or a photo exists): before that, ask again next time.
                if (eg != null) { Espn.ApplyPreviews(new List<EspnGame> { eg }); if (eg.Photo == null) await Espn.FetchPreviewsAsync(new List<EspnGame> { eg }); }
                var espnPic = eg?.Photo ?? (eg != null && eg.State == "post" ? eg.Preview : null);
                if (eg != null && (espnPic != null || eg.State == "post")) meta["espnPhotoChecked"] = Windows.Data.Json.JsonValue.CreateBooleanValue(true);
                if (espnPic != null)
                {
                    thumb = espnPic;
                    meta["thumb"] = Windows.Data.Json.JsonValue.CreateStringValue(thumb);
                    try { Fs.WriteAllText(metaPath, meta.Stringify()); } catch { }
                    var fresh = await LiveTv.ImageBytesAsync(thumb);
                    if (fresh != null) { try { Fs.WriteAllBytes(Recorder.ArtPath(it.Path), fresh); } catch { } return fresh; }
                }
                else try { Fs.WriteAllText(metaPath, meta.Stringify()); } catch { }
            }
            if (thumb.Length == 0) return null;
            var saved = Recorder.ArtPath(it.Path);
            if (Fs.Exists(saved)) return await Task.Run(() => Fs.ReadAllBytes(saved));
            return await LiveTv.ImageBytesAsync(thumb);
        }

        /// <summary>A recorded game's poster from TheSportsDB, saved with it the first time. Null if TheSportsDB has none.</summary>
        static async Task<BitmapImage> SportsPosterAsync(Item it)
        {
            var mp = Recorder.MetaPath(it.Path);
            Windows.Data.Json.JsonObject meta = null;
            try { if (Fs.Exists(mp)) meta = Windows.Data.Json.JsonObject.Parse(Fs.ReadAllText(mp)); } catch { }
            if (meta == null || meta.GetNamedBoolean("sdbPosterChecked", false)) return null;
            var day = meta.ContainsKey("start") ? DateTimeOffset.FromUnixTimeMilliseconds((long)meta.GetNamedNumber("start")).LocalDateTime : it.Added.LocalDateTime;
            string url = null;
            try { url = await SportsDb.ThumbAsync(it.Away, it.Home, day); } catch { }
            meta["sdbPosterChecked"] = Windows.Data.Json.JsonValue.CreateBooleanValue(true);
            byte[] bytes = null;
            if (!string.IsNullOrEmpty(url) && Pictures.IsGamePoster(url))
            {
                bytes = await LiveTv.ImageBytesAsync(url);
                if (bytes != null)
                {
                    try { Fs.WriteAllBytes(Recorder.ArtPath(it.Path), bytes); } catch { }
                    meta["thumb"] = Windows.Data.Json.JsonValue.CreateStringValue(url);
                    meta["posterLocked"] = Windows.Data.Json.JsonValue.CreateBooleanValue(true);
                }
            }
            try { Fs.WriteAllText(mp, meta.Stringify()); } catch { }
            return bytes == null ? null : await Covers.FromBytesAsync(bytes, 330);
        }

        /// <summary>Covers: book covers, first pages, and photos themselves (made small).</summary>
        /// <summary>Your own poster next to the video: "&lt;video name&gt;.poster.jpg" (or .png / .jpeg).</summary>
        public static string OwnPoster(string video)
        {
            if (string.IsNullOrEmpty(video)) return null;
            var stem = Path.Combine(Path.GetDirectoryName(video), Path.GetFileNameWithoutExtension(video));
            foreach (var ext in new[] { ".poster.jpg", ".poster.png", ".poster.jpeg" })
                if (Fs.Exists(stem + ext)) return stem + ext;
            return null;
        }

        public static async Task LoadCoverAsync(Item it)
        {
            if (it.Cover != null) return;
            if (it.IsGroup)
            {
                // A poster in the Posters folder for this season / series ("NFL RedZone 2024.jpg", "The Office Season 2.jpg").
                var gp = PosterFolder.Find(it);
                if (gp != null) { it.Cover = await Covers.FromBytesAsync(await Task.Run(() => Fs.ReadAllBytes(gp)), 330); if (it.Cover != null) { it.OwnPosterShown = true; return; } }
                // A season or series: its first episode's poster (or drawn poster).
                var first = it.Children[0];
                await LoadCoverAsync(first);
                it.LogoA = first.LogoA; it.LogoB = first.LogoB;
                if (first.Cover != null) it.Cover = first.Cover;
                return;
            }
            try
            {
                // Your own poster beats everything: one next to the video, or one in the Posters folder named after it.
                var own = OwnPoster(it.Path) ?? PosterFolder.Find(it);
                if (own != null)
                {
                    it.Cover = await Covers.FromBytesAsync(await Task.Run(() => Fs.ReadAllBytes(own)), 330);
                    if (it.Cover != null) { it.OwnPosterShown = true; return; }
                }
                if (it.Kind == Kind.Photo) it.Cover = await Covers.FromBytesAsync(await Task.Run(() => Fs.ReadAllBytes(it.Path)), 300);
                else if (it.Kind == Kind.Music || it.Kind == Kind.Audiobook || it.Kind == Kind.Podcast)
                    it.Cover = await Covers.FromBytesAsync(await Task.Run(() => AlbumArt.Read(it.Path)), 300);
                else if (it.Kind == Kind.Pdf || it.Kind == Kind.Comic || it.Kind == Kind.Manga || it.Kind == Kind.Book) it.Cover = await Covers.GetAsync(it);
                else if (it.Kind == Kind.Recording)
                {
                    // Its saved thumbnail; a poster saved by an older version isn't shown: a frame from the recording instead.
                    it.Cover = await Covers.ThumbFromBytesAsync(await Covers.RecordingArtAsync(it), 300);
                    if (it.Cover == null) it.Cover = await Covers.ThumbFromBytesAsync(await Covers.VideoFrameAsync(it), 300);
                }
                else if (it.Kind == Kind.SportsRecording)
                {
                    // The guide's own picture of the game (Sky's, saved when it was recorded); else the drawn thumbnail
                    // with the teams' logos and colours.
                    string saved = "";
                    try { var mp = Recorder.MetaPath(it.Path); if (Fs.Exists(mp)) saved = Windows.Data.Json.JsonObject.Parse(Fs.ReadAllText(mp)).GetNamedString("thumb", ""); } catch { }
                    if (Pictures.IsGamePoster(saved))
                    {
                        var pic = await Covers.ThumbFromBytesAsync(await Task.Run(() => Fs.Exists(Recorder.ArtPath(it.Path)) ? Fs.ReadAllBytes(Recorder.ArtPath(it.Path)) : null), 300);
                        if (pic != null) { it.Cover = pic; return; }
                    }
                    else
                    {
                        // TheSportsDB's poster of the game (asked once per game), saved and locked with the recording.
                        var sdb = await SportsPosterAsync(it);
                        if (sdb != null) { it.Cover = sdb; return; }
                    }
                    var a = !string.IsNullOrEmpty(it.LogoUrlA) ? it.LogoUrlA : TeamLogos.LogoUrl(it.Away);
                    var b = !string.IsNullOrEmpty(it.LogoUrlB) ? it.LogoUrlB : TeamLogos.LogoUrl(it.Home);
                    // The logos are locked in with the recording (saved next to it the first time).
                    var pa = Recorder.LogoPath(it.Path, "away"); var pb = Recorder.LogoPath(it.Path, "home");
                    await Recorder.SaveLogoAsync(a, pa); await Recorder.SaveLogoAsync(b, pb);
                    if (Fs.Exists(pa) && Fs.Exists(pb))
                    {
                        var la = await Covers.FromBytesAsync(await Task.Run(() => Fs.ReadAllBytes(pa)), 200);
                        var lb = await Covers.FromBytesAsync(await Task.Run(() => Fs.ReadAllBytes(pb)), 200);
                        if (la != null && lb != null) { it.LogoA = la; it.LogoB = lb; return; }
                    }
                    if (a == null || b == null) TeamLogos.LookUpNow(new[] { (it.Away, it.League), (it.Home, it.League) });
                }
                else if (it.Kind == Kind.SportsEvent)
                {
                    // TheSportsDB's poster of the event (every game and event that has one, saved with it); else the drawn poster.
                    {
                        // TheSportsDB's art, saved with the event the first time (Data\sports\<file>.img) and used from there.
                        var saved = SportsEvents.ArtPath(it.Path);
                        byte[] art = Fs.Exists(saved) ? await Task.Run(() => Fs.ReadAllBytes(saved)) : null;
                        if (art == null)
                        {
                            var th = SportsEvents.Thumb(it);
                            if (th.Length > 0) { art = await LiveTv.ImageBytesAsync(th); if (art != null) try { Fs.WriteAllBytes(saved, art); } catch { } }
                        }
                        if (art != null) it.Cover = await Covers.ThumbFromBytesAsync(art, 300);
                        if (it.Cover != null) return;
                        // No art (NFL RedZone): its logo on the drawn thumbnail.
                        if (it.Home.Length == 0)
                        {
                            if (!string.IsNullOrEmpty(it.LogoUrlA)) it.LogoA = await Covers.FromBytesAsync(await LiveTv.ImageBytesAsync(it.LogoUrlA), 200);
                            return;
                        }
                    }
                    // The team-colour thumbnail with both teams' badges (kept next to the event's details).
                    var pa = SportsEvents.LogoPath(it.Path, "away"); var pb = SportsEvents.LogoPath(it.Path, "home");
                    // Your own logos win (e.g. the teams' logos from that era): "<video name>.away.png" and
                    // "<video name>.home.png" (or .jpg) next to the video.
                    var stem = Path.Combine(Path.GetDirectoryName(it.Path), Path.GetFileNameWithoutExtension(it.Path));
                    foreach (var (side, dest) in new[] { ("away", pa), ("home", pb) })
                        foreach (var ext in new[] { ".png", ".jpg" })
                            if (Fs.Exists(stem + "." + side + ext)) { try { Fs.WriteAllBytes(dest, Fs.ReadAllBytes(stem + "." + side + ext)); } catch { } break; }
                    await Recorder.SaveLogoAsync(it.LogoUrlA, pa); await Recorder.SaveLogoAsync(it.LogoUrlB, pb);
                    if (Fs.Exists(pa) && Fs.Exists(pb))
                    {
                        var la = await Covers.FromBytesAsync(await Task.Run(() => Fs.ReadAllBytes(pa)), 200);
                        var lb = await Covers.FromBytesAsync(await Task.Run(() => Fs.ReadAllBytes(pb)), 200);
                        if (la != null && lb != null) { it.LogoA = la; it.LogoB = lb; }
                    }
                }
                else if (it.Kind == Kind.TvShow)
                {
                    // The show's wide picture (TVmaze / TMDB), else a frame from the episode.
                    var url = await Posters.LookUpNowAsync(it.Title);
                    if (url != null) it.Cover = await Covers.ThumbFromBytesAsync(await LiveTv.ImageBytesAsync(url), 300);
                    if (it.Cover == null) it.Cover = await Covers.ThumbFromBytesAsync(await Covers.VideoFrameAsync(it), 300);
                }
                else if (it.Kind == Kind.Movie)
                {
                    // The movie's poster from TMDB (found from the file name); else a frame from the video. Each edition its
                    // own poster (Director's Cut, Extended… a different one of the movie's posters); black-and-white editions
                    // in black and white.
                    var m = await Movies.LookUpAsync(it.Path);
                    var bw = Movies.IsBlackAndWhite(it.Episode);
                    var pic = m != null ? Movies.PosterFor(m, it.Episode) : "";
                    byte[] bytes = null;
                    if (bw && m != null)
                        // Logan Noir & co: one of the movie's posters that is itself black and white (often the official one).
                        foreach (var u in m.Posters)
                        {
                            var b = await LiveTv.ImageBytesAsync(u);
                            if (b != null && await Covers.IsBlackAndWhiteAsync(b)) { bytes = b; break; }
                        }
                    if (bytes == null && pic.Length > 0) bytes = await LiveTv.ImageBytesAsync(pic);
                    if (bytes == null) bytes = await Covers.VideoFrameAsync(it);
                    it.Cover = bw ? await Covers.GrayFromBytesAsync(bytes, 330) : await Covers.FromBytesAsync(bytes, 330);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// TV show episodes in TV Shows (D:\DevelopmentFiles\Shelf\TV Shows): "Show Name S01E02 ….mkv" or
    /// "TV Shows\Show Name\Season 1\02 - Pilot.mkv" → the show "Show Name", "Season 1 · Episode 2".
    /// </summary>
    public static class TvShows
    {
        public static void Name(Item it, string root)
        {
            var file = Path.GetFileNameWithoutExtension(it.Path).Replace('.', ' ').Replace('_', ' ');
            var rel = it.Path.Length > root.Length + 1 ? it.Path.Substring(root.Length + 1) : Path.GetFileName(it.Path);
            var dirs = rel.Split('\\'); // [Show, Season 1, file] or [file]
            var m = System.Text.RegularExpressions.Regex.Match(file, @"^(.*?)[\s\-\[\(]*[Ss](\d{1,2})[\s\.]?[Ee](\d{1,3})|^(.*?)[\s\-]*(\d{1,2})x(\d{2,3})");
            string show = "", season = "", ep = "";
            if (m.Success)
            {
                show = (m.Groups[1].Success && m.Groups[1].Value.Trim().Length > 0 ? m.Groups[1].Value : m.Groups[4].Value).Trim(' ', '-');
                season = m.Groups[2].Success && m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : m.Groups[5].Value;
                ep = m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? m.Groups[3].Value : m.Groups[6].Value;
            }
            if (dirs.Length >= 2) show = dirs[0]; // the show's folder names it best
            if (season.Length == 0 && dirs.Length >= 3)
            {
                var sm = System.Text.RegularExpressions.Regex.Match(dirs[dirs.Length - 2], @"(\d+)");
                if (sm.Success) season = sm.Value;
            }
            if (ep.Length == 0)
            {
                var em = System.Text.RegularExpressions.Regex.Match(file, @"^(\d{1,3})\b|[Ee]pisode\s*(\d+)");
                if (em.Success) ep = em.Groups[1].Success ? em.Groups[1].Value : em.Groups[2].Value;
            }
            if (show.Length == 0) show = file;
            show = System.Text.RegularExpressions.Regex.Replace(show, @"\s+\((19|20)\d{2}\)$", "").Trim();
            it.Title = show;
            it.Episode = (season.Length > 0 ? "Season " + int.Parse(season) : "") +
                         (ep.Length > 0 ? (season.Length > 0 ? " · " : "") + "Episode " + int.Parse(ep) : "");
        }
    }

    /// <summary>
    /// Older sports events (videos in D:\DevelopmentFiles\Shelf\Sports), found on TheSportsDB from the file name, like
    /// Kodi's sports scraper: "NFL 2008-02-03 New York Giants vs New England Patriots.mkv",
    /// "English Premier League 2015-04-26 Arsenal vs Chelsea.mp4" or just "Giants at Patriots 2008-02-03.mkv".
    /// Teams, league, date, venue and description, both teams' badges and colours; kept in Data\sports\&lt;file&gt;.json.
    /// No scores (no spoilers).
    /// </summary>
    public static class SportsEvents
    {
        // Your own TheSportsDB key when it's set on the phone page (every result, faster), else the free access.
        static string Api => SportsDb.Api + "/";
        static string MetaPath(string file) => Storage.DataFolder("sports") + "\\" + Path.GetFileName(file) + ".json";
        /// <summary>TheSportsDB's art of the event, saved with it.</summary>
        public static string ArtPath(string file) => Storage.DataFolder("sports") + "\\" + Path.GetFileName(file) + ".poster.img";
        public static string LogoPath(string file, string side) => Storage.DataFolder("sports") + "\\" + Path.GetFileName(file) + "." + side + ".img";

        static Windows.Data.Json.JsonObject Meta(Item it)
        {
            try { var p = MetaPath(it.Path); if (Fs.Exists(p)) return Windows.Data.Json.JsonObject.Parse(Fs.ReadAllText(p)); } catch { }
            return null;
        }

        /// <summary>TheSportsDB's poster of the event (portrait), if any.</summary>
        public static string Thumb(Item it) => Meta(it)?.GetNamedString("poster", "") ?? "";

        /// <summary>Raised when the scraper learns new kinds of names, so earlier misses are looked up again.</summary>
        const int ScraperVersion = 4;

        public static bool Checked(Item it)
        {
            var m = Meta(it);
            if (m == null) return false;
            // Not found: try again after a week (TheSportsDB keeps adding old events).
            // Found before TheSportsDB's posters were kept: look once more for the event's poster.
            if ((m.GetNamedString("home", "").Length > 0 || m.GetNamedString("event", "").Length > 0) && !m.ContainsKey("poster") && m.GetNamedString("event", "") != "NFL RedZone") return false;
            if (m.GetNamedString("home", "").Length > 0 || m.GetNamedString("event", "").Length > 0) return true;
            // Missed by an older version of the scraper (it couldn't read "UFC 312", short team names…): look again now.
            if (m.GetNamedNumber("v", 0) < ScraperVersion) return false;
            // Not found with the free access, and now you've added your own key: look again.
            if (SportsDb.HasOwnKey && !m.GetNamedBoolean("ownKey", false)) return false;
            return LiveTv.Now - (long)m.GetNamedNumber("checked", 0) < 7 * 86_400_000L;
        }

        const string PartPattern = @"\b(early[\s\-]*prelims|prelims|preliminary[\s\-]*card|main[\s\-]*card|countdown|weigh[\s\-]*ins?|embedded|post[\s\-]*fight([\s\-]*press[\s\-]*conference)?|press[\s\-]*conference|pre[\s\-]*show|kickoff[\s\-]*show)\b";

        /// <summary>"Prelims", "Main Card", "Early Prelims"… from the file name ("" for none).</summary>
        public static string PartOf(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(name.Replace('.', ' ').Replace('_', ' '), PartPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            var t = System.Text.RegularExpressions.Regex.Replace(m.Value.ToLowerInvariant(), @"[\s\-]+", " ");
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t).Replace("Ins", "ins");
        }

        /// <summary>What the file name says, before (or without) TheSportsDB.</summary>
        static (string League, string Date, string A, string Sep, string B) Parse(string name)
        {
            var s = name.Replace('_', ' ').Replace('.', ' ').Trim();
            string date = "";
            var dm = System.Text.RegularExpressions.Regex.Match(s, @"\b((19|20)\d{2})[- ](\d{2})[- ](\d{2})\b");
            if (dm.Success) { date = $"{dm.Groups[1].Value}-{dm.Groups[3].Value}-{dm.Groups[4].Value}"; s = s.Remove(dm.Index, dm.Length); }
            // The part of a fight card or show ("Prelims", "Main Card"): not part of the event's name.
            s = System.Text.RegularExpressions.Regex.Replace(s, PartPattern, " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim(' ', '-');
            // A numbered fight card ("UFC 312 du Plessis vs Strickland 2", "UFC Fight Night …", "Bellator 300"): an event,
            // not two teams.
            var fight = System.Text.RegularExpressions.Regex.Match(s, @"^(UFC|Bellator|PFL|ONE)\s+(\d+|Fight Night\b.*?)(\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (fight.Success)
            {
                var org = fight.Groups[1].Value.ToUpperInvariant() == "UFC" ? "UFC" : fight.Groups[1].Value;
                return (org, date, org + " " + fight.Groups[2].Value.Trim(), "event", "");
            }
            var t = System.Text.RegularExpressions.Regex.Match(s, @"^(.*?)\s+(vs\.?|v\.?|at|@)\s+(.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!t.Success)
            {
                // No teams: an event ("WWE WrestleMania 30", "WWE Raw", "NFL RedZone"): its promotion / league and name.
                var em = System.Text.RegularExpressions.Regex.Match(s.Trim(' ', '-'), @"^(WWE|AEW|TNA|ROH|NJPW|NXT|UFC|NFL|NBA|NHL|MLB)\b[\s\-:]*(.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var lg = em.Success ? em.Groups[1].Value.ToUpperInvariant() : "";
                var ev = (em.Success ? em.Groups[2].Value : s).Trim(' ', '-');
                if (lg == "NXT") { lg = "WWE"; ev = "NXT " + ev; }
                return (lg, date, ev.Length > 0 ? ev : lg, "event", "");
            }
            // "NFL 2024 Week 10 Bengals at Ravens": the season and week aren't part of the team names.
            string Clean(string x) => System.Text.RegularExpressions.Regex.Replace(x, @"\b(19|20)\d{2}\b|\bweek[\s\-]*\d{1,2}\b|\bseason\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var a = System.Text.RegularExpressions.Regex.Replace(Clean(t.Groups[1].Value), @"\s+", " ").Trim(' ', '-');
            var b = System.Text.RegularExpressions.Regex.Replace(Clean(t.Groups[3].Value), @"\s+", " ").Trim(' ', '-');
            var sep = t.Groups[2].Value.ToLowerInvariant().TrimEnd('.');
            // A league at the front ("NFL Giants vs Patriots"): split it off when it's a known one.
            string league = "";
            var lm = System.Text.RegularExpressions.Regex.Match(a, @"^(NFL|NBA|NHL|MLB|MLS|WNBA|NCAA[A-Z]*|English Premier League|Premier League|La Liga|Serie A|Bundesliga|Champions League|UFC|College Football|College Basketball)\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (lm.Success) { league = lm.Groups[1].Value; a = lm.Groups[2].Value; }
            return (league, date, a, sep, b);
        }

        /// <summary>Fills the item from what's known (saved details, or the file name).</summary>
        public static void Apply(Item it)
        {
            var m = Meta(it);
            it.LeagueStyle = m == null || m.GetNamedNumber("style", 0) >= 2; // games added from now on
            if (m != null && m.GetNamedString("home", "").Length == 0 && m.GetNamedString("event", "").Length > 0)
            {
                // An event without teams (wrestling, RedZone): its name on the thumbnail.
                it.Away = m.GetNamedString("event", ""); it.Home = ""; it.Sep = "";
                if (it.Away == "NFL RedZone")
                {
                    TeamColors.Remember("NFL RedZone", "c8102e", "013369"); // RedZone's red
                    it.LogoUrlA = "https://a.espncdn.com/i/teamlogos/leagues/500/nfl.png"; // the NFL shield
                }
                it.League = m.GetNamedString("league", "");
                if (DateTime.TryParse(m.GetNamedString("date", ""), out var d0)) it.When = d0.ToString("MMM d, yyyy");
                it.Episode = m.GetNamedString("extra", "");
                it.Title = it.Away;
                return;
            }
            if (m != null && m.GetNamedString("home", "").Length > 0)
            {
                it.Home = m.GetNamedString("home", ""); it.Away = m.GetNamedString("away", ""); it.Sep = "at";
                it.League = m.GetNamedString("league", "");
                it.LogoUrlA = m.GetNamedString("awayBadge", ""); it.LogoUrlB = m.GetNamedString("homeBadge", "");
                if (DateTime.TryParse(m.GetNamedString("date", ""), out var d)) it.When = d.ToString("MMM d, yyyy");
                it.Episode = m.GetNamedString("event", "") is string ev && ev.Length > 0 && ev != $"{it.Home} vs {it.Away}" ? ev : "";
                var part = m.GetNamedString("extra", "");
                if (part.Length > 0) it.Episode = it.Episode.Length > 0 ? it.Episode + " · " + part : part;
                TeamColors.Remember(it.Home, m.GetNamedString("homeColor", ""), m.GetNamedString("homeAlt", ""));
                TeamColors.Remember(it.Away, m.GetNamedString("awayColor", ""), m.GetNamedString("awayAlt", ""));
            }
            else
            {
                var p = Parse(Path.GetFileNameWithoutExtension(it.Path));
                if (p.Sep == "event")
                {
                    it.Away = p.A; it.Home = ""; it.Sep = ""; it.League = p.League; it.Title = p.A; it.Episode = PartOf(Path.GetFileNameWithoutExtension(it.Path));
                    if (DateTime.TryParse(p.Date, out var d1)) it.When = d1.ToString("MMM d, yyyy");
                    return;
                }
                if (p.A.Length > 0)
                {
                    // "A vs B": home first (TheSportsDB's way); "A at B": away first.
                    if (p.Sep == "at" || p.Sep == "@") { it.Away = p.A; it.Home = p.B; } else { it.Home = p.A; it.Away = p.B; }
                    it.Sep = "at"; it.League = p.League;
                    if (DateTime.TryParse(p.Date, out var d)) it.When = d.ToString("MMM d, yyyy");
                }
            }
            if (it.Away.Length > 0) it.Title = $"{it.Away} at {it.Home}";
        }

        static async Task<Windows.Data.Json.JsonObject> GetAsync(string q)
        {
            await Task.Delay(SportsDb.HasOwnKey ? 700 : 2100); // free access: about 30 requests a minute; supporters 100
            try
            {
                var o = Windows.Data.Json.JsonObject.Parse(await LiveTv.Client.GetStringAsync(Api + q));
                return o;
            }
            catch { return null; }
        }

        static Windows.Data.Json.JsonArray Events(Windows.Data.Json.JsonObject o, string field = "event") =>
            o != null && o.ContainsKey(field) && o[field].ValueType == Windows.Data.Json.JsonValueType.Array ? o.GetNamedArray(field) : new Windows.Data.Json.JsonArray();

        static bool NearDate(string a, string b) =>
            DateTime.TryParse(a, out var x) && DateTime.TryParse(b, out var y) && Math.Abs((x - y).TotalDays) <= 1;

        static readonly (string League, string Path)[] TeamLists = { ("NFL", "football/nfl"), ("NBA", "basketball/nba"), ("NHL", "hockey/nhl"), ("MLB", "baseball/mlb") };

        /// <summary>Both teams' full names ("Cleveland Browns") from ESPN's team list of the league (or the first league with both).</summary>
        static async Task<Tuple<string, string>> FullNamesAsync(string league, string a, string b)
        {
            foreach (var (lg, path) in TeamLists.Where(x => league.Length == 0 || string.Equals(x.League, league, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var root = Windows.Data.Json.JsonObject.Parse(await LiveTv.Client.GetStringAsync($"https://site.api.espn.com/apis/site/v2/sports/{path}/teams"));
                    var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var tv in root.GetNamedArray("sports").GetObjectAt(0).GetNamedArray("leagues").GetObjectAt(0).GetNamedArray("teams"))
                    {
                        var t = tv.GetObject().GetNamedObject("team");
                        var full = S(t, "displayName");
                        foreach (var k in new[] { full, S(t, "shortDisplayName"), S(t, "name"), S(t, "location") })
                            if (k.Length > 0 && !names.ContainsKey(k)) names[k] = full;
                    }
                    if (names.TryGetValue(a, out var fa) && names.TryGetValue(b, out var fb)) return Tuple.Create(fa, fb);
                }
                catch { }
            }
            return null;
        }

        static string S(Windows.Data.Json.JsonObject o, string f) => o.ContainsKey(f) && o[f].ValueType == Windows.Data.Json.JsonValueType.String ? o.GetNamedString(f) : "";

        /// <summary>Finds the event on TheSportsDB (by file name, then by teams and date) and saves its details.</summary>
        public static async Task LookUpAsync(Item it)
        {
            var name = Path.GetFileNameWithoutExtension(it.Path);
            var p = Parse(name);
            Windows.Data.Json.JsonObject ev = null;
            // 1) TheSportsDB's file-name search ("League_YYYY-MM-DD_Home_vs_Away").
            var r = await GetAsync("searchfilename.php?e=" + Uri.EscapeDataString(System.Text.RegularExpressions.Regex.Replace(name.Replace('.', ' ').Trim(), @"\s+", "_")));
            ev = Events(r).Where(x => x.ValueType == Windows.Data.Json.JsonValueType.Object).Select(x => x.GetObject()).FirstOrDefault();
            // 2) The teams (both ways round), in that season, the right date first.
            if (ev == null && p.A.Length > 0 && p.Sep != "event")
            {
                // Short names ("Browns at Steelers") → full ones from ESPN's team lists, as TheSportsDB needs them.
                var full = await FullNamesAsync(p.League, p.A, p.B);
                if (full != null) p = (p.League, p.Date, full.Item1, p.Sep, full.Item2);
                // Named by week ("NFL 2024 Week 10 Bengals at Ravens"): the game's date from ESPN's schedule for that week.
                var wk = System.Text.RegularExpressions.Regex.Match(name, @"\bweek[\s\-_]*(\d{1,2})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var yr = System.Text.RegularExpressions.Regex.Match(name, @"\b(19|20)\d{2}\b");
                if (p.Date.Length != 10 && wk.Success && yr.Success && full != null)
                {
                    try
                    {
                        var sb = Windows.Data.Json.JsonObject.Parse(await LiveTv.Client.GetStringAsync(
                            $"https://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard?dates={yr.Value}&seasontype=2&week={wk.Groups[1].Value}"));
                        foreach (var x in sb.GetNamedArray("events", new Windows.Data.Json.JsonArray()).Where(v => v.ValueType == Windows.Data.Json.JsonValueType.Object).Select(v => v.GetObject()))
                        {
                            var nm = S(x, "name");
                            if (nm.IndexOf(full.Item1, StringComparison.OrdinalIgnoreCase) < 0 || nm.IndexOf(full.Item2, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            // ESPN's time is UTC: the local (US) day of a night game is the day before.
                            if (DateTimeOffset.TryParse(S(x, "date"), out var when))
                                p = (p.League, when.ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd"), p.A, p.Sep, p.B);
                            break;
                        }
                    }
                    catch { }
                }
                var year = p.Date.Length >= 4 ? int.Parse(p.Date.Substring(0, 4)) : 0;
                var seasons = year > 0 ? new[] { year.ToString(), (year - 1).ToString(), $"{year - 1}-{year}", $"{year}-{year + 1}" } : new[] { "" };
                foreach (var pair in new[] { (p.A, p.B), (p.B, p.A) })
                {
                    foreach (var season in seasons)
                    {
                        var q = "searchevents.php?e=" + Uri.EscapeDataString((pair.Item1 + " vs " + pair.Item2).Replace(' ', '_')) + (season.Length > 0 ? "&s=" + season : "");
                        var list = Events(await GetAsync(q)).Where(x => x.ValueType == Windows.Data.Json.JsonValueType.Object).Select(x => x.GetObject()).ToList();
                        // The same day, or a day either side (TheSportsDB dates night games by UTC).
                        ev = (p.Date.Length > 0 ? list.FirstOrDefault(x => NearDate(S(x, "dateEvent"), p.Date)) : null) ?? (p.Date.Length == 0 ? list.FirstOrDefault() : null);
                        if (ev != null || (year == 0 && list.Count > 0)) break;
                    }
                    if (ev != null) break;
                }
            }
            var meta = new Windows.Data.Json.JsonObject { ["checked"] = Windows.Data.Json.JsonValue.CreateNumberValue(LiveTv.Now), ["ownKey"] = Windows.Data.Json.JsonValue.CreateBooleanValue(SportsDb.HasOwnKey),
                ["v"] = Windows.Data.Json.JsonValue.CreateNumberValue(ScraperVersion) };
            var before = Meta(it);
            meta["style"] = Windows.Data.Json.JsonValue.CreateNumberValue(before == null ? 2 : before.GetNamedNumber("style", 0));
            void Put0(string k, string v) => meta[k] = Windows.Data.Json.JsonValue.CreateStringValue(v ?? "");
            // NFL RedZone (a whip-around show, not a game): its week from ESPN's schedule.
            if (ev == null && p.Sep == "event" && System.Text.RegularExpressions.Regex.IsMatch(name, @"red\s*zone", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                Put0("event", "NFL RedZone"); Put0("league", "NFL");
                // "NFL RedZone 2024 Week 10": that week's Sunday from ESPN's schedule (the day most of its games were on).
                var wkm = System.Text.RegularExpressions.Regex.Match(name, @"\bweek[\s\-_]*(\d{1,2})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var yrm = System.Text.RegularExpressions.Regex.Match(name, @"\b(19|20)\d{2}\b");
                if (p.Date.Length != 10 && wkm.Success && yrm.Success)
                {
                    try
                    {
                        var sb0 = Windows.Data.Json.JsonObject.Parse(await LiveTv.Client.GetStringAsync(
                            $"https://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard?dates={yrm.Value}&seasontype=2&week={wkm.Groups[1].Value}"));
                        var day = sb0.GetNamedArray("events", new Windows.Data.Json.JsonArray()).Where(x => x.ValueType == Windows.Data.Json.JsonValueType.Object)
                            .Select(x => x.GetObject().GetNamedString("date", "")).Where(d0 => d0.Length >= 10).GroupBy(d0 => d0.Substring(0, 10))
                            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                        if (day != null) p = (p.League, day, p.A, p.Sep, p.B);
                    }
                    catch { }
                    Put0("extra", $"Week {int.Parse(wkm.Groups[1].Value)} · {yrm.Value} season");
                }
                Put0("date", p.Date);
                if (p.Date.Length == 10 && !wkm.Success)
                    try
                    {
                        var sb = Windows.Data.Json.JsonObject.Parse(await LiveTv.Client.GetStringAsync("https://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard?dates=" + p.Date.Replace("-", "")));
                        var wk = sb.ContainsKey("week") && sb["week"].ValueType == Windows.Data.Json.JsonValueType.Object ? sb.GetNamedObject("week").GetNamedNumber("number", 0) : 0;
                        if (wk > 0) Put0("extra", "Week " + wk);
                    }
                    catch { }
                try { Fs.WriteAllText(MetaPath(it.Path), meta.Stringify()); } catch { }
                Apply(it);
                return;
            }
            // An event without teams (WWE WrestleMania, SummerSlam, Raw…): by name in that year, by name without its
            // number ("WrestleMania 30" → WrestleMania XXX), then WWE's event on that date.
            if (ev == null && p.Sep == "event" && p.A.Length > 0)
            {
                var year = p.Date.Length >= 4 ? p.Date.Substring(0, 4) : (System.Text.RegularExpressions.Regex.Match(name, @"\b(19|20)\d{2}\b").Value);
                var nm = System.Text.RegularExpressions.Regex.Replace(p.A, @"\b(19|20)\d{2}\b", "").Trim();
                var bare = System.Text.RegularExpressions.Regex.Replace(nm, @"\s+(\d+|[IVXLC]+)$", "").Trim();
                if (p.League.Length > 0 && p.Date.Length == 10)
                {
                    var fr = await GetAsync("searchfilename.php?e=" + Uri.EscapeDataString($"{p.League}_{p.Date}_{nm}".Replace(' ', '_')));
                    ev = Events(fr).Where(x => x.ValueType == Windows.Data.Json.JsonValueType.Object).Select(x => x.GetObject()).FirstOrDefault();
                }
                foreach (var n in new[] { nm, bare }.Distinct())
                {
                    if (ev != null || n.Length == 0) break;
                    var list = Events(await GetAsync("searchevents.php?e=" + Uri.EscapeDataString(n.Replace(' ', '_')) + (year.Length == 4 ? "&s=" + year : "")))
                        .Where(x => x.ValueType == Windows.Data.Json.JsonValueType.Object).Select(x => x.GetObject()).ToList();
                    ev = (p.Date.Length == 10 ? list.FirstOrDefault(x => NearDate(S(x, "dateEvent"), p.Date)) : null) ?? (p.Date.Length != 10 ? list.FirstOrDefault() : null);
                }
                if (ev == null && p.Date.Length == 10 && (p.League == "WWE" || p.League.Length == 0))
                {
                    var list = Events(await GetAsync("eventsday.php?d=" + p.Date + "&l=4444")).Where(x => x.ValueType == Windows.Data.Json.JsonValueType.Object).Select(x => x.GetObject()).ToList();
                    ev = list.FirstOrDefault(x => bare.Length == 0 || S(x, "strEvent").IndexOf(bare.Split(' ')[0], StringComparison.OrdinalIgnoreCase) >= 0) ?? (list.Count == 1 ? list[0] : null);
                }
            }
            if (ev != null)
            {
                void Put(string k, string v) => meta[k] = Windows.Data.Json.JsonValue.CreateStringValue(v ?? "");
                Put("event", S(ev, "strEvent")); Put("date", S(ev, "dateEvent")); Put("league", S(ev, "strLeague")); Put("season", S(ev, "strSeason"));
                Put("home", S(ev, "strHomeTeam")); Put("away", S(ev, "strAwayTeam")); Put("thumb", S(ev, "strThumb")); Put("poster", S(ev, "strPoster"));
                Put("extra", PartOf(name)); Put("venue", S(ev, "strVenue")); Put("description", S(ev, "strDescriptionEN"));
                // Both teams' badges and colours (for the team-colour thumbnail).
                foreach (var side in new[] { "home", "away" })
                {
                    var id = S(ev, side == "home" ? "idHomeTeam" : "idAwayTeam");
                    if (id.Length == 0) continue;
                    var team = Events(await GetAsync("lookupteam.php?id=" + id), "teams").Where(x => x.ValueType == Windows.Data.Json.JsonValueType.Object).Select(x => x.GetObject()).FirstOrDefault();
                    if (team == null) continue;
                    Put(side + "Badge", S(team, "strBadge").Length > 0 ? S(team, "strBadge") + "/medium" : "");
                    Put(side + "Color", S(team, "strColour1").TrimStart('#')); Put(side + "Alt", S(team, "strColour2").TrimStart('#'));
                }
            }
            try { Fs.WriteAllText(MetaPath(it.Path), meta.Stringify()); } catch { }
            Apply(it);
        }
    }

    /// <summary>
    /// Import from USB: video files on any USB drive plugged into the Xbox, copied into the library (much faster than
    /// Wi-Fi). Each goes where it belongs: games ("Spurs at Knicks 2026-06-10", "WWE SummerSlam 2015") to Sports,
    /// episodes ("Show S01E02") to TV Shows, the rest to Movies. Files already in the library are skipped.
    /// </summary>
    public static class UsbImport
    {
        static readonly string[] Types = { ".mp4", ".m4v", ".mkv", ".ts", ".m2ts", ".mov", ".avi", ".wmv", ".webm" };

        public static async Task<List<Windows.Storage.StorageFile>> FindAsync()
        {
            var found = new List<Windows.Storage.StorageFile>();
            foreach (var drive in await Windows.Storage.KnownFolders.RemovableDevices.GetFoldersAsync())
            {
                try
                {
                    var opts = new Windows.Storage.Search.QueryOptions(Windows.Storage.Search.CommonFileQuery.DefaultQuery, Types)
                        { FolderDepth = Windows.Storage.Search.FolderDepth.Deep };
                    found.AddRange(await drive.CreateFileQueryWithOptions(opts).GetFilesAsync());
                }
                catch { }
            }
            return found;
        }

        /// <summary>Movies, TV Shows or Sports, from the file name.</summary>
        public static string FolderFor(string name)
        {
            var n = System.IO.Path.GetFileNameWithoutExtension(name).Replace('.', ' ').Replace('_', ' ');
            if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b[Ss]\d{1,2}\s?[Ee]\d{1,3}\b|\b\d{1,2}x\d{2,3}\b")) return "TV Shows";
            if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b(WWE|AEW|NXT|TNA|UFC|RedZone|Red Zone|WrestleMania|SummerSlam|Royal Rumble)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "Sports";
            if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\s(vs\.?|v|at|@)\s", System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                System.Text.RegularExpressions.Regex.IsMatch(n, @"\b(19|20)\d{2}\b")) return "Sports";
            return "Movies";
        }

        /// <summary>Copies one file; false if it was already there (same name and size). Progress 0..1.</summary>
        public static async Task<bool> CopyAsync(Windows.Storage.StorageFile f, IProgress<double> progress, System.Threading.CancellationToken ct)
        {
            var folder = Storage.Folder(FolderFor(f.Name));
            var size = (long)(await f.GetBasicPropertiesAsync()).Size;
            var target = folder + "\\" + f.Name;
            var have = Fs.Info(target);
            if (have != null && have.Size == size) return false;
            var dest = have == null ? target : Fs.UniquePath(folder, f.Name);
            var part = dest + ".part";
            try
            {
                using (var src = await f.OpenStreamForReadAsync())
                using (var dst = Fs.Create(part))
                {
                    var buf = new byte[4 * 1024 * 1024];
                    long done = 0; int n;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n, ct);
                        done += n;
                        progress?.Report(size > 0 ? (double)done / size : 0);
                    }
                }
                if (!Fs.Move(part, dest)) throw new System.IO.IOException("Couldn't finish saving " + f.Name);
                return true;
            }
            catch { Fs.Delete(part); throw; }
        }
    }

    /// <summary>
    /// Seasons: a TV show's episodes by season ("The Office · Season 2"), and recurring sports shows by series and year
    /// ("NFL RedZone · 2024", "WWE RAW · 1999", "AEW Collision · 2025"): every season its own poster, holding its
    /// episodes.
    /// </summary>
    public static class Seasons
    {
        static (string Key, string Title)? KeyOf(Item it)
        {
            if (it.Kind == Kind.TvShow)
            {
                var sm = System.Text.RegularExpressions.Regex.Match(it.Episode ?? "", @"Season (\d+)");
                return ($"tv|{it.Title}|{(sm.Success ? sm.Groups[1].Value : "")}", it.Title + (sm.Success ? " · Season " + sm.Groups[1].Value : ""));
            }
            if (it.Kind == Kind.SportsEvent && it.Home.Length == 0 && it.Away.Length > 0)
            {
                // "RAW #1089" → RAW, "Collision #80" → Collision, "NFL RedZone" stays; the year from its date.
                var series = System.Text.RegularExpressions.Regex.Replace(it.Away, @"\s*#\s*\d+.*$", "").Trim();
                if (series.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(series, @"\d")) return null; // numbered cards (UFC 312) stay single
                var y = System.Text.RegularExpressions.Regex.Match(it.When ?? "", @"(19|20)\d{2}").Value;
                var league = it.League.Length > 0 && !series.StartsWith(it.League, StringComparison.OrdinalIgnoreCase) ? it.League + " " : "";
                return ($"sp|{league}{series}|{y}", league + series + (y.Length > 0 ? " · " + y : ""));
            }
            return null;
        }

        public static List<Item> Group(List<Item> items)
        {
            var result = new List<Item>();
            var groups = new Dictionary<string, Item>();
            foreach (var it in items)
            {
                var k = KeyOf(it);
                if (k == null) { result.Add(it); continue; }
                if (!groups.TryGetValue(k.Value.Key, out var g))
                {
                    g = new Item
                    {
                        IsGroup = true, Children = new List<Item>(), Title = k.Value.Title, Kind = it.Kind, Path = it.Path,
                        Away = k.Value.Title, Home = "", Sep = "", League = it.League, LogoUrlA = it.LogoUrlA, Added = it.Added,
                        Channel = it.Channel, Badge = it.Badge, LeagueStyle = it.LeagueStyle,
                    };
                    groups[k.Value.Key] = g; result.Add(g);
                }
                g.Children.Add(it);
            }
            // Every season is its own poster, even with one episode so far.
            foreach (var g in result.Where(x => x.IsGroup))
                g.Children = g.Children.OrderBy(c => c.Episode).ThenBy(c => c.Added).ToList();
            return result;
        }
    }

    /// <summary>
    /// The Posters folder (D:\DevelopmentFiles\Shelf\Posters): an image named after a show, movie or season is that
    /// one's poster in the library. The most specific name wins: "Spawn Director's Cut.jpg" for the Director's Cut,
    /// "Spawn.jpg" for every Spawn; "NFL RedZone 2024.jpg" for that season, "NFL RedZone.jpg" for all of them;
    /// "The Office Season 2.jpg", "SportsCenter.jpg" (every recording of it)… Letters and numbers count, not
    /// punctuation or case.
    /// </summary>
    public static class PosterFolder
    {
        static Dictionary<string, string> files;
        static DateTime loaded;
        static readonly string[] Types = { ".jpg", ".jpeg", ".png", ".webp" };

        static string Key(string s)
        {
            var t = (s ?? "").ToLowerInvariant().Replace("&", "and");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"^the\s+", "");
            return System.Text.RegularExpressions.Regex.Replace(t, "[^a-z0-9]", "");
        }

        static void Load()
        {
            if (files != null && DateTime.Now - loaded < TimeSpan.FromSeconds(20)) return; // new posters show up on the next look
            var f = new Dictionary<string, string>();
            try
            {
                foreach (var e in Fs.Walk(Storage.Folder("Posters")))
                    if (Types.Contains(Path.GetExtension(e.Name).ToLowerInvariant()))
                    {
                        var k = Key(Path.GetFileNameWithoutExtension(e.Name).Replace(".poster", ""));
                        if (k.Length > 0 && !f.ContainsKey(k)) f[k] = e.Path;
                    }
            }
            catch { }
            files = f; loaded = DateTime.Now;
        }

        /// <summary>The names this item could have a poster under, most specific first.</summary>
        static IEnumerable<string> Names(Item it)
        {
            var title = it.Title ?? "";
            var noYear = System.Text.RegularExpressions.Regex.Replace(title, @"\s*\((19|20)\d{2}\)", "").Trim();
            var year = System.Text.RegularExpressions.Regex.Match(title, @"(19|20)\d{2}").Value;
            var ep = it.Episode ?? "";
            switch (it.Kind)
            {
                case Kind.Movie:
                    if (ep.Length > 0) { yield return title + " " + ep; yield return noYear + " " + ep; }
                    yield return title; yield return noYear;
                    break;
                case Kind.TvShow:
                    var season = System.Text.RegularExpressions.Regex.Match(ep, @"Season \d+").Value;
                    if (it.IsGroup) yield return title; // "The Office · Season 2"
                    if (season.Length > 0) yield return title + " " + season;
                    yield return System.Text.RegularExpressions.Regex.Replace(title, @"\s*·\s*Season \d+$", "");
                    break;
                case Kind.SportsEvent:
                case Kind.SportsRecording:
                    if (it.IsGroup)
                    {
                        yield return title; // "NFL RedZone · 2024"
                        yield return System.Text.RegularExpressions.Regex.Replace(title, @"\s*·\s*(19|20)\d{2}$", "");
                    }
                    else
                    {
                        if (it.Away.Length > 0 && it.Home.Length > 0) { yield return $"{it.Away} at {it.Home} {year}"; yield return $"{it.Away} at {it.Home}"; yield return $"{it.Away} vs {it.Home}"; }
                        if (ep.Length > 0) yield return title + " " + ep;
                        yield return title;
                    }
                    break;
                default:
                    yield return title; // a recording: every recording of that show
                    break;
            }
        }

        public static string Find(Item it)
        {
            Load();
            if (files.Count == 0) return null;
            foreach (var n in Names(it))
            {
                var k = Key(n);
                if (k.Length > 0 && files.TryGetValue(k, out var path)) return path;
            }
            return null;
        }
    }
}
