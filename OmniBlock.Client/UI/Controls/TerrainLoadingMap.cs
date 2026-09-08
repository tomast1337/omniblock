using OmniBlock.Client.Network;
using OmniBlock.Client.UI.Rendering;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls;

/// <summary>
///     A tiny top-down survey of the spawn chunks. Terrain appears as the network decodes the
///     outer 5x5 area; the central 3x3 brightens when its meshes are ready to draw.
/// </summary>
public sealed class TerrainLoadingMap(ClientWorldPreloadState preload) : UIElement
{
    private const int CellSize = 12;
    private const int Gap = 3;
    private const int GridSize = 5;
    private float _time;

    public override void Update(float partialTicks)
    {
        base.Update(partialTicks);
        _time += 0.08f;
    }

    public override void Measure(MeasureContext context)
    {
        ComputedWidth = Style.Width ?? GridSize * CellSize + (GridSize - 1) * Gap + 8;
        ComputedHeight = Style.Height ?? GridSize * CellSize + (GridSize - 1) * Gap + 8;
    }

    public override void Render(UIRenderer renderer)
    {
        var gridWidth = GridSize * CellSize + (GridSize - 1) * Gap;
        var startX = (ComputedWidth - gridWidth) / 2;
        var startY = (ComputedHeight - gridWidth) / 2;
        var scanIndex = (int)_time % (GridSize * GridSize);

        for (var z = -2; z <= 2; z++)
        for (var x = -2; x <= 2; x++)
        {
            var drawX = startX + (x + 2) * (CellSize + Gap);
            var drawY = startY + (z + 2) * (CellSize + Gap);
            var decoded = preload.IsChunkDecoded(x, z);
            var meshed = preload.HasMesh(x, z);

            // A separate offset shadow makes each square read as a small map tile instead of a
            // conventional progress-bar cell.
            renderer.DrawRect(drawX + 2, drawY + 2, CellSize, CellSize, new Color(0, 0, 0, 105));

            if (!decoded)
            {
                renderer.DrawRect(drawX, drawY, CellSize, CellSize, new Color(20, 25, 29, 180));
                DrawBorder(renderer, drawX, drawY, new Color(62, 69, 71, 150));

                if ((z + 2) * GridSize + x + 2 == scanIndex)
                    DrawBorder(renderer, drawX - 1, drawY - 1, new Color(190, 220, 196, 180), CellSize + 2);
                continue;
            }

            var variation = Math.Abs(x * 17 + z * 31) % 3;
            var terrain = variation switch
            {
                0 => new Color(84, 129, 70),
                1 => new Color(98, 143, 76),
                _ => new Color(73, 116, 68)
            };
            renderer.DrawRect(drawX, drawY, CellSize, CellSize, terrain);
            renderer.DrawRect(drawX, drawY + CellSize - 3, CellSize, 3, new Color(91, 68, 45));

            // Meshed central chunks receive a pale survey overlay. The checker detail keeps the
            // icon lively while remaining independent of the world's actual texture atlas.
            if (meshed)
            {
                renderer.DrawRect(drawX + 2, drawY + 2, 3, 3, new Color(190, 218, 139));
                renderer.DrawRect(drawX + 7, drawY + 5, 3, 3, new Color(151, 193, 111));
                DrawBorder(renderer, drawX, drawY, new Color(211, 234, 168));
            }
            else
            {
                DrawBorder(renderer, drawX, drawY, new Color(121, 151, 102));
            }
        }

        // The spawn marker breathes subtly at the center of the map.
        var center = startX + 2 * (CellSize + Gap) + CellSize / 2;
        var marker = 2 + (MathF.Sin(_time * 2.2f) > 0 ? 1 : 0);
        renderer.DrawRect(center - marker, center - marker, marker * 2, marker * 2, new Color(245, 244, 211));
        base.Render(renderer);
    }

    private static void DrawBorder(UIRenderer renderer, float x, float y, Color color, float size = CellSize)
    {
        renderer.DrawRect(x, y, size, 1, color);
        renderer.DrawRect(x, y + size - 1, size, 1, color);
        renderer.DrawRect(x, y, 1, size, color);
        renderer.DrawRect(x + size - 1, y, 1, size, color);
    }
}
