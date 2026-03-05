using System;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Authentication;
using BetaSharp.Launcher.Features.Home;
using BetaSharp.Launcher.Features.Sessions;
using BetaSharp.Launcher.Features.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.Splash;

internal sealed partial class SplashViewModel(AuthenticationService authenticationService, StorageService storageService, NavigationService navigationService) : ObservableObject
{
    [RelayCommand]
    private async Task InitializeAsync()
    {
        var delay = Task.Delay(TimeSpan.FromSeconds(2.5));

        await authenticationService.InitializeAsync();
        var session = await storageService.GetAsync(SessionsSerializerContext.Default.Session);

        await delay;

        if (string.IsNullOrWhiteSpace(session?.Token))
        {
            navigationService.Navigate<AuthenticationViewModel>();
        }
        else
        {
            navigationService.Navigate<HomeViewModel>();
        }
    }
}
