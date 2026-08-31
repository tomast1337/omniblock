using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Items;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Rendering;
using OmniBlock.Client.UI.Screens.Menu.World;
using OmniBlock.Items;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.ListItems;

public class FlatPresetListItem(FlatPresetsScreen.PresetItem preset) : ListItem<FlatPresetsScreen.PresetItem>(preset)
{
    private static readonly ItemRenderer s_itemRenderer = new();

    public override void Render(UIRenderer renderer)
    {
        base.Render(renderer);

        // Draw icon
        renderer.DrawRect(4, 4, 18, 18, Color.BackgroundBlackAlpha);

        if (Value.IconId < 256)
        {
            Block block = BlockRegistry.GetByProtocolId(Value.IconId);
            if (block != null)
            {
                int textureId = block.GetTexture(Side.Up);
                renderer.DrawItemIntoGui(s_itemRenderer, Value.IconId, Value.IconMeta, textureId, 5, 5);
            }
        }
        else
        {
            Item item = Item.Items[Value.IconId];
            if (item != null)
            {
                int textureId = item.GetTextureId(Value.IconMeta);
                renderer.DrawItemIntoGui(s_itemRenderer, Value.IconId, Value.IconMeta, textureId, 5, 5);
            }
        }

        renderer.DrawText(Value.Name, 26, 4, Color.White);
    }
}
