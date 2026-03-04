using Avalonia;
using Avalonia.Controls;

namespace BetaSharp.Launcher.Features.Home;

internal sealed partial class HomeView : UserControl
{
    public HomeView(HomeViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        _ = ((HomeViewModel?)DataContext)?.InitializeCommand.ExecuteAsync(null);
    }
}
