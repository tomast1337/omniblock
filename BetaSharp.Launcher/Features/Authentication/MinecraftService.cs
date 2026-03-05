using System.Linq;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Mojang;
using BetaSharp.Launcher.Features.Mojang.Token;
using BetaSharp.Launcher.Features.Xbox;

namespace BetaSharp.Launcher.Features.Authentication;

internal sealed class MinecraftService(XboxClient xboxClient, MojangClient mojangClient)
{
    public async Task<TokenResponse?> TryGetTokenAsync(string token)
    {
        var user = await xboxClient.GetUserAsync(token);
        var xbox = await xboxClient.GetTokenAsync(user.Token);

        var mojang = await mojangClient.GetTokenAsync(xbox.Value, user.DisplayClaims.Xui[0].Uhs);
        var entitlements = await mojangClient.GetEntitlementsAsync(mojang.Value);

        return entitlements.Items.Any(item => item.Name is "product_minecraft" or "game_minecraft") ? mojang : null;
    }
}
