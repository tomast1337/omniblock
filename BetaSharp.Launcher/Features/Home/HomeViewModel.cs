using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BetaSharp.Launcher.Features.Authentication;
using BetaSharp.Launcher.Features.Sessions;
using BetaSharp.Launcher.Features.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Home;

internal sealed partial class HomeViewModel : ObservableObject
{
    [ObservableProperty]
    public partial Session? Session { get; set; }

    [ObservableProperty]
    public partial CroppedBitmap? Face { get; set; }

    private readonly NavigationService _navigationService;
    private readonly StorageService _storageService;
    private readonly ClientService _clientService;
    private readonly AuthenticationService _authenticationService;
    private readonly SessionService _sessionService;

    public HomeViewModel(NavigationService navigationService, StorageService storageService, ClientService clientService, AuthenticationService authenticationService, SessionService sessionService)
    {
        _navigationService = navigationService;
        _storageService = storageService;
        _clientService = clientService;
        _authenticationService = authenticationService;
        _sessionService = sessionService;

        WeakReferenceMessenger.Default.Register<HomeViewModel, SessionMessage>(
            this,
            static (viewModel, message) => viewModel.Session = message.Session);
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (Session?.Expiration > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            string? token = await _authenticationService.TryAuthenticateSilentlyAsync();

            if (string.IsNullOrWhiteSpace(token))
            {
                _navigationService.Navigate<AuthenticationViewModel>();
                return;
            }

            Session = await _sessionService.TryCreateAsync(token);

            if (Session is null)
            {
                _navigationService.Navigate<AuthenticationViewModel>();
                return;
            }

            ArgumentNullException.ThrowIfNull(Session);

            await _storageService.SetAsync(Session, SessionsSerializerContext.Default.Session);
        }

        ArgumentNullException.ThrowIfNull(Session);

        await _clientService.DownloadAsync();

        var info = new ProcessStartInfo { Arguments = $"{Session.Name} {Session.Token} {Session.Skin}", CreateNoWindow = true, FileName = Path.Combine(AppContext.BaseDirectory, "Client", "BetaSharp.Client") };

        // Probably should move this into a service/view-model.
        using var process = Process.Start(info);

        ArgumentNullException.ThrowIfNull(process);

        await process.WaitForExitAsync();
    }

    [RelayCommand]
    private void SignOut()
    {
        _navigationService.Navigate<AuthenticationViewModel>();
        _storageService.Delete(nameof(Session));

        Face?.Dispose();
    }
}
