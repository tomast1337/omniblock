using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering;

public class FrustrumCuller : ICuller
{
    private readonly FrustumData _frustum = Frustum.Instance();
    private double _x;
    private double _y;
    private double _z;

    public void SetPosition(double x, double y, double z)
    {
        _x = x;
        _y = y;
        _z = z;
    }

    public bool IsBoundingBoxInFrustum(Box aabb) => IsBoxInFrustum(aabb);

    public bool IsBoxInFrustum(Box box) => _frustum.IsBoxInFrustum(box.Offset(-_x, -_y, -_z));

    /// <summary>
    ///     Copies the current six planes and camera origin into a worker-safe culler. The global
    ///     compatibility frustum is rebuilt on the render thread every frame and must never be
    ///     read asynchronously.
    /// </summary>
    internal ImmutableFrustum Capture() => new(_frustum.Frustum, _x, _y, _z);
}

internal sealed class ImmutableFrustum : ICuller
{
    private readonly FrustumData _data = new();
    private double _x;
    private double _y;
    private double _z;

    public ImmutableFrustum(ReadOnlySpan<float> planes, double x, double y, double z)
    {
        if (planes.Length != 24)
            throw new ArgumentException("A frustum snapshot requires six four-component planes.", nameof(planes));
        planes.CopyTo(_data.Frustum);
        SetPosition(x, y, z);
    }

    public bool IsBoundingBoxInFrustum(Box aabb) =>
        _data.IsBoxInFrustum(aabb.Offset(-_x, -_y, -_z));

    public void SetPosition(double x, double y, double z)
    {
        _x = x;
        _y = y;
        _z = z;
    }
}
