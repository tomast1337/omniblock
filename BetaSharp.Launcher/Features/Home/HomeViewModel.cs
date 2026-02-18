using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.Home;

internal sealed partial class HomeViewModel(AuthenticationService authenticationService, MinecraftService minecraftService, XboxService xboxService) : ObservableObject
{
    [RelayCommand]
    private async Task GetGameAsync()
    {
        string? microsoft = await authenticationService.GetTokenAsync();

        if (string.IsNullOrWhiteSpace(microsoft))
        {
            // Go to authentication view.
            return;
        }

        var profile = await xboxService.GetTokenAsync(microsoft);

        string minecraft = await minecraftService.GetTokenAsync(profile.Token, profile.Hash);

        string name = await minecraftService.GetNameAsync(minecraft);

        Debug.WriteLine(name);
    }
}
