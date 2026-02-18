using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BetaSharp.Launcher.Features.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Home;

internal sealed partial class HomeViewModel(AuthenticationService authenticationService, MinecraftService minecraftService, XboxService xboxService) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsReady { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "...";

    [ObservableProperty]
    public partial CroppedBitmap? Face { get; set; }

    [ObservableProperty]
    public partial string Play { get; set; } = "Play";

    private string? _id;
    private string? _token;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        string? microsoft = await authenticationService.GetTokenAsync();

        if (string.IsNullOrWhiteSpace(microsoft))
        {
            // Navigate to authentication.
            return;
        }

        var xbox = await xboxService.GetAsync(microsoft);

        string minecraft = await minecraftService.GetTokenAsync(xbox.Token, xbox.Hash);
        _token = minecraft;

        var profile = await minecraftService.GetProfileAsync(minecraft);

        Name = profile.Name;
        _id = profile.ID;
        Face = profile.Image;

        IsReady = true;
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        Play = "Downloading the game...";

        await minecraftService.DownloadAsync();

        Play = "Playing";

        using var process = new Process();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Directory.GetCurrentDirectory(), "Client", "BetaSharp.Client"),
            Arguments = $"{Name} {_token}",
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        process.Start();
    }
}
