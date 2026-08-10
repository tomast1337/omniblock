using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniBlock.Launcher.Features.Alert;

internal sealed partial class AlertView : Window
{
    public AlertView()
    {
        InitializeComponent();
    }

    private void CloseClick(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
