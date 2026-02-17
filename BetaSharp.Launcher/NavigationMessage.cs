using System.ComponentModel;

namespace BetaSharp.Launcher;

internal sealed class NavigationMessage(INotifyPropertyChanged destination)
{
    public INotifyPropertyChanged Destination => destination;
}
