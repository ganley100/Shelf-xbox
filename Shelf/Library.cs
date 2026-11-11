using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;

namespace Shelf
{
    public enum Kind { Book, Pdf, Text, Comic, Manga, Audiobook, Music, Podcast, Movie, Recording, Photo, Game }

    public sealed class Item : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public string Title { get; set; }
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
            }
        }
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
            ("Movies", new[] { Kind.Movie, Kind.Recording }),
            ("Recorded", new[] { Kind.Recording }),
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
            if (Roms.Contains(ext)) return Kind.Game;
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
        public static Task<List<Item>> ScanAsync() => Task.Run(() =>
        {
            var items = new List<Item>();
            foreach (var name in Storage.Folders)
            {
                if (name == "Saves" || name == "Data") continue;
                foreach (var f in Fs.Walk(Storage.Folder(name)))
                {
                    if (f.Name.StartsWith(".") || f.Name.EndsWith(".part")) continue;
                    var k = KindFor(name, Path.GetExtension(f.Name));
                    if (k == null) continue;
                    if (name == "Recordings" && k == Kind.Movie) k = Kind.Recording;
                    items.Add(new Item
                    {
                        Title = Path.GetFileNameWithoutExtension(f.Name).Replace('_', ' '),
                        Path = f.Path, Size = f.Size, Kind = k.Value, Badge = BadgeFor(k.Value),
                        Added = f.Created > f.Modified ? f.Created : f.Modified,
                    });
                }
            }
            return items.OrderByDescending(i => i.Added).ToList();
        });

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
                else if (it.Kind == Kind.Movie) it.Cover = await Covers.FromBytesAsync(await Covers.VideoFrameAsync(it), 300);
            }
            catch { }
        }
    }
}
