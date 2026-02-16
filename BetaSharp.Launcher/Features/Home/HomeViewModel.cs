using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.Home;

internal sealed partial class HomeViewModel(AuthenticationService authenticationService) : ObservableObject
{
    [RelayCommand]
    private async Task SignOutAsync()
    {
        await authenticationService.SignOutAsync();
    }
}
