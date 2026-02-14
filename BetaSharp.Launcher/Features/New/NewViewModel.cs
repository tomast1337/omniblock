using System.Diagnostics;
using System.Threading.Tasks;
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

        await minecraftDownloader.DownloadAsync();

        using var process = new Process();
        
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "BetaSharpClient",
            Arguments = "Starlk -",
            UseShellExecute = false, 
            CreateNoWindow = false, 
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        process.Start();
    }
}