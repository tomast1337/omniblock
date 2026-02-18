using System;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Home;
using BetaSharp.Launcher.Features.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Splash;

internal sealed partial class SplashViewModel(AuthenticationService authenticationService, HomeViewModel homeViewModel) : ObservableObject
{
    [RelayCommand]
    private async Task InitializeAsync()
    {
        // Simulate lag to avoid Avalonia crashing.
        // This will be replaced by an update checker.
        await Task.Delay(TimeSpan.FromSeconds(1));

        await authenticationService.InitializeAsync();

        WeakReferenceMessenger.Default.Send(new NavigationMessage(homeViewModel));
    }
}
