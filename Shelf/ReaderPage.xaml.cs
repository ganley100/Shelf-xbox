using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    /// <summary>
    /// EPUB and TXT books, shown as web pages in dark reading colours. Down / A / RB = next screen,
    /// Up / LB = previous; at the end of a chapter it moves on to the next. B goes back.
    /// </summary>
    public sealed partial class ReaderPage : Page
    {
        const string Css = "html,body{background:#111 !important;color:#e8e2d6 !important;}" +
            "body{font-family:Georgia,serif;font-size:30px;line-height:1.6;max-width:1100px;margin:60px auto 120px;padding:0 60px;}" +
            "img,svg{max-width:100%;height:auto;}a{color:#f5b82e;}h1,h2,h3{color:#fff;}";

        Item item;
        string folder;            // localcache-relative folder of the extracted book
        List<string> chapters = new List<string>();
        int chapter;
        double pendingScroll;

        public ReaderPage()
        {
            InitializeComponent();
            View.NavigationCompleted += async (s, e) =>
            {
                await Js("var st=document.createElement('style');st.textContent=" + Quote(Css) + ";document.head.appendChild(st);");
                if (pendingScroll > 0)
                    await Js($"window.scrollTo(0,(document.documentElement.scrollHeight-innerHeight)*{pendingScroll.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
                pendingScroll = 0;
                UpdateCounter();
            };
        }

        static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        async Task<string> Js(string code)
        {
            try { return await View.InvokeScriptAsync("eval", new[] { code }); } catch { return ""; }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            item = (Item)e.Parameter;
            Window.Current.CoreWindow.KeyDown += OnKey;
            try
            {
                await PrepareAsync();
                var p = await Progress.GetAsync(item);
                chapter = Math.Min((int)p, Math.Max(0, chapters.Count - 1));
                pendingScroll = p - Math.Floor(p);
                Open();
            }
            catch (Exception ex)
            {
                View.NavigateToString("<body style='background:#111;color:#eee;font:28px sans-serif;padding:60px'>Couldn't open this book: " +
                    System.Net.WebUtility.HtmlEncode(ex.Message) + "</body>");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown -= OnKey;
            base.OnNavigatedFrom(e);
        }

        /// <summary>Unpacks the book once into the app's cache so the web view can show it with its pictures.</summary>
        async Task PrepareAsync()
        {
            var key = LiveTv.Hash(item.Path) + "-" + item.Size;
            folder = "books/" + key;
            var cache = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("books", CreationCollisionOption.OpenIfExists);
            var dir = await cache.CreateFolderAsync(key, CreationCollisionOption.OpenIfExists);
            var done = await dir.TryGetItemAsync(".ready");

            if (item.Kind == Kind.Text)
            {
                if (done == null)
                {
                    var text = ReadTextAnyEncoding(await Task.Run(() => Fs.ReadAllBytes(item.Path)));
                    var html = new StringBuilder("<!doctype html><html><head><meta charset='utf-8'></head><body>");
                    foreach (var para in text.Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.None))
                        html.Append("<p>").Append(System.Net.WebUtility.HtmlEncode(para).Replace("\n", "<br>")).Append("</p>");
                    html.Append("</body></html>");
                    var f = await dir.CreateFileAsync("book.html", CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteTextAsync(f, html.ToString());
                    await dir.CreateFileAsync(".ready", CreationCollisionOption.ReplaceExisting);
                }
                chapters = new List<string> { "book.html" };
                return;
            }

            if (done == null)
            {
                using (var s = Fs.OpenRead(item.Path))
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    foreach (var en in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(en.Name)) continue;
                        var parts = en.FullName.Replace('\\', '/').Split('/').Where(x => x.Length > 0 && x != "..").ToArray();
                        var target = dir;
                        for (int i = 0; i < parts.Length - 1; i++) target = await target.CreateFolderAsync(parts[i], CreationCollisionOption.OpenIfExists);
                        var file = await target.CreateFileAsync(parts.Last(), CreationCollisionOption.ReplaceExisting);
                        using (var src = en.Open())
                        using (var dst = await file.OpenStreamForWriteAsync())
                            await src.CopyToAsync(dst);
                    }
                }
                await dir.CreateFileAsync(".ready", CreationCollisionOption.ReplaceExisting);
            }

            // container.xml -> the OPF -> spine order.
            var container = XDocument.Parse(await FileIO.ReadTextAsync(await dir.GetFileAsync(@"META-INF\container.xml")));
            var opfPath = container.Descendants().First(x => x.Name.LocalName == "rootfile").Attribute("full-path").Value;
            var opfFile = await StorageFile.GetFileFromPathAsync(Path.Combine(dir.Path, opfPath.Replace('/', '\\')));
            var opf = XDocument.Parse(await FileIO.ReadTextAsync(opfFile));
            var baseDir = Path.GetDirectoryName(opfPath.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
            var manifest = opf.Descendants().Where(x => x.Name.LocalName == "item")
                .ToDictionary(x => (string)x.Attribute("id"), x => (string)x.Attribute("href"));
            chapters = opf.Descendants().Where(x => x.Name.LocalName == "itemref")
                .Select(x => (string)x.Attribute("idref")).Where(manifest.ContainsKey)
                .Select(id => (baseDir.Length > 0 ? baseDir + "/" : "") + Uri.UnescapeDataString(manifest[id]))
                .ToList();
        }

        static string ReadTextAnyEncoding(byte[] bytes)
        {
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch { return Encoding.GetEncoding("iso-8859-1").GetString(bytes); }
        }

        void Open()
        {
            if (chapters.Count == 0) return;
            var path = string.Join("/", chapters[chapter].Split('/').Select(Uri.EscapeDataString));
            View.Navigate(new Uri("ms-appdata:///localcache/" + folder + "/" + path));
        }

        async void UpdateCounter()
        {
            var frac = await Fraction();
            Counter.Text = chapters.Count > 1 ? $"Chapter {chapter + 1} of {chapters.Count}" : $"{Math.Round(frac * 100)}%";
            await Progress.SetAsync(item, chapter + Math.Min(frac, 0.999));
        }

        async Task<double> Fraction()
        {
            var r = await Js("(function(){var m=document.documentElement.scrollHeight-innerHeight;return m>0?String(scrollY/m):'0';})()");
            return double.TryParse(r, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        async void OnKey(CoreWindow sender, KeyEventArgs e)
        {
            int dir = 0;
            switch (e.VirtualKey)
            {
                case VirtualKey.GamepadDPadDown: case VirtualKey.GamepadLeftThumbstickDown: case VirtualKey.GamepadA:
                case VirtualKey.Down: case VirtualKey.PageDown: case VirtualKey.Space: dir = 1; break;
                case VirtualKey.GamepadDPadUp: case VirtualKey.GamepadLeftThumbstickUp: case VirtualKey.Up: case VirtualKey.PageUp: dir = -1; break;
                case VirtualKey.GamepadRightShoulder: case VirtualKey.GamepadDPadRight:
                    if (chapter < chapters.Count - 1) { e.Handled = true; chapter++; Open(); } return;
                case VirtualKey.GamepadLeftShoulder: case VirtualKey.GamepadDPadLeft:
                    if (chapter > 0) { e.Handled = true; chapter--; Open(); } return;
            }
            if (dir == 0) return;
            e.Handled = true;
            var at = await Js(dir > 0
                ? "(function(){var b=scrollY+innerHeight>=document.documentElement.scrollHeight-4;if(!b)scrollBy(0,innerHeight*0.85);return b?'end':'';})()"
                : "(function(){var t=scrollY<=2;if(!t)scrollBy(0,-innerHeight*0.85);return t?'top':'';})()");
            if (at == "end" && chapter < chapters.Count - 1) { chapter++; Open(); return; }
            if (at == "top" && chapter > 0) { chapter--; pendingScroll = 0.999; Open(); return; }
            UpdateCounter();
        }
    }
}
