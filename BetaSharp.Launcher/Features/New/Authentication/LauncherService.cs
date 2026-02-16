using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace BetaSharp.Launcher.Features.New.Authentication;

internal sealed class LauncherService
{
    private readonly Window? _window = ((ClassicDesktopStyleApplicationLifetime?) Application.Current?.ApplicationLifetime)?.MainWindow;

    public async Task LaunchAsync(string destination)
    {
        ArgumentNullException.ThrowIfNull(_window);
        await _window.Launcher.LaunchUriAsync(new Uri(destination));
    }
}