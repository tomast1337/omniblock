using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Authentication;
using BetaSharp.Launcher.Features.Home;
using BetaSharp.Launcher.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Splash;

internal sealed partial class SplashViewModel(
    AuthenticationService authenticationService,
    AuthenticationViewModel authenticationViewModel,
    HomeViewModel homeViewModel) : ObservableObject
{
    [RelayCommand]
    private async Task InitializeAsync()
    {
        await Task.Delay(2500);

        await authenticationService.InitializeAsync();

        bool has = await authenticationService.HasAccountsAsync();

        WeakReferenceMessenger.Default.Send(new NavigationMessage(has ? homeViewModel : authenticationViewModel));
    }
}
