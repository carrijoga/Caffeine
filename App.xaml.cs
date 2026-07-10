using System.Drawing;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Caffeine;

public partial class App : Application
{
    private MainWindow? _window;
    private TaskbarIcon? _trayIcon;
    private ToggleMenuFlyoutItem? _trayToggleItem;
    private string _iconOnPath = string.Empty;
    private string _iconOffPath = string.Empty;

    public AwakeState State { get; } = new();

    public App()
    {
        InitializeComponent();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        UnhandledException += (_, e) =>
            LogCrash("App.UnhandledException", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "crash.log"),
                $"[{DateTime.Now:O}] {source}\n{ex}\n\n");
        }
        catch
        {
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _iconOnPath = Path.Combine(AppContext.BaseDirectory, "Assets", "caffeine-on.ico");
        _iconOffPath = Path.Combine(AppContext.BaseDirectory, "Assets", "caffeine-off.ico");

        _window = new MainWindow(State);
        CreateTrayIcon();

        State.Changed += OnStateChanged;
        State.Set(true); // start active, like the original Caffeine

        _window.Activate();
    }

    private void CreateTrayIcon()
    {
        _trayToggleItem = new ToggleMenuFlyoutItem { Text = "Keep screen awake" };
        _trayToggleItem.Click += (_, _) => State.Toggle();

        var openItem = new MenuFlyoutItem { Text = "Open Caffeine" };
        openItem.Click += (_, _) => _window?.ShowFromTray();

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (_, _) => ExitApp();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Caffeine",
            Icon = new Icon(_iconOffPath),
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(State.Toggle),
            ContextMenuMode = ContextMenuMode.SecondWindow,
            ContextFlyout = new MenuFlyout
            {
                Items = { _trayToggleItem, new MenuFlyoutSeparator(), openItem, exitItem },
            },
        };
        _trayIcon.ForceCreate();
    }

    private void OnStateChanged(bool active)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Icon = new Icon(active ? _iconOnPath : _iconOffPath);
            _trayIcon.ToolTipText = active
                ? "Caffeine — keeping your screen awake"
                : "Caffeine — inactive";
        }

        if (_trayToggleItem is not null)
        {
            _trayToggleItem.IsChecked = active;
        }
    }

    private void ExitApp()
    {
        State.Set(false);
        _trayIcon?.Dispose();
        _window?.AllowClose();
        Exit();
    }
}
