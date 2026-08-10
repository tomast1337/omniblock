using OmniBlock.Client.Entities.FX;
using OmniBlock.Client.Rendering.Core;

namespace OmniBlock.Client.Rendering.Particles;

public class LegacyParticleAdapter(EntityFX fx) : ISpecialParticle
{
    public bool IsDead => fx.Dead;
    public void Tick() => fx.Tick();
    public void Render(Tessellator t, float partialTick, double interpX, double interpY, double interpZ)
    {
        EntityFX.interpPosX = interpX;
        EntityFX.interpPosY = interpY;
        EntityFX.interpPosZ = interpZ;
        fx.renderParticle(t, partialTick, 0, 0, 0, 0, 0);
    }
}
