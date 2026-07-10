using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
    }

    private void AwakeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingToggle)
        {
            _state.Set(AwakeToggle.IsOn);
        }
    }
}
