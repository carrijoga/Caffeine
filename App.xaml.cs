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

    public AppSettings Settings { get; private set; } = new();

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

        Settings = SettingsService.Load();
        StartupService.RefreshPath();

        _window = new MainWindow();
        if (Settings.RunInSystemTray)
        {
            CreateTrayIcon();
        }

        State.Changed += OnStateChanged;
        State.Set(Settings.RememberState ? Settings.LastAwakeActive : true);

        // When Windows launches us at sign-in, start quietly in the tray
        // instead of popping the window — unless there is no tray to live in.
        bool startupLaunch = Environment.GetCommandLineArgs().Contains("--startup");
        if (!startupLaunch || !Settings.RunInSystemTray)
        {
            _window.Activate();
        }
    }

    public void SetRememberState(bool remember)
    {
        Settings.RememberState = remember;
        Settings.LastAwakeActive = State.IsActive;
        SettingsService.Save(Settings);
    }

    public void SetRunInSystemTray(bool runInTray)
    {
        Settings.RunInSystemTray = runInTray;
        SettingsService.Save(Settings);

        if (runInTray)
        {
            CreateTrayIcon();
            OnStateChanged(State.IsActive);
        }
        else
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            _trayToggleItem = null;
        }
    }

    private void CreateTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

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

        if (Settings.RememberState && Settings.LastAwakeActive != active)
        {
            Settings.LastAwakeActive = active;
            SettingsService.Save(Settings);
        }
    }

    public void ExitApp()
    {
        // Release the awake request without clobbering the remembered toggle
        // position — LastAwakeActive must reflect the user's last choice.
        State.Changed -= OnStateChanged;
        State.Set(false);
        _trayIcon?.Dispose();
        _window?.AllowClose();
        Exit();
    }
}
