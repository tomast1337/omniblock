using CommunityToolkit.Mvvm.ComponentModel;

namespace BetaSharp.Launcher.Features.Shell;

internal sealed partial class ShellViewModel : ObservableObject
{
    public string Greeting { get; } = "Welcome to Avalonia!";
}