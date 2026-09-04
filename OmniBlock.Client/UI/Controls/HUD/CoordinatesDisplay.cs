using OmniBlock.Client.UI.Rendering;
using OmniBlock.Entities;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.HUD;

public class CoordinatesDisplay(Func<Entity?> getEntity, Func<bool> showCoordinates) : UIElement
{
    public override void Render(UIRenderer renderer)
    {
        if (!showCoordinates()) return;

        var entity = getEntity();
        if (entity == null) return;

        var x = (int)Math.Floor(entity.X);
        var y = (int)Math.Floor(entity.Y);
        var z = (int)Math.Floor(entity.Z);

        renderer.DrawText($"Position: {x}, {y}, {z}", 0, 0, Color.White, shadow: true);
    }
}
