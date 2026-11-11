using System;
using Windows.Storage;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    /// <summary>Photos, full screen.</summary>
    public sealed partial class ImagePage : Page
    {
        public ImagePage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var it = (Item)e.Parameter;
            Picture.Source = await Covers.FromBytesAsync(await System.Threading.Tasks.Task.Run(() => Fs.ReadAllBytes(it.Path)), 2160);
        }
    }
}
