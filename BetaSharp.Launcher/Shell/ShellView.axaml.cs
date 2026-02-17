using Avalonia.Controls;

namespace BetaSharp.Launcher.Shell;

internal sealed partial class ShellView : Window
{
    public ShellView(ShellViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
