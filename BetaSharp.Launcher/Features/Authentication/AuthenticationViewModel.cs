using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Home;
using BetaSharp.Launcher.Features.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Authentication;

// Does it need a better name?
internal sealed partial class AuthenticationViewModel(AuthenticationService authenticationService, HomeViewModel homeViewModel) : ObservableObject
{
    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        await authenticationService.AuthenticateAsync();
        WeakReferenceMessenger.Default.Send(new NavigationMessage(homeViewModel));
    }
}
