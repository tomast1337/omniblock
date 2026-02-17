using System.ComponentModel;

namespace BetaSharp.Launcher.Messages;

internal sealed class NavigationMessage(INotifyPropertyChanged destination)
{
    public INotifyPropertyChanged Destination => destination;
}
