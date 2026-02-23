using System;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Accounts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetaSharp.Launcher.Features.Home;

internal sealed partial class HomeViewModel(AccountsService accountsService) : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = "...";

    private Account? _account;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        _account = await accountsService.GetAsync();

        ArgumentNullException.ThrowIfNull(_account);

        Name = _account.Name;
    }
}
