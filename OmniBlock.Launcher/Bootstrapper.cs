using System;
using System.IO;
using CommunityToolkit.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using OmniBlock.Launcher.Features;
using OmniBlock.Launcher.Features.Alert;
using OmniBlock.Launcher.Features.Authentication;
using OmniBlock.Launcher.Features.Home;
using OmniBlock.Launcher.Features.Home.GitHub;
using OmniBlock.Launcher.Features.Hosting;
using OmniBlock.Launcher.Features.Mojang;
using OmniBlock.Launcher.Features.Properties;
using OmniBlock.Launcher.Features.Sessions;
using OmniBlock.Launcher.Features.Shell;
using OmniBlock.Launcher.Features.Splash;
using OmniBlock.Launcher.Features.Xbox;
using Serilog;

namespace OmniBlock.Launcher;

internal static partial class Bootstrapper
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddHttpClient();

        services.AddLogging(builder =>
        {
            // Find a way to display class names and hide HttpClient's logs.
            const string template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level} {Message:lj}{NewLine}{Exception}";

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(App.Folder, "logs", ".txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 5,
                    outputTemplate: template)
                .CreateLogger();

            builder.AddSerilog(Log.Logger);
        });

        ConfigureServices(services);

        return services.BuildServiceProvider();
    }

    // Services
    [Singleton(typeof(AuthenticationService))]
    [Singleton(typeof(NavigationService))]
    [Singleton(typeof(AlertService))]
    [Transient(typeof(TitleService))]
    [Transient(typeof(SessionService))]
    [Transient(typeof(StorageService))]
    [Transient(typeof(MinecraftService))]
    [Transient(typeof(ProcessService))]

    // VMs
    [Singleton(typeof(ShellViewModel))]
    [Singleton(typeof(HostingViewModel))]
    [Singleton(typeof(HomeViewModel))]
    [Singleton(typeof(PropertiesViewModel))]
    [Transient(typeof(AuthenticationViewModel))]
    [Transient(typeof(SplashViewModel))]

    // Views
    [Singleton(typeof(HomeView))]
    [Transient(typeof(PropertiesView))]
    [Transient(typeof(ShellView))]
    [Transient(typeof(HostingView))]
    [Transient(typeof(AuthenticationView))]
    [Transient(typeof(SplashView))]

    // ...
    [Singleton(typeof(ViewLocator))]
    [Transient(typeof(MojangClient))]
    [Transient(typeof(XboxClient))]
    [Transient(typeof(GitHubClient))]
    private static partial void ConfigureServices(IServiceCollection services);
}
