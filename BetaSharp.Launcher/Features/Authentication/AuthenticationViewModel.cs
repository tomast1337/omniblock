using System;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Alert;
using BetaSharp.Launcher.Features.Home;
using BetaSharp.Launcher.Features.Mojang;
using BetaSharp.Launcher.Features.Sessions;
using BetaSharp.Launcher.Features.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.Authentication;

internal sealed partial class AuthenticationViewModel(
    AuthenticationService authenticationService,
    MinecraftService minecraftService,
    AlertService alertService,
    MojangClient mojangClient,
    StorageService storageService,
    NavigationService navigationService) : ObservableObject
{
    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        var token = await minecraftService.TryGetTokenAsync(await authenticationService.AuthenticateAsync());

        if (string.IsNullOrWhiteSpace(token?.Value))
        {
            await alertService.ShowAsync("Authentication Failure", "The selected Microsoft account does not own a copy of Minecraft Java edition");
            return;
        }

        var profile = await mojangClient.GetProfileAsync(token.Value);
        var session = new Session { Name = profile.Name, Skin = profile.Skins[0].Url, Token = token.Value, Expiration = DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(token.Expiration)) };

        await storageService.SetAsync(session, SessionsSerializerContext.Default.Session);

        navigationService.Navigate<HomeViewModel>();
    }
}
