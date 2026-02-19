using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Home;

internal sealed partial class HomeViewModel(AuthenticationService authenticationService, MinecraftService minecraftService, XboxService xboxService) : ObservableObject
{
    [RelayCommand]
    private async Task TestAsync()
    {
        string? microsoft = await authenticationService.GetTokenAsync();

        if (string.IsNullOrWhiteSpace(microsoft))
        {
            WeakReferenceMessenger.Default.Send(new NavigationMessage(Destination.Authentication));
            return;
        }

        var xbox = await xboxService.GetAsync(microsoft);
        string minecraft = await minecraftService.GetTokenAsync(xbox.Token, xbox.Hash);
        var profile = await minecraftService.GetProfileAsync(minecraft);
    }
}
