using System;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Alert;
using BetaSharp.Launcher.Features.Authentication;
using BetaSharp.Launcher.Features.Home;
using BetaSharp.Launcher.Features.Sessions;
using BetaSharp.Launcher.Features.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Splash;

internal sealed partial class SplashViewModel(
    AuthenticationService authenticationService,
    StorageService storageService,
    NavigationService navigationService,
    SessionService sessionService,
    AlertService alertService) : ObservableObject
{
    [RelayCommand]
    private async Task InitializeAsync()
    {
        await authenticationService.InitializeAsync();

        var session = await storageService.GetAsync(SessionsSerializerContext.Default.Session);

        if (session?.Expiration > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            navigationService.Navigate<HomeViewModel>();
            return;
        }

        string? token = await authenticationService.TryAuthenticateSilentlyAsync();

        if (string.IsNullOrWhiteSpace(token))
        {
            navigationService.Navigate<AuthenticationViewModel>();
            return;
        }

        session = await sessionService.TryCreateAsync(token);

        if (session is null)
        {
            navigationService.Navigate<AuthenticationViewModel>();
            return;
        }

        ArgumentNullException.ThrowIfNull(session);

        await storageService.SetAsync(session, SessionsSerializerContext.Default.Session);

        WeakReferenceMessenger.Default.Send(new SessionMessage(session));

        navigationService.Navigate<HomeViewModel>();
    }
}
