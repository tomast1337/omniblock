using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using OmniBlock.Launcher.Features.Authentication;
using OmniBlock.Launcher.Features.Home;
using OmniBlock.Launcher.Features.Sessions;

namespace OmniBlock.Launcher.Features.Splash;

internal sealed partial class SplashViewModel(ILogger<SplashViewModel> logger, TitleService titleService, StorageService storageService, NavigationService navigationService) : ObservableObject
{
    [RelayCommand]
    private async Task InitializeAsync()
    {
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, nameof(Kind.Client), "version.txt");

            using var reader = new StreamReader(file);

            var version = await reader.ReadLineAsync();

            titleService.Set($"OmniBlock Launcher {version}");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to update the title");

            titleService.Set("OmniBlock Launcher development build ( a BetaSharp fork )");
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
