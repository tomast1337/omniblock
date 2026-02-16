using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Home;
using BetaSharp.Launcher.Features.New.Authentication;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.New;

internal sealed partial class NewViewModel(AuthenticationService authenticationService, DownloadingService downloadingService, HomeViewModel homeViewModel) : ObservableObject
{
    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        await authenticationService.AuthenticateAsync();
    }
}
