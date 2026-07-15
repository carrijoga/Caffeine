using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Caffeine;

public sealed partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        Title = "Caffeine";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "caffeine-on.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 640));

        // PowerToys look: Mica backdrop + content extended into the title bar.
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
        }

        // With "Run in system tray" on, closing hides to tray and the app exits
        // only via the tray menu; with it off, closing exits the app.
        AppWindow.Closing += (_, e) =>
        {
            var app = (App)Application.Current;
            if (!_allowClose && app.Settings.RunInSystemTray)
            {
                e.Cancel = true;
                AppWindow.Hide();
            }
            else if (!_allowClose)
            {
                app.ExitApp();
            }
        };

        NavView.SelectedItem = HomeItem;
    }

    public void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    public void AllowClose() => _allowClose = true;

    private void NavView_SelectionChanged(
        NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            Type page = (string)item.Tag switch
            {
                "todos" => typeof(Pages.TodosPage),
                "timers" => typeof(Pages.TimersPage),
                "awake" => typeof(Pages.AwakePage),
                "settings" => typeof(Pages.SettingsPage),
                "welcome" => typeof(Pages.WelcomePage),
                _ => typeof(Pages.HomePage),
            };

            if (ContentFrame.CurrentSourcePageType != page)
            {
                ContentFrame.Navigate(page);
            }
        }
    }

    /// <summary>Selects the sidebar item with the given Tag ("home", "todos", "timers", "awake", "settings", "welcome").</summary>
    public void NavigateTo(string tag)
    {
        foreach (object entry in NavView.MenuItems.Concat(NavView.FooterMenuItems))
        {
            if (entry is NavigationViewItem item && (string)item.Tag == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }
    }
}
