using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Authentication;
using BetaSharp.Launcher.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Splash;

internal sealed partial class SplashViewModel(AuthenticationService authenticationService, GitHubService gitHubService, AuthenticationViewModel authenticationViewModel) : ObservableObject
{
    [RelayCommand]
    private async Task InitializeAsync()
    {
        await authenticationService.InitializeAsync();
        await gitHubService.GetUpdatesAsync();

        WeakReferenceMessenger.Default.Send(new NavigationMessage(authenticationViewModel));
    }
}
