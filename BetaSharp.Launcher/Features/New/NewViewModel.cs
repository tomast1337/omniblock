using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.New;

internal sealed partial class NewViewModel(AuthenticationService authenticationService) : ObservableObject
{
    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        // What to do if the browser tab was closed?
        var result = await authenticationService.OwnsMinecraftAsync();
    }
}