using Avalonia.Controls;

namespace OmniBlock.Launcher.Features.Hosting;

internal sealed partial class HostingView : UserControl
{
    public HostingView(HostingViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
