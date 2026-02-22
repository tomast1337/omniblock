using System;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Messages;
using BetaSharp.Launcher.Features.Mojang;
using BetaSharp.Launcher.Features.Xbox;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Authentication;

// Does this need a better name?
internal sealed partial class AuthenticationViewModel(
    AccountService accountService,
    AuthenticationService authenticationService,
    XboxClient xboxClient,
    MojangClient minecraftService) : ObservableObject
{
    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        string microsoft = await authenticationService.AuthenticateAsync();

        var xboxProfile = await xboxClient.GetProfileAsync(microsoft);

        var xboxToken = await xboxClient.GetTokenAsync(xboxProfile.Token);

        var minecraftToken = await minecraftService.GetTokenAsync(xboxToken.Value, xboxProfile.DisplayClaims.Xui[0].Uhs);

        var minecraftProfile = await minecraftService.GetProfileAsync(minecraftToken.Value);

        await accountService.UpdateAsync(minecraftProfile.Name, "", minecraftToken.Value, DateTimeOffset.Now.AddHours(1));

        WeakReferenceMessenger.Default.Send(new NavigationMessage(Destination.Home));
    }
}
