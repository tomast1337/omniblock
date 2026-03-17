using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Authentication;
using BetaSharp.Launcher.Features.Hosting;
using BetaSharp.Launcher.Features.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Home;

internal sealed partial class HomeViewModel : ObservableObject
{
    [ObservableProperty]
    public partial Session? Session { get; set; }

    private readonly NavigationService _navigationService;
    private readonly StorageService _storageService;
    private readonly MinecraftService _minecraftService;
    private readonly ProcessService _processService;

    public HomeViewModel(NavigationService navigationService, StorageService storageService, MinecraftService minecraftService, ProcessService processService)
    {
        _navigationService = navigationService;
        _storageService = storageService;
        _minecraftService = minecraftService;
        _processService = processService;

        WeakReferenceMessenger.Default.Register<HomeViewModel, SessionMessage>(
            this,
            static (viewModel, message) => viewModel.Session = message.Session);
    }

    [RelayCommand]
    private void SignOut()
    {
        _navigationService.Navigate<AuthenticationViewModel>();
        _storageService.Delete(nameof(Session));
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (Session?.HasExpired ?? true)
        {
            _navigationService.Navigate<AuthenticationViewModel>();
            return;
        }

        string directory = Path.Combine(AppContext.BaseDirectory, "Client");

        await _minecraftService.DownloadAsync(directory);

        using var process = _processService.StartAsync(directory, "Client", Session.Name, Session.Token);

        await process.WaitForExitAsync();
    }

    [RelayCommand]
    private void Host()
    {
        _navigationService.Navigate<HostingViewModel>();
    }
}
