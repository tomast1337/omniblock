using OmniBlock.Client.Rendering.Core;

namespace OmniBlock.Client.Rendering.Particles;

public interface ISpecialParticle
{
    bool IsDead { get; }
    void Tick();
    void Render(Tessellator t, float partialTick, double interpX, double interpY, double interpZ);
}
