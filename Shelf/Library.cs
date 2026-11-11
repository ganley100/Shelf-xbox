using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;
using Windows.UI.Xaml.Media.Imaging;

namespace Shelf
{
    public enum Kind { Book, Pdf, Text, Comic, Manga, Audiobook, Music, Podcast, Movie, Photo, Game }

    public sealed class Item : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public string Title { get; set; }
        public StorageFile File { get; set; }
        public Kind Kind { get; set; }
        public string Badge { get; set; }
        BitmapImage cover;
        public BitmapImage Cover
        {
            get => cover;
            set { cover = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Cover))); }
        }
        public string Initials => string.IsNullOrEmpty(Title) ? "?" : Title;
        public DateTimeOffset Added { get; set; }
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
            ("Movies", new[] { Kind.Movie }),
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
                case Kind.Photo: return "PHOTO";
                default: return "GAME";
            }
        }

        /// <summary>Every item in the Shelf folders, newest first.</summary>
        public static async Task<List<Item>> ScanAsync()
        {
            var items = new List<Item>();
            foreach (var name in Storage.Folders)
            {
                if (name == "Saves" || name == "Data") continue;
                var folder = await Storage.FolderAsync(name);
                var q = folder.CreateFileQueryWithOptions(new QueryOptions(CommonFileQuery.DefaultQuery, new[] { "*" })
                {
                    FolderDepth = FolderDepth.Deep,
                    IndexerOption = IndexerOption.DoNotUseIndexer,
                });
                IReadOnlyList<StorageFile> files;
                try { files = await q.GetFilesAsync(); } catch { continue; }
                foreach (var f in files)
                {
                    var k = KindFor(name, Path.GetExtension(f.Name));
                    if (k == null) continue;
                    var kind = name == "Recordings" && k == Kind.Movie ? Kind.Movie : k.Value;
                    items.Add(new Item
                    {
                        Title = Path.GetFileNameWithoutExtension(f.Name).Replace('_', ' '),
                        File = f,
                        Kind = kind,
                        Badge = BadgeFor(kind),
                        Added = f.DateCreated,
                    });
                }
            }
            return items.OrderByDescending(i => i.Added).ToList();
        }

        /// <summary>
        /// The item's cover: the first page of PDFs and comics, an EPUB's cover picture, and Windows'
        /// thumbnails for photos, videos and music. Cached as JPEGs in the app's cache.
        /// </summary>
        public static async Task LoadCoverAsync(Item it)
        {
            if (it.Cover != null) return;
            if (it.Kind == Kind.Pdf || it.Kind == Kind.Comic || it.Kind == Kind.Manga || it.Kind == Kind.Book)
            {
                try { it.Cover = await Covers.GetAsync(it); } catch { }
                return;
            }
            if (it.Kind != Kind.Photo && it.Kind != Kind.Movie && it.Kind != Kind.Music && it.Kind != Kind.Audiobook && it.Kind != Kind.Podcast) return;
            try
            {
                using (var t = await it.File.GetThumbnailAsync(it.Kind == Kind.Photo ? ThumbnailMode.PicturesView : ThumbnailMode.SingleItem, 320))
                {
                    if (t == null || t.Type != ThumbnailType.Image) return;
                    var b = new BitmapImage();
                    await b.SetSourceAsync(t);
                    it.Cover = b;
                }
            }
            catch { }
        }
    }
}
