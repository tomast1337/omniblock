using System.Diagnostics;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.New.Authentication;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.New;

internal sealed partial class NewViewModel(AuthenticationService authenticationService, DownloadingService downloadingService) : ObservableObject
{
    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        var authentication = authenticationService.AuthenticateAsync();

        await Task.WhenAll(authentication, downloadingService.DownloadAsync());

        var session = await authentication;

        Debug.WriteLine($"{session.Name}, {session.Token}");
    }
}
