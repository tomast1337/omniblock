using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Authentication.Services;
using BetaSharp.Launcher.Features.Home;
using BetaSharp.Launcher.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Authentication;

// Does it need a better name?
internal sealed partial class AuthenticationViewModel(
    AuthenticationService authenticationService,
    XboxService xboxService,
    MinecraftService minecraftService,
    HomeViewModel homeViewModel) : ObservableObject
{
    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        string microsoft = await authenticationService.AuthenticateAsync();

        var profile = await xboxService.GetProfileAsync(microsoft);

        string xbox = await xboxService.GetTokenAsync(profile.Token);
        string minecraft = await minecraftService.GetTokenAsync(xbox, profile.Hash);

        if (!await minecraftService.GetGameAsync(minecraft))
        {
            // Do something.
            return;
        }

        WeakReferenceMessenger.Default.Send(new NavigationMessage(homeViewModel));
    }
}
