using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Home;
using BetaSharp.Launcher.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Authentication;

internal sealed partial class AuthenticationViewModel(AuthenticationService authenticationService, HomeViewModel homeViewModel) : ObservableObject
{
    [ObservableProperty]
    public partial string Message { get; set; } = "Authenticate with Microsoft";

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await authenticationService.InitializeAsync();
    }

    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        string token = await authenticationService.AuthenticateAsync();

        WeakReferenceMessenger.Default.Send(new NavigationMessage(homeViewModel));
    }
}
