using OmniBlock.Client.Entities;
using OmniBlock.Client.UI.Rendering;
using OmniBlock.Textures;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.HUD;

public class PortalOverlay(Func<ClientPlayerEntity?> getPlayer) : UIElement
{
    private float _partialTicks;

    public override void Update(float partialTicks)
    {
        _partialTicks = partialTicks;
        base.Update(partialTicks);
    }

    public override void Render(UIRenderer renderer)
    {
        var player = getPlayer();
        if (player == null) return;

        var last = player.LastScreenDistortion;
        var curr = player.ChangeDimensionCooldown;
        var portal = last + (curr - last) * _partialTicks;

        if (portal > 0.0F)
        {
            if (portal < 1.0F)
            {
                portal *= portal;
                portal *= portal;
                portal = portal * 0.8F + 0.2F;
            }

            renderer.SetAlphaTest(false);
            renderer.PushColor(new Color(255, 255, 255, (byte)(255 * portal)));

            var tile = Atlases.Terrain.IndexOf("nether_portal");
            renderer.DrawTexturedModalRect(renderer.TextureManager.GetTextureId("/terrain.png"), 0, 0,
                tile % Atlases.Terrain.GridWidth * Atlases.Terrain.TileSize,
                tile / Atlases.Terrain.GridWidth * Atlases.Terrain.TileSize,
                ComputedWidth, ComputedHeight, 16, 16, -90.0f);
            renderer.PopColor();
            renderer.SetAlphaTest(true);
        }

        base.Render(renderer);
    }
}
