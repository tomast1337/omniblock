using System;
using System.ComponentModel;
using OmniBlock.Launcher.Features.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace OmniBlock.Launcher.Features;

internal sealed class NavigationService(ShellViewModel shellViewModel, IServiceProvider services)
{
    public void Navigate<T>() where T : INotifyPropertyChanged
    {
        shellViewModel.Current = services.GetRequiredService<T>();
    }
}
