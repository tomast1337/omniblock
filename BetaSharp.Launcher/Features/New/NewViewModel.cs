using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Home;
using BetaSharp.Launcher.Features.New.Authentication;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.New;

internal sealed partial class NewViewModel(
    MicrosoftService microsoftService,
    XboxService xboxService,
    MinecraftService minecraftService,
    DownloadingService downloadingService,
    HomeViewModel homeViewModel) : ObservableObject
{
    [ObservableProperty]
    public partial string Message { get; set; } = "Authenticate with Microsoft";

    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        Message = "Authenticating";

        string microsoft = await microsoftService.AuthenticateAsync();

        var profile = await xboxService.GetProfileAsync(microsoft);

        Message = "Checking ownership";

        string xbox = await xboxService.GetTokenAsync(profile.Token);
        string minecraft = await minecraftService.GetTokenAsync(xbox, profile.Hash);

        if (!await minecraftService.GetGameAsync(minecraft))
        {
            return;
        }

        string name = await minecraftService.GetNameAsync(minecraft);

        Message = "Downloading the game";

        await downloadingService.DownloadAsync();

        WeakReferenceMessenger.Default.Send(new NavigationMessage(homeViewModel));

        // Wait for the animation to finish, This is just a nitpick and completely unnecessary.
        await Task.Delay(500);
    }
}
