using System;
using System.Collections.Generic;
using System.Linq;
using Windows.System;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    /// <summary>
    /// The library, laid out like the Android TV version: header with item count, Live TV and Add media,
    /// category chips (LB/RB switch them), the highlighted item's details, and covers on a shared baseline
    /// with a gold outline on the highlighted one.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        List<Item> all = new List<Item>();
        List<Item> shown = new List<Item>();
        readonly List<Button> chips = new List<Button>();
        int category;
        bool focusedOnce;

        static readonly SolidColorBrush Gold = new SolidColorBrush(Color.FromArgb(255, 0xF5, 0xB8, 0x2E));
        static readonly SolidColorBrush ChipText = new SolidColorBrush(Color.FromArgb(255, 0xBB, 0xBB, 0xBB));

        public MainPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
            for (int i = 0; i < Library.Categories.Length; i++)
            {
                var index = i;
                var b = new Button
                {
                    Content = Library.Categories[i].Name,
                    Style = (Style)Resources["Pill"],
                    FontSize = 26, FontWeight = Windows.UI.Text.FontWeights.Normal, Padding = new Thickness(32, 10, 32, 10),
                };
                b.Click += (s, e) => Select(index);
                chips.Add(b);
                Chips.Children.Add(b);
            }
            Window.Current.CoreWindow.KeyDown += (s, e) =>
            {
                if (Frame?.Content != this) return;
                if (e.VirtualKey == VirtualKey.GamepadRightShoulder) { e.Handled = true; Select((category + 1) % chips.Count); }
                else if (e.VirtualKey == VirtualKey.GamepadLeftShoulder) { e.Handled = true; Select((category + chips.Count - 1) % chips.Count); }
            };
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (all.Count > 0 && e.NavigationMode == NavigationMode.Back) return;
            await UploadServer.StartAsync();
            UploadServer.Uploaded -= OnUploaded;
            UploadServer.Uploaded += OnUploaded;
            all = await Library.ScanAsync();
            Select(category);
        }

        void OnUploaded()
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, async () => { all = await Library.ScanAsync(); Select(category); });
        }

        void Select(int index)
        {
            category = index;
            for (int i = 0; i < chips.Count; i++)
            {
                chips[i].BorderBrush = i == index ? Gold : new SolidColorBrush(Colors.Transparent);
                chips[i].Foreground = i == index ? Gold : ChipText;
            }
            chips[index].StartBringIntoView();
            var cat = Library.Categories[index];
            shown = cat.Kinds == null ? all : all.Where(i => cat.Kinds.Contains(i.Kind)).ToList();
            CoverGrid.ItemsSource = shown;
            Subtitle.Text = "Your media library  ·  " + all.Count + (all.Count == 1 ? " item" : " items");
            DetailLine.Text = shown.FirstOrDefault()?.Detail ?? "";
            Empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Empty.Text = all.Count == 0
                ? "Your library is empty. Press + Add media to add files from your phone."
                : "Nothing in " + cat.Name + " yet.";
            if (!focusedOnce && shown.Count > 0)
            {
                // First time in: start on the first cover, so A opens it straight away.
                focusedOnce = true;
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => (CoverGrid.ContainerFromIndex(0) as Control)?.Focus(FocusState.Programmatic));
            }
        }

        void LiveTv_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(LiveTvPage));

        async void Add_Click(object sender, RoutedEventArgs e)
        {
            var address = UploadServer.Address;
            var text = address == null
                ? "Connect the Xbox to your home network, then try again."
                : "On your phone or computer (same Wi-Fi), open:\n\n" + address + "\n\nPick a shelf (Books, Music, Movies, Games…) and choose files. " +
                  "Every file shows up as its own item. You can also use Device Portal → File explorer → DevelopmentFiles → Shelf.";
            await new MessageDialog(text, "Add media").ShowAsync();
        }

        void CoverGrid_GotFocus(object sender, RoutedEventArgs e)
        {
            if ((e.OriginalSource as GridViewItem)?.Content is Item it)
            {
                foreach (var x in shown) if (x.Focused && x != it) x.Focused = false;
                it.Focused = true;
                DetailLine.Text = it.Detail;
            }
        }

        void CoverGrid_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((e.OriginalSource as GridViewItem)?.Content is Item it) it.Focused = false;
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
                case Kind.Game:
                    Frame.Navigate(typeof(GamePage), it);
                    break;
                default:
                    await new MessageDialog("Shelf can't open this file.", it.Title).ShowAsync();
                    break;
            }
        }
    }
}
