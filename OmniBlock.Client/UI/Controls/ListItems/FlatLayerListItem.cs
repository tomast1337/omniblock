using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Items;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Rendering;
using OmniBlock.Worlds.Gen.Flat;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.ListItems;

public class FlatLayerListItem(FlatLayerInfo layer) : ListItem<FlatLayerInfo>(layer)
{
    public override void Render(UIRenderer renderer)
    {
        base.Render(renderer);

        var block = renderer.Context.Content.Blocks.GetByProtocolId(Value.FillBlock);
        var blockName = block?.TranslateBlockName() ?? Translations.Get("newWorld.customize.unknown");

        renderer.DrawRect(4, 4, 18, 18, Color.BackgroundBlackAlpha);

        if (block != null)
        {
            var textureId = block.GetTexture(Side.Up);
            renderer.DrawItemIntoGui(Value.FillBlock, Value.FillBlockMeta, textureId, 5, 5);
        }

        renderer.DrawText(blockName, 26, 4, Color.White);
        renderer.DrawText($"{Translations.Get("newWorld.customize.height")}: {Value.LayerCount}", 26, 16, Color.Gray80);
    }
}
