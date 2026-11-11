using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Shelf
{
    sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            // A controller app: no mouse cursor on Xbox.
            RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;
            // Xbox doubles every size by default (a 1080p screen acts like 960×540). Shelf is laid out in real
            // 1080p pixels, like the Android TV version, so turn that off.
            try { ApplicationViewScaling.TrySetDisableLayoutScaling(true); } catch { }
            Suspending += (s, e) => { };
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            // Use the whole TV; pages keep their own safe margins.
            ApplicationView.GetForCurrentView().SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);

            if (!(Window.Current.Content is Frame root))
            {
                root = new Frame();
                Window.Current.Content = root;
                await Storage.InitAsync();
            }
            if (root.Content == null) root.Navigate(typeof(MainPage), e.Arguments);
            Window.Current.Activate();

            // B / Back goes back a page.
            Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested += (s, a) =>
            {
                if (root.CanGoBack) { a.Handled = true; root.GoBack(); }
            };
        }
    }
}
