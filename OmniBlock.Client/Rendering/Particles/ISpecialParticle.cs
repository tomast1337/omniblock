using OmniBlock.Client.Rendering.Core;

namespace OmniBlock.Client.Rendering.Particles;

public interface ISpecialParticle
{
    bool IsDead { get; }
    double X { get; }
    double Y { get; }
    double Z { get; }
    void Tick();
    void Render(Tessellator t, float partialTick, double interpX, double interpY, double interpZ);
}
