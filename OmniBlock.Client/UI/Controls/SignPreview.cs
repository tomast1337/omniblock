using OmniBlock.Blocks.Entities;
using OmniBlock.Client.UI.Rendering;

namespace OmniBlock.Client.UI.Controls;

public class SignPreview : UIElement
{
    public BlockEntitySign? Sign { get; set; }
    public float Scale { get; set; } = 120.0f;

    public override void Render(UIRenderer renderer)
    {
        if (Sign != null)
        {
            renderer.DrawSign(Sign, ComputedWidth / 2, -64, Scale);
        }

        base.Render(renderer);
    }
}
