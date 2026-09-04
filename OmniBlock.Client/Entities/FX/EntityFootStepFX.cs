using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Entities.FX;

public class EntityFootStepFX : EntityFX
{
    private readonly int maxAge;
    private readonly TextureManager textureManager;

    private int localAge;

    public EntityFootStepFX(TextureManager textureManager, World world, double x, double y, double z) : base(world, x, y, z, 0.0D, 0.0D, 0.0D)
    {
        this.textureManager = textureManager;
        VelocityX = VelocityY = VelocityZ = 0.0D;
        maxAge = 200;
    }

    public override void renderParticle(Tessellator t, float partialTick, float rotX, float rotY, float rotZ, float upX, float upZ)
    {
        var lifeProgress = (localAge + partialTick) / maxAge;
        lifeProgress *= lifeProgress;
        var alpha = 2.0F - lifeProgress * 2.0F;
        if (alpha > 1.0F)
        {
            alpha = 1.0F;
        }

        alpha *= 0.2F;
        GLManager.LightingEnabled = false;
        var footprintSize = 2.0F / 16.0F;
        var renderX = (float)(X - interpPosX);
        var renderY = (float)(Y - interpPosY);
        var renderZ = (float)(Z - interpPosZ);
        var brightness = World.Lighting.GetLuminance(MathHelper.Floor(X), MathHelper.Floor(Y), MathHelper.Floor(Z));
        textureManager.BindTexture(textureManager.GetTextureId("/misc/footprint.png"));
        GLManager.State.Apply(RenderState.Entity with
        {
            Blend = BlendMode.Alpha
        });
        t.startDrawingQuads();
        t.setColorRGBA_F(brightness, brightness, brightness, alpha);
        t.addVertexWithUV(renderX - footprintSize, renderY, renderZ + footprintSize, 0.0D, 1.0D);
        t.addVertexWithUV(renderX + footprintSize, renderY, renderZ + footprintSize, 1.0D, 1.0D);
        t.addVertexWithUV(renderX + footprintSize, renderY, renderZ - footprintSize, 1.0D, 0.0D);
        t.addVertexWithUV(renderX - footprintSize, renderY, renderZ - footprintSize, 0.0D, 0.0D);
        t.draw(ProgramSlot.Entities);
        GLManager.State.Apply(RenderState.Entity);
        GLManager.LightingEnabled = true;
    }

    public override void Tick()
    {
        ++localAge;
        if (localAge == maxAge)
        {
            MarkDead();
        }
    }

    public override int getFXLayer() => 3;
}
