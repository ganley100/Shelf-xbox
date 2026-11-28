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
    /// The library, laid out like the Apple TV app's: a sidebar (Recently Added, Movies, TV Shows, Recordings…, Live TV,
    /// Sports, Add videos, and the genres of your movies) and rounded portrait posters, 6 to a row. The highlighted
    /// poster zooms in quickly with a white edge. LB / RB move between the sections.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        List<Item> all = new List<Item>();
        List<Item> shown = new List<Item>();
        bool focusedOnce;

        /// <summary>A section of the sidebar that shows posters: a category, or a genre.</summary>
        sealed class Section { public string Name; public string Glyph; public Func<Item, bool> Keep; public Button Row; }
        readonly List<Section> sections = new List<Section>();
        int category;
        readonly List<Button> genreRows = new List<Button>();
        TextBlock genresHeader;

        static readonly SolidColorBrush RowOn = new SolidColorBrush(Color.FromArgb(255, 0xEE, 0xEE, 0xEE));
        static readonly SolidColorBrush RowOnText = new SolidColorBrush(Color.FromArgb(255, 0x1C, 0x1C, 0x1E));
        static readonly SolidColorBrush RowOffText = new SolidColorBrush(Color.FromArgb(255, 0xE6, 0xE6, 0xE6));
        static readonly SolidColorBrush Clear = new SolidColorBrush(Colors.Transparent);

        static readonly string[] Glyphs = { "\uE823", "\uE8B2", "\uE7F4", "\uE7C8", "\uE7FC", "\uE909" };
        static readonly string[] Names = { "Recently Added", "Movies", "TV Shows", "Recordings", "Recorded Sports", "Sports Events" };

        Button Row(string glyph, string name)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
            panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 22, Width = 28 });
            panel.Children.Add(new TextBlock { Text = name, FontSize = 24, VerticalAlignment = VerticalAlignment.Center });
            return new Button { Content = panel, Style = (Style)Resources["SideRow"] };
        }

        public MainPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
            for (int i = 0; i < Library.Categories.Length; i++)
            {
                var kinds = Library.Categories[i].Kinds;
                var sec = new Section { Name = i < Names.Length ? Names[i] : Library.Categories[i].Name, Glyph = i < Glyphs.Length ? Glyphs[i] : "\uE8B7",
                                        Keep = kinds == null ? (Func<Item, bool>)(x => true) : x => kinds.Contains(x.Kind) };
                AddSection(sec);
            }
            Side.Children.Add(new Border { Height = 18 });
            foreach (var (glyph, name, click) in new (string, string, RoutedEventHandler)[]
            {
                ("\uE714", "Live TV", LiveTv_Click), ("\uE805", "Sports", Sports_Click), ("\uE710", "Add videos", Add_Click),
            })
            {
                var b = Row(glyph, name);
                b.Click += click;
                Side.Children.Add(b);
            }
            genresHeader = new TextBlock { Text = "Genres", FontSize = 20, Foreground = (Brush)Application.Current.Resources["ShelfMuted"], Margin = new Thickness(26, 26, 0, 8), Visibility = Visibility.Collapsed };
            Side.Children.Add(genresHeader);
            Window.Current.CoreWindow.KeyDown += (s, e) =>
            {
                if (Frame?.Content != this) return;
                if (e.VirtualKey == VirtualKey.GamepadRightShoulder) { e.Handled = true; Select((category + 1) % sections.Count); }
                else if (e.VirtualKey == VirtualKey.GamepadLeftShoulder) { e.Handled = true; Select((category + sections.Count - 1) % sections.Count); }
            };
        }

        void AddSection(Section sec)
        {
            var b = Row(sec.Glyph, sec.Name);
            var index = sections.Count;
            b.Click += (s, e) => Select(index);
            // Like the Apple TV app: moving onto a section shows it straight away.
            b.GotFocus += (s, e) => { if (category != index) Select(index, keepFocus: true); };
            sec.Row = b;
            sections.Add(sec);
            Side.Children.Add(b);
        }

        /// <summary>The genres of your movies (from TMDB), under "Genres" in the sidebar.</summary>
        void UpdateGenres()
        {
            var genres = all.SelectMany(Library.GenresOf).Distinct().OrderBy(g => g).ToList();
            var have = sections.Where(x => x.Glyph == "\uE8EC").Select(x => x.Name).ToList();
            if (genres.SequenceEqual(have)) return;
            // Rebuild the genre rows (the categories before them stay).
            foreach (var sec in sections.Where(x => x.Glyph == "\uE8EC").ToList()) { Side.Children.Remove(sec.Row); sections.Remove(sec); }
            if (category >= sections.Count) category = 0;
            foreach (var g in genres)
            {
                var name = g;
                AddSection(new Section { Name = name, Glyph = "\uE8EC", Keep = x => Library.GenresOf(x).Contains(name) });
            }
            genresHeader.Visibility = genres.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (all.Count > 0 && e.NavigationMode == NavigationMode.Back) return;
            await UploadServer.StartAsync();
            UploadServer.Uploaded -= OnUploaded;
            UploadServer.Uploaded += OnUploaded;
            Library.Changed -= OnUploaded; Library.Changed += OnUploaded;
            all = await Library.ScanAsync();
            UpdateGenres();
            Select(category);
        }

        void OnUploaded()
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, async () => { all = await Library.ScanAsync(); UpdateGenres(); Select(category); });
        }

        void Select(int index, bool keepFocus = false)
        {
            if (index < 0 || index >= sections.Count) index = 0;
            category = index;
            for (int i = 0; i < sections.Count; i++)
            {
                sections[i].Row.Background = i == index ? RowOn : Clear;
                sections[i].Row.Foreground = i == index ? RowOnText : RowOffText;
            }
            if (!keepFocus) sections[index].Row.StartBringIntoView();
            var sec = sections[index];
            openGroup = null;
            shown = Seasons.Group(all.Where(sec.Keep).ToList());
            CoverGrid.ItemsSource = shown;
            Subtitle.Text = all.Count + (all.Count == 1 ? " video" : " videos");
            DetailLine.Text = shown.FirstOrDefault()?.Detail ?? "";
            Empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Empty.Text = all.Count == 0
                ? "Your library is empty. Choose Add videos to add files from a USB drive or your phone."
                : "Nothing in " + sec.Name + " yet.";
            if (!focusedOnce && shown.Count > 0)
            {
                // First time in: start on the first poster, so A opens it straight away.
                focusedOnce = true;
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => (CoverGrid.ContainerFromIndex(0) as Control)?.Focus(FocusState.Programmatic));
            }
        }

        void LiveTv_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(LiveTvPage));
        void Sports_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SportsPage));

        async void Add_Click(object sender, RoutedEventArgs e)
        {
            var address = UploadServer.Address;
            var dialog = new ContentDialog
            {
                Title = "Add videos",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, FontSize = 20,
                    Text = "Fastest: plug a USB drive with your videos into the Xbox and choose Import from USB. Games, TV episodes " +
                           "and movies each go to the right place.\n\n" +
                           (address == null ? "Over Wi-Fi: connect the Xbox to your home network first."
                                            : "Over Wi-Fi (slower for big files): on your phone or computer, open " + address + " and choose Movies, TV Shows or Sports."),
                },
                PrimaryButtonText = "Import from USB",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ImportUsbAsync();
        }

        /// <summary>Copies every video on the USB drive(s) into the library, showing progress; Cancel stops it.</summary>
        async System.Threading.Tasks.Task ImportUsbAsync()
        {
            List<Windows.Storage.StorageFile> files;
            try { files = await UsbImport.FindAsync(); } catch { files = new List<Windows.Storage.StorageFile>(); }
            if (files.Count == 0)
            {
                await new MessageDialog("No videos found on a USB drive. Plug in a drive with .mp4, .mkv, .ts (or similar) files and try again.", "Import from USB").ShowAsync();
                return;
            }
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 20 };
            var bar = new ProgressBar { Maximum = 1, Margin = new Thickness(0, 16, 0, 0), Foreground = (Brush)Application.Current.Resources["ShelfAccent"] };
            var panel = new StackPanel { MinWidth = 560 }; panel.Children.Add(status); panel.Children.Add(bar);
            var cts = new System.Threading.CancellationTokenSource();
            var progress = new ProgressDialog(panel, cts);
            var show = progress.Dialog.ShowAsync();
            int copied = 0, skipped = 0, failed = 0;
            for (int i = 0; i < files.Count && !cts.IsCancellationRequested; i++)
            {
                var f = files[i];
                status.Text = $"Copying {i + 1} of {files.Count} to {UsbImport.FolderFor(f.Name)}:\n{f.Name}";
                bar.Value = 0;
                try
                {
                    var ok = await UsbImport.CopyAsync(f, new Progress<double>(v => bar.Value = v), cts.Token);
                    if (ok) copied++; else skipped++;
                }
                catch (OperationCanceledException) { break; }
                catch { failed++; }
            }
            progress.Dialog.Hide();
            try { await show; } catch { }
            all = await Library.ScanAsync(); Select(category);
            var msg = $"{copied} video{(copied == 1 ? "" : "s")} added." + (skipped > 0 ? $" {skipped} already in your library." : "") +
                      (failed > 0 ? $" {failed} couldn't be copied." : "") + (cts.IsCancellationRequested ? " Stopped." : "");
            await new MessageDialog(msg, "Import from USB").ShowAsync();
        }

        sealed class ProgressDialog
        {
            public readonly ContentDialog Dialog;
            public ProgressDialog(UIElement content, System.Threading.CancellationTokenSource cts)
            {
                Dialog = new ContentDialog { Title = "Importing from USB", Content = content, CloseButtonText = "Cancel" };
                Dialog.CloseButtonClick += (s, a) => cts.Cancel();
            }
        }

        /// <summary>The highlighted poster zooms in quickly (and back out when you move on), like the Apple TV app.</summary>
        static void Zoom(GridViewItem c, double to)
        {
            if (c == null) return;
            if (!(c.RenderTransform is ScaleTransform st)) { st = new ScaleTransform(); c.RenderTransform = st; c.RenderTransformOrigin = new Point(0.5, 0.5); }
            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            foreach (var prop in new[] { "ScaleX", "ScaleY" })
            {
                var a = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    To = to, Duration = TimeSpan.FromMilliseconds(120),
                    EasingFunction = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut },
                };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, st);
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, prop);
                sb.Children.Add(a);
            }
            sb.Begin();
            Canvas.SetZIndex(c, to > 1 ? 1 : 0);
        }

        void CoverGrid_GotFocus(object sender, RoutedEventArgs e)
        {
            Zoom(e.OriginalSource as GridViewItem, 1.1);
            if ((e.OriginalSource as GridViewItem)?.Content is Item it)
            {
                foreach (var x in shown) if (x.Focused && x != it) x.Focused = false;
                it.Focused = true;
                DetailLine.Text = it.Detail;
            }
        }

        void CoverGrid_LostFocus(object sender, RoutedEventArgs e)
        {
            Zoom(e.OriginalSource as GridViewItem, 1.0);
            if ((e.OriginalSource as GridViewItem)?.Content is Item it) it.Focused = false;
        }

        async void CoverGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            if (args.Item is Item it)
            {
                await Library.LoadCoverAsync(it);
                if (it.Kind == Kind.Movie) UpdateGenres(); // its genres are known now (from TMDB)
            }
        }

        Item openGroup;

        /// <summary>Opens a season / series: its episodes in place of the posters (B goes back).</summary>
        void OpenGroup(Item g)
        {
            openGroup = g;
            shown = g.Children;
            CoverGrid.ItemsSource = shown;
            Subtitle.Text = g.Title + "  ·  " + g.Children.Count + " episodes  ·  B goes back";
            DetailLine.Text = shown.FirstOrDefault()?.Detail ?? "";
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => (CoverGrid.ContainerFromIndex(0) as Control)?.Focus(FocusState.Programmatic));
        }

        /// <summary>B inside a season: back to the posters. False when no season is open.</summary>
        public bool CloseGroup()
        {
            if (openGroup == null) return false;
            var g = openGroup;
            Select(category);
            var i = shown.IndexOf(shown.FirstOrDefault(x => x.IsGroup && x.Title == g.Title));
            if (i >= 0) { var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => (CoverGrid.ContainerFromIndex(i) as Control)?.Focus(FocusState.Programmatic)); }
            return true;
        }

        async void CoverGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            var it = (Item)e.ClickedItem;
            if (it.IsGroup) { OpenGroup(it); return; }
            switch (it.Kind)
            {
                case Kind.Movie:
                case Kind.TvShow:
                case Kind.SportsEvent:
                case Kind.Recording:
                case Kind.SportsRecording:
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
