using System.ComponentModel;

namespace BetaSharp.Launcher.Features;

internal sealed class NavigationMessage(INotifyPropertyChanged destination)
{
    public INotifyPropertyChanged Destination => destination;
}