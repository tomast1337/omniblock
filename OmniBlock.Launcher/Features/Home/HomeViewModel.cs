using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using OmniBlock.Launcher.Features.Authentication;
using OmniBlock.Launcher.Features.Home.GitHub;
using OmniBlock.Launcher.Features.Hosting;
using OmniBlock.Launcher.Features.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Launcher.Features.Home;

internal sealed partial class HomeViewModel : ObservableObject
{
    public ObservableCollection<OmniBlockRelease> Releases { get; } = [];

    [ObservableProperty]
    public partial Session? Session { get; set; }

    [ObservableProperty]
    public partial bool DebugMode { get; set; }

    private readonly ILogger<HomeViewModel> _logger;
    private readonly GitHubClient _gitHubClient;
    private readonly NavigationService _navigationService;
    private readonly StorageService _storageService;
    private readonly ProcessService _processService;

    public HomeViewModel(
        ILogger<HomeViewModel> logger,
        GitHubClient gitHubClient,
        NavigationService navigationService,
        StorageService storageService,
        ProcessService processService)
    {
        _logger = logger;
        _gitHubClient = gitHubClient;
        _navigationService = navigationService;
        _storageService = storageService;
        _processService = processService;

        // Replace messenger with a session store service?
        WeakReferenceMessenger.Default.Register<HomeViewModel, SessionMessage>(
            this,
            static (viewModel, message) => viewModel.Session = message.Session);
    }

    [RelayCommand]
    private void Initialize()
    {
        // Use observables?
        Task.Run(GetReleasesAsync);
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

        string[] args = DebugMode
            ? ["--username", Session.Name, "--token", Session.Token, "--debug"]
            : ["--username", Session.Name, "--token", Session.Token];

        using var process = await _processService.StartAsync(Kind.Client, args);
        await process.WaitForExitAsync();
    }

    [RelayCommand]
    private void Host()
    {
        _navigationService.Navigate<HostingViewModel>();
    }

    private async Task GetReleasesAsync()
    {
        try
        {
            // Collection is already populated.
            if (Releases.Any())
            {
                return;
            }

            var releases = await _gitHubClient.GetReleasesAsync("omniblock-official", nameof(OmniBlock));

            foreach (var release in releases)
            {
                Releases.Add(new OmniBlockRelease(release.Name, DateTimeOffset.Parse(release.Date).ToString("d"), release.Url));
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An exception occurred in home view-model on get releases");
        }
    }
}
