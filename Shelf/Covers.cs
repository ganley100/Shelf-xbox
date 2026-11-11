using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace Shelf
{
    /// <summary>Covers for books, PDFs and comics, made once and kept in the app's cache.</summary>
    public static class Covers
    {
        static readonly string[] Img = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

        public static async Task<BitmapImage> FromBytesAsync(byte[] bytes, int height)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var b = new BitmapImage { DecodePixelHeight = height };
            using (var ms = new InMemoryRandomAccessStream())
            {
                await ms.WriteAsync(bytes.AsBuffer());
                ms.Seek(0);
                await b.SetSourceAsync(ms);
            }
            return b;
        }

        public static async Task<BitmapImage> GetAsync(Item it)
        {
            var name = LiveTv.Hash(it.Path) + "-" + it.Size + ".img";
            var dir = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("covers", CreationCollisionOption.OpenIfExists);
            byte[] bytes;
            if (await dir.TryGetItemAsync(name) is StorageFile cached) bytes = (await FileIO.ReadBufferAsync(cached)).ToArray();
            else
            {
                if (it.Kind == Kind.Pdf) bytes = await PdfCoverAsync(it.Path);
                else if (it.Kind == Kind.Book) bytes = await Task.Run(() => EpubCover(it.Path));
                else bytes = await Task.Run(() => ZipFirstImage(it.Path));
                if (bytes == null || bytes.Length == 0) return null;
                var f = await dir.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBytesAsync(f, bytes);
            }
            return await FromBytesAsync(bytes, 300);
        }

        static async Task<byte[]> PdfCoverAsync(string path)
        {
            using (var fs = Fs.OpenRead(path))
            {
                var doc = await PdfDocument.LoadFromStreamAsync(fs.AsRandomAccessStream());
                if (doc.PageCount == 0) return null;
                using (var page = doc.GetPage(0))
                using (var ms = new InMemoryRandomAccessStream())
                {
                    await page.RenderToStreamAsync(ms, new PdfPageRenderOptions { DestinationHeight = 450 });
                    var buf = new byte[ms.Size];
                    ms.Seek(0);
                    await ms.ReadAsync(buf.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);
                    return buf;
                }
            }
        }

        /// <summary>
        /// A recording's picture: the one saved when it was recorded; else the guide's picture for that show
        /// (by channel and time, or the same title on any channel); else a frame from the video; else the
        /// channel's logo. Whatever is found is saved next to the others in Data\recordings.
        /// </summary>
        public static async Task<byte[]> RecordingArtAsync(Item it)
        {
            var saved = Recorder.ArtPath(it.Path);
            if (Fs.Exists(saved)) return await Task.Run(() => Fs.ReadAllBytes(saved));
            await LiveTv.InitAsync();

            // "Channel - Title - 2026-11-15 1300"
            var parts = Path.GetFileNameWithoutExtension(it.Path).Split(new[] { " - " }, StringSplitOptions.None);
            string channelName = parts.Length >= 3 ? parts[0] : "";
            string title = parts.Length >= 3 ? string.Join(" - ", parts.Skip(1).Take(parts.Length - 2)) : "";
            long when = 0;
            if (parts.Length >= 3 && DateTime.TryParseExact(parts.Last(), "yyyy-MM-dd HHmm", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal, out var t))
                when = new DateTimeOffset(t).ToUnixTimeMilliseconds();
            var channel = LiveTv.Channels.FirstOrDefault(c => string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));

            string url = null;
            if (channel != null && when > 0)
                url = LiveTv.Guide.FirstOrDefault(p => p.ChannelId == channel.Id && p.Start <= when + 60_000 && p.End > when && p.Image.Length > 0)?.Image;
            if (url == null && title.Length > 0)
                url = LiveTv.Guide.FirstOrDefault(p => p.Image.Length > 0 && string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase))?.Image;
            byte[] bytes = url == null ? null : await LiveTv.ImageBytesAsync(url);
            if (bytes == null) bytes = await VideoFrameAsync(it);
            if (bytes == null && channel != null && channel.Logo.Length > 0) bytes = await LiveTv.ImageBytesAsync(channel.Logo);
            if (bytes != null) try { Fs.WriteAllBytes(saved, bytes); } catch { }
            return bytes;
        }

        /// <summary>
        /// A frame from a video. Windows only makes thumbnails of files it may open itself, so the first part of
        /// the video is copied into the app's cache and Windows makes the thumbnail from that.
        /// </summary>
        public static async Task<byte[]> VideoFrameAsync(Item it)
        {
            var dir = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("frames", CreationCollisionOption.OpenIfExists);
            var name = LiveTv.Hash(it.Path) + "-" + it.Size + ".img";
            if (await dir.TryGetItemAsync(name) is StorageFile done)
            {
                var b = (await FileIO.ReadBufferAsync(done)).ToArray();
                return b.Length > 0 ? b : null; // an empty file means "no frame": don't try again
            }
            byte[] result = null;
            StorageFile part = null;
            try
            {
                part = await dir.CreateFileAsync("part-" + LiveTv.Hash(it.Path) + it.Extension, CreationCollisionOption.ReplaceExisting);
                await Task.Run(async () =>
                {
                    using (var src = Fs.OpenRead(it.Path))
                    using (var dst = await part.OpenStreamForWriteAsync())
                    {
                        var buf = new byte[1024 * 1024];
                        long left = 24L * 1024 * 1024;
                        int n;
                        while (left > 0 && (n = src.Read(buf, 0, (int)Math.Min(buf.Length, left))) > 0) { dst.Write(buf, 0, n); left -= n; }
                    }
                });
                using (var th = await part.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, 320))
                {
                    if (th != null && th.Type == Windows.Storage.FileProperties.ThumbnailType.Image && th.Size > 0)
                    {
                        result = new byte[th.Size];
                        await th.ReadAsync(result.AsBuffer(), (uint)th.Size, InputStreamOptions.None);
                    }
                }
            }
            catch { }
            finally { try { if (part != null) await part.DeleteAsync(); } catch { } }
            try { var f = await dir.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); await FileIO.WriteBytesAsync(f, result ?? new byte[0]); } catch { }
            return result;
        }

        public static string NaturalKey(string s) => Regex.Replace(s.ToLowerInvariant(), @"\d+", m => m.Value.PadLeft(10, '0'));

        static byte[] ZipFirstImage(string path)
        {
            using (var s = Fs.OpenRead(path))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                var first = zip.Entries.Where(x => Img.Contains(Path.GetExtension(x.FullName).ToLowerInvariant()) && !x.FullName.StartsWith("__MACOSX"))
                    .OrderBy(x => NaturalKey(x.FullName)).FirstOrDefault();
                return first == null ? null : Read(first);
            }
        }

        /// <summary>The cover named in the OPF (EPUB 2 meta or EPUB 3 cover-image), else the first picture.</summary>
        static byte[] EpubCover(string path)
        {
            using (var s = Fs.OpenRead(path))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                var container = zip.GetEntry("META-INF/container.xml");
                if (container != null)
                {
                    XDocument c; using (var cs = container.Open()) c = XDocument.Load(cs);
                    var opfPath = c.Descendants().FirstOrDefault(x => x.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
                    var opfEntry = opfPath == null ? null : zip.GetEntry(opfPath);
                    if (opfEntry != null)
                    {
                        XDocument opf; using (var os = opfEntry.Open()) opf = XDocument.Load(os);
                        var items = opf.Descendants().Where(x => x.Name.LocalName == "item").ToList();
                        var coverId = opf.Descendants().FirstOrDefault(x => x.Name.LocalName == "meta" && (string)x.Attribute("name") == "cover")?.Attribute("content")?.Value;
                        var it = items.FirstOrDefault(x => ((string)x.Attribute("properties") ?? "").Contains("cover-image"))
                                 ?? items.FirstOrDefault(x => coverId != null && (string)x.Attribute("id") == coverId);
                        if (it != null)
                        {
                            var dir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? "";
                            var href = Uri.UnescapeDataString((string)it.Attribute("href"));
                            var e = zip.GetEntry((dir.Length > 0 ? dir + "/" : "") + href) ?? zip.GetEntry(href);
                            if (e != null) return Read(e);
                        }
                    }
                }
                var img = zip.Entries.FirstOrDefault(x => Img.Contains(Path.GetExtension(x.FullName).ToLowerInvariant()) && x.Name.ToLowerInvariant().Contains("cover"))
                          ?? zip.Entries.FirstOrDefault(x => Img.Contains(Path.GetExtension(x.FullName).ToLowerInvariant()));
                return img == null ? null : Read(img);
            }
        }

        static byte[] Read(ZipArchiveEntry e)
        {
            using (var es = e.Open()) using (var ms = new MemoryStream()) { es.CopyTo(ms); return ms.ToArray(); }
        }
    }
}
