using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering;

public interface ICuller
{
    bool IsBoundingBoxInFrustum(Box aabb);

    void SetPosition(double x, double y, double z);
}
