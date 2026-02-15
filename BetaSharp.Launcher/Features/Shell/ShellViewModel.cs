using System.ComponentModel;
using BetaSharp.Launcher.Features.New;
using BetaSharp.Launcher.Features.Splash;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetaSharp.Launcher.Features.Shell;

internal sealed partial class ShellViewModel(SplashViewModel splashViewModel, NewViewModel newViewModel) : ObservableObject
{
    [ObservableProperty]
    public partial INotifyPropertyChanged Current { get; set; } = newViewModel;
}