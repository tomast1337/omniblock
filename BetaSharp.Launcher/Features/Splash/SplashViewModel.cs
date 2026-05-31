using System;
using System.IO;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Authentication;
using BetaSharp.Launcher.Features.Home;
using BetaSharp.Launcher.Features.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Launcher.Features.Splash;

internal sealed partial class SplashViewModel(ILogger<SplashViewModel> logger, TitleService titleService, StorageService storageService, NavigationService navigationService) : ObservableObject
{
    [RelayCommand]
    private async Task InitializeAsync()
    {
        try
        {
            string file = Path.Combine(AppContext.BaseDirectory, nameof(Kind.Client), "version.txt");

            using var reader = new StreamReader(file);

            string? version = await reader.ReadLineAsync();

            titleService.Set($"BetaSharp Launcher {version}");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to update the title");

            titleService.Set("BetaSharp Launcher development build");
        }

        var session = await storageService.GetAsync(SessionSerializerContext.Default.Session);

        if (session?.HasExpired ?? true)
        {
            navigationService.Navigate<AuthenticationViewModel>();
        }
        else
        {
            navigationService.Navigate<HomeViewModel>();
            WeakReferenceMessenger.Default.Send(new SessionMessage(session));
        }
    }
}
