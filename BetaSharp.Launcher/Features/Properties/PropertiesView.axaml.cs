using Avalonia.Controls;

namespace BetaSharp.Launcher.Features.Properties;

internal sealed partial class PropertiesView : UserControl
{
    public PropertiesView(PropertiesViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
