using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Entities;

public class LightningEntityRenderer : EntityRenderer
{
    /// <summary>
    ///     The bolt, which only ever brightens what is behind it.
    /// </summary>
    /// <remarks>
    ///     Everything but the blending comes from <see cref="RenderState.Entity" />, so the bolt is
    ///     unculled like the rest of the entity tree and depth tested against the world in front of
    ///     it. Saying all of it is the point, since a pipeline is selected whole rather than
    ///     adjusted a field at a time.
    /// </remarks>
    private static readonly RenderState s_bolt = RenderState.Entity with
    {
        Blend = BlendMode.AdditiveByAlpha
    };

    public void render(long renderSeed, double x, double y, double z)
    {
        var tessellator = Tessellator.instance;

        // Texturing and lighting are shader uniforms underneath rather than pipeline state, so they
        // stay as they are and are not part of the state above.
        GLManager.TextureEnabled = false;
        GLManager.LightingEnabled = false;
        GLManager.State.Apply(s_bolt);
        var xOffsets = new double[8];
        var zOffsets = new double[8];
        var offsetX = 0.0D;
        var offsetZ = 0.0D;
        JavaRandom random = new(renderSeed);

        for (var segmentIndex = 7; segmentIndex >= 0; --segmentIndex)
        {
            xOffsets[segmentIndex] = offsetX;
            zOffsets[segmentIndex] = offsetZ;
            offsetX += random.NextInt(11) - 5;
            offsetZ += random.NextInt(11) - 5;
        }

        for (var layerIndex = 0; layerIndex < 4; ++layerIndex)
        {
            JavaRandom branchRandom = new(renderSeed);

            for (var branchDepth = 0; branchDepth < 3; ++branchDepth)
            {
                var startIndex = 7;
                var endIndex = 0;
                if (branchDepth > 0)
                {
                    startIndex = 7 - branchDepth;
                }

                if (branchDepth > 0)
                {
                    endIndex = startIndex - 2;
                }

                var branchX = xOffsets[startIndex] - offsetX;
                var branchZ = zOffsets[startIndex] - offsetZ;

                for (var yIndex = startIndex; yIndex >= endIndex; --yIndex)
                {
                    var prevBranchX = branchX;
                    var prevBranchZ = branchZ;
                    if (branchDepth == 0)
                    {
                        branchX += branchRandom.NextInt(11) - 5;
                        branchZ += branchRandom.NextInt(11) - 5;
                    }
                    else
                    {
                        branchX += branchRandom.NextInt(31) - 15;
                        branchZ += branchRandom.NextInt(31) - 15;
                    }

                    tessellator.startDrawing(5);
                    var alphaScale = 0.5F;
                    tessellator.setColorRGBA_F(0.9F * alphaScale, 0.9F * alphaScale, 1.0F * alphaScale, 0.3F);
                    var outerRadius = 0.1D + layerIndex * 0.2D;
                    if (branchDepth == 0)
                    {
                        outerRadius *= yIndex * 0.1D + 1.0D;
                    }

                    var innerRadius = 0.1D + layerIndex * 0.2D;
                    if (branchDepth == 0)
                    {
                        innerRadius *= (yIndex - 1) * 0.1D + 1.0D;
                    }

                    for (var cornerIndex = 0; cornerIndex < 5; ++cornerIndex)
                    {
                        var outerX = x + 0.5D - outerRadius;
                        var outerZ = z + 0.5D - outerRadius;
                        if (cornerIndex == 1 || cornerIndex == 2)
                        {
                            outerX += outerRadius * 2.0D;
                        }

                        if (cornerIndex == 2 || cornerIndex == 3)
                        {
                            outerZ += outerRadius * 2.0D;
                        }

                        var innerX = x + 0.5D - innerRadius;
                        var innerZ = z + 0.5D - innerRadius;
                        if (cornerIndex == 1 || cornerIndex == 2)
                        {
                            innerX += innerRadius * 2.0D;
                        }

                        if (cornerIndex == 2 || cornerIndex == 3)
                        {
                            innerZ += innerRadius * 2.0D;
                        }

                        tessellator.addVertex(innerX + branchX, y + yIndex * 16, innerZ + branchZ);
                        tessellator.addVertex(outerX + prevBranchX, y + (yIndex + 1) * 16, outerZ + prevBranchZ);
                    }

                    tessellator.draw(ProgramSlot.Basic);
                }
            }
        }

        GLManager.State.Apply(RenderState.Entity);
        GLManager.LightingEnabled = true;
        GLManager.TextureEnabled = true;
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        var renderSeed = target.Behaviors.Find<LightningStrikeBehavior>()?.RenderSeed(target) ?? 0L;
        render(renderSeed, x, y, z);
    }
}
