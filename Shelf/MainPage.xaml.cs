using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    public sealed partial class MainPage : Page
    {
        List<Item> all = new List<Item>();

        public MainPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
            Tabs.ItemsSource = Library.Categories.Select(c => c.Name).ToList();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (all.Count > 0 && e.NavigationMode == NavigationMode.Back) return;
            Status.Text = "Loading your library…";
            await UploadServer.StartAsync();
            UploadServer.Uploaded -= OnUploaded;
            UploadServer.Uploaded += OnUploaded;
            all = await Library.ScanAsync();
            if (Tabs.SelectedIndex < 0) Tabs.SelectedIndex = 0; else Show();
        }

        void OnUploaded()
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, async () => { all = await Library.ScanAsync(); Show(); });
        }

        void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => Show();

        void Show()
        {
            var cat = Library.Categories[Math.Max(0, Tabs.SelectedIndex)];
            var shown = cat.Kinds == null ? all : all.Where(i => cat.Kinds.Contains(i.Kind)).ToList();
            Grid.ItemsSource = shown;
            var where = Storage.OnDevelopmentFiles
                ? Storage.XboxRoot
                : Storage.Root.Path + "  (couldn't open " + Storage.XboxRoot + ": " + Storage.Problem + ")";
            Status.Text = (shown.Count == 0
                ? "Nothing here yet. Put files in the folders under " + where + " (Device Portal → File explorer → DevelopmentFiles)."
                : shown.Count + (shown.Count == 1 ? " item" : " items") + " · " + where)
                + (UploadServer.Address != null ? "\nAdd files from your phone: " + UploadServer.Address : "");
        }

        async void Grid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            if (args.Item is Item it) await Library.LoadCoverAsync(it);
        }

        async void Grid_ItemClick(object sender, ItemClickEventArgs e)
        {
            var it = (Item)e.ClickedItem;
            switch (it.Kind)
            {
                case Kind.Movie:
                case Kind.Music:
                case Kind.Podcast:
                case Kind.Audiobook:
                    Frame.Navigate(typeof(PlayerPage), it);
                    break;
                case Kind.Photo:
                    Frame.Navigate(typeof(ImagePage), it);
                    break;
                case Kind.Pdf:
                case Kind.Comic:
                case Kind.Manga:
                    Frame.Navigate(typeof(PagesPage), it);
                    break;
                case Kind.Book:
                case Kind.Text:
                    Frame.Navigate(typeof(ReaderPage), it);
                    break;
                default:
                    await new MessageDialog(it.Badge.ToLower() + " files open in the next build.", it.Title).ShowAsync();
                    break;
            }
        }
    }
}
