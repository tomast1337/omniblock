using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Home;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.Hosting;

internal sealed partial class HostingViewModel(ProcessService processService, NavigationService navigationService) : ObservableObject
{
    public ObservableCollection<string> Logs { get; } = [];

    [ObservableProperty]
    public partial int Last { get; set; }

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

            _process.CancelOutputRead();
            _process.OutputDataReceived -= ProcessOnOutputDataReceived;

            _process.Kill();
            _process.Dispose();

            Message = "Run";

            _isRunning = false;

            return;
        }

        Message = "Initializing";

        _process = await processService.StartAsync(Kind.Server);

        Logs.Clear();

        _process.BeginOutputReadLine();
        _process.OutputDataReceived += ProcessOnOutputDataReceived;

        Message = "Stop";

        _isRunning = true;
    }

    [RelayCommand]
    private void Back()
    {
        navigationService.Navigate<HomeViewModel>();
    }

    private void ProcessOnOutputDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            return;
        }

        Logs.Add(eventArgs.Data);
        Last = Logs.Count - 1;
    }
}
