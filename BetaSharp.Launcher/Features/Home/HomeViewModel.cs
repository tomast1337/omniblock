using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BetaSharp.Launcher.Features.Authentication;
using BetaSharp.Launcher.Features.Mojang;
using BetaSharp.Launcher.Features.Sessions;
using BetaSharp.Launcher.Features.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.Home;

internal sealed partial class HomeViewModel(
    StorageService storageService,
    NavigationService navigationService,
    SkinService skinService,
    AuthenticationService authenticationService,
    MinecraftService minecraftService,
    MojangClient mojangClient,
    ClientService clientService) : ObservableObject
{
    [ObservableProperty]
    public partial Session? Session { get; set; }

    [ObservableProperty]
    public partial CroppedBitmap? Face { get; set; }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        Session = await storageService.GetAsync(SessionsSerializerContext.Default.Session);

        if (string.IsNullOrWhiteSpace(Session?.Token))
        {
            navigationService.Navigate<AuthenticationViewModel>();
            return;
        }

        if (Session.Expiration > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            Face = await skinService.GetFaceAsync(Session.Skin);
            return;
        }

        string? microsoft = await authenticationService.TryAuthenticateSilentlyAsync();

        if (string.IsNullOrWhiteSpace(microsoft))
        {
            navigationService.Navigate<AuthenticationViewModel>();
            return;
        }

        var minecraft = await minecraftService.TryGetTokenAsync(microsoft);

        ArgumentNullException.ThrowIfNull(minecraft);

        var profile = await mojangClient.GetProfileAsync(minecraft.Value);
        Session = new Session { Name = profile.Name, Skin = profile.Skins.Last().Url, Token = minecraft.Value, Expiration = DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(minecraft.Expiration)) };

        await storageService.SetAsync(Session, SessionsSerializerContext.Default.Session);
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        // Check if session's token has expired.
        ArgumentNullException.ThrowIfNull(Session);

        await clientService.DownloadAsync();

        var info = new ProcessStartInfo { Arguments = $"{Session.Name} {Session.Token} {Session.Skin}", CreateNoWindow = true, FileName = Path.Combine(AppContext.BaseDirectory, "Client", "BetaSharp.Client") };

        // Probably should move this into a service/view-model.
        using var process = Process.Start(info);

        ArgumentNullException.ThrowIfNull(process);

        await process.WaitForExitAsync();
    }

    [RelayCommand]
    private void SignOut()
    {
        navigationService.Navigate<AuthenticationViewModel>();
        storageService.Delete(nameof(Session));

        Face?.Dispose();
    }
}
