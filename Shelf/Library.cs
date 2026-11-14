using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;

namespace Shelf
{
    public enum Kind { Book, Pdf, Text, Comic, Manga, Audiobook, Music, Podcast, Movie, Recording, SportsRecording, Photo, Game }

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
        public string LeagueUpper => (League ?? "").ToUpperInvariant();
        BitmapImage logoA, logoB;
        public BitmapImage LogoA { get => logoA; set { logoA = value; Raise(nameof(LogoA)); Raise(nameof(LogosVisibility)); Raise(nameof(NamesVisibility)); } }
        public BitmapImage LogoB { get => logoB; set { logoB = value; Raise(nameof(LogoB)); Raise(nameof(LogosVisibility)); Raise(nameof(NamesVisibility)); } }
        void Raise(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
        public Windows.UI.Xaml.Visibility MatchupVisibility => Kind == Kind.SportsRecording ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
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
        public Windows.UI.Xaml.Visibility BookTileVisibility => Vis(cover == null && !IsGame && Kind != Kind.SportsRecording);
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
        public string BadgeText => IsGame ? SystemShort : Kind == Kind.Recording ? "RECORDED" : Kind == Kind.SportsRecording ? "SPORTS" : Kind == Kind.Movie ? "VIDEO" : Badge;

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
                    default: return $"{Title}  ·  Video";
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

        public static readonly (string Name, Kind[] Kinds)[] Categories =
        {
            ("All", null),
            ("Books", new[] { Kind.Book, Kind.Text }),
            ("Audiobooks", new[] { Kind.Audiobook }),
            ("Music", new[] { Kind.Music }),
            ("Podcasts", new[] { Kind.Podcast }),
            ("Movies", new[] { Kind.Movie, Kind.Recording, Kind.SportsRecording }),
            ("Recorded", new[] { Kind.Recording, Kind.SportsRecording }),
            ("Recorded Sports", new[] { Kind.SportsRecording }),
            ("Photos", new[] { Kind.Photo }),
            ("Games", new[] { Kind.Game }),
            ("Comics", new[] { Kind.Comic }),
            ("Manga", new[] { Kind.Manga }),
            ("PDFs", new[] { Kind.Pdf }),
        };

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
                case Kind.Movie: return "VIDEO";
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
        public static Task<List<Item>> ScanAsync() => Task.Run(async () =>
        {
            var items = new List<Item>();
            foreach (var name in Storage.Folders)
            {
                if (name == "Saves" || name == "Data") continue;
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
                    if (name == "Recordings" && k == Kind.Movie) k = Kind.Recording;
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
                    items.Add(item);
                }
            }
            // Recordings of a game whose listing didn't name the teams ("College Football" on TNT): ask ESPN which
            // game that channel was showing, and remember the answer with the recording.
            foreach (var it in items.Where(i => i.Kind == Kind.Recording).ToList())
                try { await SportsFromEspnAsync(it); } catch { }
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
                else if (it.Kind == Kind.Recording) it.Cover = await Covers.FromBytesAsync(await Covers.RecordingArtAsync(it), 300);
                else if (it.Kind == Kind.SportsRecording)
                {
                    // Matchup card: the teams' logos (saved with the recording, or known since).
                    var a = !string.IsNullOrEmpty(it.LogoUrlA) ? it.LogoUrlA : TeamLogos.LogoUrl(it.Away);
                    var b = !string.IsNullOrEmpty(it.LogoUrlB) ? it.LogoUrlB : TeamLogos.LogoUrl(it.Home);
                    if (a != null && b != null)
                    {
                        var la = await ChannelRow.LoadAsync(a, 200); var lb = await ChannelRow.LoadAsync(b, 200);
                        if (la != null && lb != null) { it.LogoA = la; it.LogoB = lb; }
                    }
                    else TeamLogos.LookUpNow(new[] { (it.Away, it.League), (it.Home, it.League) });
                }
                else if (it.Kind == Kind.Movie) it.Cover = await Covers.FromBytesAsync(await Covers.VideoFrameAsync(it), 300);
            }
            catch { }
        }
    }
}
