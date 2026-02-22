using System;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Messages;
using BetaSharp.Launcher.Features.Mojang;
using BetaSharp.Launcher.Features.Mojang.Token;
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

        var xboxUser = await xboxClient.GetUserAsync(microsoft);

        var xboxToken = await xboxClient.GetTokenAsync(xboxUser.Token);

        var minecraft = await minecraftService.GetTokenAsync(new TokenRequest
        {
            Value = $"XBL3.0 x={xboxUser.DisplayClaims.Xui[0].Uhs};{xboxToken.Token}"
        });

        var profile = await minecraftService.GetProfileAsync(minecraft.Value);

        await accountService.UpdateAsync(profile.Name, "", minecraft.Value, DateTimeOffset.Now.AddHours(1));

        WeakReferenceMessenger.Default.Send(new NavigationMessage(Destination.Home));
    }
}
