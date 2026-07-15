using Caffeine.Core.Awake;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Caffeine.Pages;

public sealed partial class HomePage : Page
{
    private readonly AwakeState _state;
    private bool _updatingToggle;

    public HomePage()
    {
        _state = ((App)Application.Current).State;
        InitializeComponent();

        _state.Changed += OnStateChanged;
        Unloaded += (_, _) => _state.Changed -= OnStateChanged;

        OnStateChanged(_state.IsActive);
    }

    private void OnStateChanged(bool active)
    {
        _updatingToggle = true;
        AwakeToggle.IsOn = active;
        _updatingToggle = false;

        BadgeActive.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        BadgeInactive.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        AwakeCaption.Text = active
            ? _state.KeepScreenOn ? "Keeping your PC and screen awake" : "Keeping your PC awake"
            : "Your PC can sleep";
    }

    private void AwakeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingToggle)
        {
            _state.Set(AwakeToggle.IsOn);
        }
    }

    private void AwakeToggle_Tapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = true;

    private void OpenAwake_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).NavigateTo("awake");
}
