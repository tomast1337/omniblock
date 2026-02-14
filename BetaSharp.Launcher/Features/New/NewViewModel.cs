using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.New;

internal sealed partial class NewViewModel(AuthenticationService authenticationService, MinecraftDownloader minecraftDownloader) : ObservableObject
{
    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        // What to do if the browser tab was closed?
        var owns = await authenticationService.OwnsMinecraftAsync();

        if (!owns)
        {
            return;
        }

        var path = await minecraftDownloader.DownloadAsync();

        ((ClassicDesktopStyleApplicationLifetime?) Application.Current?.ApplicationLifetime)?.Shutdown();
    }
}