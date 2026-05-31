using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace BetaSharp.Launcher.Features.Splash;

internal sealed class TitleService
{
    public void Set(string title)
    {
        if (Application.Current?.ApplicationLifetime is not ClassicDesktopStyleApplicationLifetime lifetime)
        {
            return;
        }

        var window = lifetime.MainWindow;

        ArgumentNullException.ThrowIfNull(window);

        window.Title = title;
    }
}
