using System.ComponentModel;
using BetaSharp.Launcher.Splash;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Shell;

internal sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    public partial INotifyPropertyChanged Current { get; set; }

    public ShellViewModel(SplashViewModel splashViewModel)
    {
        Current = splashViewModel;

        WeakReferenceMessenger.Default.Register<NavigationMessage>(this, (_, message) => Current = message.Destination);
    }
}
