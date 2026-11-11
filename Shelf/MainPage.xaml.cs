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
        bool focusedOnce;

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
            await UploadServer.StartAsync();
            UploadServer.Uploaded -= OnUploaded;
            UploadServer.Uploaded += OnUploaded;
            all = await Library.ScanAsync();
            if (Tabs.SelectedIndex < 0) Tabs.SelectedIndex = 0; else Show();
        }

        void LiveTv_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(LiveTvPage));

        void OnUploaded()
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, async () => { all = await Library.ScanAsync(); Show(); });
        }

        void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => Show();

        void Show()
        {
            var cat = Library.Categories[Math.Max(0, Tabs.SelectedIndex)];
            var shown = cat.Kinds == null ? all : all.Where(i => cat.Kinds.Contains(i.Kind)).ToList();
            CoverGrid.ItemsSource = shown;
            PhoneHint.Text = UploadServer.Address != null ? "Add from your phone\n" + UploadServer.Address : "";
            Empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Empty.Text = all.Count == 0
                ? "Your library is empty. Add files from your phone at the address above, or with Device Portal's File explorer."
                : "Nothing in " + cat.Name + " yet.";
            if (!focusedOnce && shown.Count > 0)
            {
                // First time in: put the controller on the first cover so A opens it straight away.
                focusedOnce = true;
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => (CoverGrid.ContainerFromIndex(0) as Control)?.Focus(FocusState.Programmatic));
            }
        }

        async void CoverGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            if (args.Item is Item it) await Library.LoadCoverAsync(it);
        }

        async void CoverGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            var it = (Item)e.ClickedItem;
            switch (it.Kind)
            {
                case Kind.Movie:
                case Kind.Recording:
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
                    await new MessageDialog("Games open in the next build (built-in emulators are on the way).", it.Title).ShowAsync();
                    break;
            }
        }
    }
}
