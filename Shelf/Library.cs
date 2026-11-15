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
            }
        }
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
        public string BadgeText => IsGame ? SystemShort : Kind == Kind.Recording ? "RECORDED" : Kind == Kind.SportsRecording ? "SPORTS" : Kind == Kind.Movie ? (Episode.Length > 0 ? Episode.ToUpperInvariant() : "MOVIE") : Kind == Kind.TvShow ? "TV" : Kind == Kind.SportsEvent ? (Episode.Length > 0 && Episode.Length <= 16 && Home.Length == 0 ? Episode.ToUpperInvariant() : "SPORTS") : Badge;

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

        /// <summary>Covers: book covers, first pages, and photos themselves (made small).</summary>
        public static async Task LoadCoverAsync(Item it)
        {
            if (it.Cover != null) return;
            try
            {
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
                    // An event without teams (WrestleMania, SummerSlam): TheSportsDB's art of it; else its name on a colour.
                    if (it.Home.Length == 0)
                    {
                        var th = SportsEvents.Thumb(it);
                        if (th.Length > 0) it.Cover = await Covers.ThumbFromBytesAsync(await LiveTv.ImageBytesAsync(th), 300);
                        return;
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
                    // The movie from TMDB (found from the file name): its backdrop; else a frame from the video.
                    var m = await Movies.LookUpAsync(it.Path);
                    // A wide thumbnail (the movie's backdrop) first; its poster otherwise.
                    // Each edition its own picture: Director's Cut, Extended… get a different backdrop of the movie; a black
                    // and white edition is shown in black and white.
                    var bw = Movies.IsBlackAndWhite(it.Episode);
                    var pic = m != null ? Movies.BackdropFor(m, it.Episode) : "";
                    byte[] bytes = pic.Length > 0 ? await LiveTv.ImageBytesAsync(pic) : null;
                    if (bytes == null) bytes = await Covers.VideoFrameAsync(it); // never its poster
                    it.Cover = bw ? await Covers.GrayFromBytesAsync(bytes, 300) : await Covers.ThumbFromBytesAsync(bytes, 300);
                    // The movie's title logo over it (in black and white for those editions too).
                    if (it.Cover != null && m != null && m.Logo.Length > 0)
                    {
                        var lb = await LiveTv.ImageBytesAsync(m.Logo);
                        it.LogoArt = bw ? await Covers.GrayFromBytesAsync(lb, 120, keepAlpha: true) : await Covers.FromBytesAsync(lb, 120);
                    }
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
        public static string LogoPath(string file, string side) => Storage.DataFolder("sports") + "\\" + Path.GetFileName(file) + "." + side + ".img";

        static Windows.Data.Json.JsonObject Meta(Item it)
        {
            try { var p = MetaPath(it.Path); if (Fs.Exists(p)) return Windows.Data.Json.JsonObject.Parse(Fs.ReadAllText(p)); } catch { }
            return null;
        }

        /// <summary>TheSportsDB's wide picture of an event without teams (a wrestling show's art), if any.</summary>
        public static string Thumb(Item it) => Meta(it)?.GetNamedString("thumb", "") ?? "";

        /// <summary>Raised when the scraper learns new kinds of names, so earlier misses are looked up again.</summary>
        const int ScraperVersion = 3;

        public static bool Checked(Item it)
        {
            var m = Meta(it);
            if (m == null) return false;
            // Not found: try again after a week (TheSportsDB keeps adding old events).
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
            var a = t.Groups[1].Value.Trim(' ', '-'); var b = t.Groups[3].Value.Trim(' ', '-');
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
            if (m != null && m.GetNamedString("home", "").Length == 0 && m.GetNamedString("event", "").Length > 0)
            {
                // An event without teams (wrestling, RedZone): its name on the thumbnail.
                it.Away = m.GetNamedString("event", ""); it.Home = ""; it.Sep = "";
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
            void Put0(string k, string v) => meta[k] = Windows.Data.Json.JsonValue.CreateStringValue(v ?? "");
            // NFL RedZone (a whip-around show, not a game): its week from ESPN's schedule.
            if (ev == null && p.Sep == "event" && System.Text.RegularExpressions.Regex.IsMatch(name, @"red\s*zone", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                Put0("event", "NFL RedZone"); Put0("league", "NFL"); Put0("date", p.Date);
                if (p.Date.Length == 10)
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
                Put("home", S(ev, "strHomeTeam")); Put("away", S(ev, "strAwayTeam")); Put("thumb", S(ev, "strThumb"));
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
}
