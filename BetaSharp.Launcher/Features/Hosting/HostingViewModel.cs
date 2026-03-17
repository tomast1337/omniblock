using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Home;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.Hosting;

internal sealed partial class HostingViewModel( ProcessService processService, NavigationService navigationService) : ObservableObject
{
    [ObservableProperty]
    public partial string Message { get; set; } = "Run";

    private bool _isRunning;
    private Process? _process;

    [RelayCommand]
    private async Task RunAsync()
    {
        if (_isRunning)
        {
            Message = "Stopping";

            ArgumentNullException.ThrowIfNull(_process);

            _process.Kill();
            _process.Dispose();

            Message = "Run";

            _isRunning = false;

            return;
        }

        Message = "Initializing";

        _process = await processService.StartAsync(Kind.Server);

        Message = "Stop";

        _isRunning = true;
    }

    [RelayCommand]
    private void Back()
    {
        navigationService.Navigate<HomeViewModel>();
    }
}
