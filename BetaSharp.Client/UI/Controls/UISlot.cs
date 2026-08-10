using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.UI.Rendering;
using OmniBlock.Items;
using OmniBlock.Screens.Slots;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls;

public class UISlot : UIElement
{
    public UISlot(Slot slot)
    {
        Slot = slot;
        Style.Width = 16;
        Style.Height = 16;
    }

    public Slot Slot { get; }
    public bool Highlighted { get; set; }

    public override void Render(UIRenderer renderer)
    {
        ItemStack stack = Slot.getStack();

        if (stack == null)
        {
            int iconIdx = Slot.getBackgroundTextureId();
            if (iconIdx >= 0)
            {
                // Background icon (e.g. for armor slots)
                TextureHandle texture = renderer.TextureManager.GetTextureId("/gui/items.png");
                renderer.DrawTexturedModalRect(texture, 0, 0, iconIdx % 16 * 16, iconIdx / 16 * 16, 16, 16);
            }
        }
        else
        {
            renderer.DrawItem(stack, 0, 0);
            renderer.DrawItemOverlay(stack, 0, 0);
        }

        if (IsHovered || Highlighted)
        {
            renderer.DrawRect(0, 0, 16, 16, Color.BackgroundWhiteAlpha);
        }

        base.Render(renderer);
    }
}
