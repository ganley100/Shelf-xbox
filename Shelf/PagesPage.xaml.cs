using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    /// <summary>
    /// Page-by-page reading: PDFs and CBZ comics. Manga reads right to left (left = next page).
    /// D-pad / stick left-right or LB/RB turn pages; B goes back.
    /// </summary>
    public sealed partial class PagesPage : Page
    {
        Item item;
        PdfDocument pdf;
        ZipArchive zip;
        Stream zipStream;
        List<ZipArchiveEntry> entries;
        int count, index;

        public PagesPage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            item = (Item)e.Parameter;
            Window.Current.CoreWindow.KeyDown += OnKey;
            if (item.Kind == Kind.Pdf)
            {
                pdf = await PdfDocument.LoadFromFileAsync(item.File);
                count = (int)pdf.PageCount;
            }
            else
            {
                zipStream = (await item.File.OpenReadAsync()).AsStreamForRead();
                zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
                var img = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
                entries = zip.Entries.Where(x => img.Contains(Path.GetExtension(x.FullName).ToLowerInvariant()) && !x.FullName.StartsWith("__MACOSX"))
                    .OrderBy(x => NaturalKey(x.FullName)).ToList();
                count = entries.Count;
            }
            index = Math.Min(Math.Max(0, (int)await Progress.GetAsync(item)), Math.Max(0, count - 1));
            await ShowAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown -= OnKey;
            zip?.Dispose(); zipStream?.Dispose();
            base.OnNavigatedFrom(e);
        }

        static string NaturalKey(string s) => Regex.Replace(s.ToLowerInvariant(), @"\d+", m => m.Value.PadLeft(10, '0'));

        bool RightToLeft => item.Kind == Kind.Manga;

        async void OnKey(CoreWindow sender, KeyEventArgs e)
        {
            int step = 0;
            switch (e.VirtualKey)
            {
                case VirtualKey.GamepadDPadRight: case VirtualKey.GamepadLeftThumbstickRight: case VirtualKey.Right:
                    step = RightToLeft ? -1 : 1; break;
                case VirtualKey.GamepadDPadLeft: case VirtualKey.GamepadLeftThumbstickLeft: case VirtualKey.Left:
                    step = RightToLeft ? 1 : -1; break;
                case VirtualKey.GamepadRightShoulder: case VirtualKey.GamepadA: case VirtualKey.PageDown: step = 1; break;
                case VirtualKey.GamepadLeftShoulder: case VirtualKey.PageUp: step = -1; break;
            }
            if (step == 0) return;
            e.Handled = true;
            var next = Math.Min(Math.Max(0, index + step), count - 1);
            if (next == index) return;
            index = next;
            await ShowAsync();
        }

        async Task ShowAsync()
        {
            if (count == 0) { Counter.Text = "No pages found"; return; }
            var b = new BitmapImage();
            if (pdf != null)
            {
                using (var page = pdf.GetPage((uint)index))
                using (var ms = new InMemoryRandomAccessStream())
                {
                    var h = (uint)Math.Max(1080, ActualHeight * 1.5);
                    await page.RenderToStreamAsync(ms, new PdfPageRenderOptions { DestinationHeight = h });
                    await b.SetSourceAsync(ms);
                }
            }
            else
            {
                using (var es = entries[index].Open())
                using (var ms = new InMemoryRandomAccessStream())
                {
                    await es.CopyToAsync(ms.AsStreamForWrite());
                    ms.Seek(0);
                    await b.SetSourceAsync(ms);
                }
            }
            Picture.Source = b;
            Counter.Text = $"{index + 1} / {count}" + (RightToLeft ? "  ·  right to left" : "");
            await Progress.SetAsync(item, index);
        }
    }
}
