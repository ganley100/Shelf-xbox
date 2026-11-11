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
