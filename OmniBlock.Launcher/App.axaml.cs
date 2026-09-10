using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using OmniBlock.Launcher.Features;
using OmniBlock.Launcher.Features.Hosting;
using OmniBlock.Launcher.Features.Shell;
using OmniBlock.Launcher.Features.Splash;

namespace OmniBlock.Launcher;

internal sealed class App : Application
{
    private readonly IServiceProvider _services = Bootstrapper.Build();

    static App()
    {
        Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), $".{nameof(OmniBlock)}", "launcher");
        Directory.CreateDirectory(Folder);
    }

    public static string Folder { get; }

    public override void Initialize()
    {
        DataTemplates.Add(_services.GetRequiredService<ViewLocator>());
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services
                .GetRequiredService<NavigationService>()
                .Navigate<SplashViewModel>();

            desktop.MainWindow = _services.GetRequiredService<ShellView>();
            desktop.Exit += DesktopOnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DesktopOnExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        _services
            .GetRequiredService<HostingViewModel>()
            .Stop();
    }
}
