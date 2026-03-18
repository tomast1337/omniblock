using BetaSharp.Launcher.Features.Hosting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.Properties;

internal sealed partial class PropertiesViewModel(NavigationService navigationService) : ObservableObject
{
    [RelayCommand]
    private void Save()
    {
        navigationService.Navigate<HostingViewModel>();
    }

    [RelayCommand]
    private void Back()
    {
        navigationService.Navigate<HostingViewModel>();
    }
}
