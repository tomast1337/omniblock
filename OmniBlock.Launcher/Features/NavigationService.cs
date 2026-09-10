using System;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using OmniBlock.Launcher.Features.Shell;

namespace OmniBlock.Launcher.Features;

internal sealed class NavigationService(ShellViewModel shellViewModel, IServiceProvider services)
{
    public void Navigate<T>() where T : INotifyPropertyChanged => shellViewModel.Current = services.GetRequiredService<T>();
}
