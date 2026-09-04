using OmniBlock.Client.Entities;
using OmniBlock.Client.UI.Rendering;

namespace OmniBlock.Client.UI.Controls.HUD;

public class PumpkinBlur(Func<ClientPlayerEntity?> getPlayer) : UIElement
{
    public override void Render(UIRenderer renderer)
    {
        var player = getPlayer();
        if (player == null) return;

        var head = player.Inventory.ArmorItemBySlot(3);
        if (head is { ItemId: 86 }) // Pumpkin
        {
            renderer.DrawTexture(renderer.TextureManager.GetTextureId("%blur%%clamp%/misc/pumpkinblur.png"), 0, 0, ComputedWidth, ComputedHeight);
        }

        base.Render(renderer);
    }
}
