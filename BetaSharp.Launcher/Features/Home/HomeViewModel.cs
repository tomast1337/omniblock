using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BetaSharp.Launcher.Features.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BetaSharp.Launcher.Features.Home;

internal sealed partial class HomeViewModel(AccountService accountService, MinecraftService minecraftService) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsReady { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "...";

    [ObservableProperty]
    public partial CroppedBitmap? Face { get; set; }

    private string? _token;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await Task.Yield();

        var account = await accountService.GetAsync();

        if (account is null)
        {
            WeakReferenceMessenger.Default.Send(new NavigationMessage(Destination.Authentication));
            return;
        }

        IsReady = true;

        Name = account.Name;

        ArgumentException.ThrowIfNullOrWhiteSpace(account.Skin);

        Face = await minecraftService.GetFaceAsync(account.Skin);

        _token = account.Token;
    }
}
