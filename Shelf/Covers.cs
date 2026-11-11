using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace Shelf
{
    /// <summary>Covers for books, PDFs and comics, made once and kept in the cache.</summary>
    public static class Covers
    {
        static readonly string[] Img = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

        public static async Task<BitmapImage> GetAsync(Item it)
        {
            var size = (await it.File.GetBasicPropertiesAsync()).Size;
            var name = ((uint)it.File.Path.GetHashCode()).ToString("x8") + "-" + size + ".img";
            var dir = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("covers", CreationCollisionOption.OpenIfExists);
            var cached = await dir.TryGetItemAsync(name) as StorageFile;
            if (cached == null)
            {
                byte[] bytes = null;
                if (it.Kind == Kind.Pdf) bytes = await PdfCoverAsync(it.File);
                else if (it.Kind == Kind.Book) bytes = await EpubCoverAsync(it.File);
                else bytes = await ZipFirstImageAsync(it.File);
                if (bytes == null || bytes.Length == 0) return null;
                cached = await dir.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBytesAsync(cached, bytes);
            }
            var b = new BitmapImage { DecodePixelHeight = 300 };
            using (var s = await cached.OpenReadAsync()) await b.SetSourceAsync(s);
            return b;
        }

        static async Task<byte[]> PdfCoverAsync(StorageFile f)
        {
            var doc = await PdfDocument.LoadFromFileAsync(f);
            if (doc.PageCount == 0) return null;
            using (var page = doc.GetPage(0))
            using (var ms = new InMemoryRandomAccessStream())
            {
                await page.RenderToStreamAsync(ms, new PdfPageRenderOptions { DestinationHeight = 450 });
                return await ReadAllAsync(ms);
            }
        }

        static async Task<byte[]> ZipFirstImageAsync(StorageFile f)
        {
            using (var s = (await f.OpenReadAsync()).AsStreamForRead())
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                var first = zip.Entries.Where(x => Img.Contains(Path.GetExtension(x.FullName).ToLowerInvariant()) && !x.FullName.StartsWith("__MACOSX"))
                    .OrderBy(x => Regex.Replace(x.FullName.ToLowerInvariant(), @"\d+", m => m.Value.PadLeft(10, '0'))).FirstOrDefault();
                return first == null ? null : Read(first);
            }
        }

        /// <summary>The cover named in the OPF (EPUB 2 meta or EPUB 3 cover-image), else the first picture.</summary>
        static async Task<byte[]> EpubCoverAsync(StorageFile f)
        {
            using (var s = (await f.OpenReadAsync()).AsStreamForRead())
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

        static async Task<byte[]> ReadAllAsync(IRandomAccessStream s)
        {
            s.Seek(0);
            var buf = new byte[s.Size];
            using (var r = new DataReader(s.GetInputStreamAt(0))) { await r.LoadAsync((uint)s.Size); r.ReadBytes(buf); }
            return buf;
        }
    }
}
