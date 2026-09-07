using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Items;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Rendering;
using OmniBlock.Client.UI.Screens.Menu.World;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.ListItems;

public class FlatPresetListItem(FlatPresetsScreen.PresetItem preset) : ListItem<FlatPresetsScreen.PresetItem>(preset)
{
    public override void Render(UIRenderer renderer)
    {
        base.Render(renderer);

        // Draw icon
        renderer.DrawRect(4, 4, 18, 18, Color.BackgroundBlackAlpha);

        if (Value.IconId < 256)
        {
            var block = renderer.Context.Content.Blocks.GetByProtocolId(Value.IconId);
            if (block != null)
            {
                var textureId = block.GetTexture(Side.Up);
                renderer.DrawItemIntoGui(Value.IconId, Value.IconMeta, textureId, 5, 5);
            }
        }
        else
        {
            if (renderer.Context.Content.Items.TryGetByProtocolId(Value.IconId, out var item) && item is not null)
            {
                var textureId = item.GetTextureId(Value.IconMeta);
                renderer.DrawItemIntoGui(Value.IconId, Value.IconMeta, textureId, 5, 5);
            }
        }

        renderer.DrawText(Value.Name, 26, 4, Color.White);
    }
}
