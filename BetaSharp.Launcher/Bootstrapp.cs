using System;
using BetaSharp.Launcher.Features.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace BetaSharp.Launcher;

internal static class Bootstrapper
{
    public static IServiceProvider Build()
    {
        var builder = new ServiceCollection();

        builder
            .AddTransient<ShellView>()
            .AddTransient<ShellViewModel>();

        return builder.BuildServiceProvider();
    }
}