using System.ComponentModel;

namespace BetaSharp.Launcher.Features.Messages;

internal sealed class NavigationMessage(INotifyPropertyChanged destination)
{
    public INotifyPropertyChanged Destination => destination;
}
