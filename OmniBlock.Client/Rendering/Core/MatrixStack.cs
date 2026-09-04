using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Core;

public class MatrixStack
{
    private readonly Stack<Matrix4X4<float>> _stack = new();

    public Matrix4X4<float> Top { get; private set; } = Matrix4X4<float>.Identity;

    /// <summary>
    ///     Counts mutations, so a consumer can tell whether what it last uploaded is still current.
    /// </summary>
    /// <remarks>
    ///     Replaces a dirty flag that the stack's owner had to remember to set. A flag only works
    ///     while every mutation goes through the one wrapper that sets it; a version is carried by
    ///     the stack itself, so a caller holding this directly cannot silently skip the
    ///     notification. That is what makes it safe to hand this out rather than hide it.
    /// </remarks>
    public uint Version { get; private set; }

    public void LoadIdentity()
    {
        Top = Matrix4X4<float>.Identity;
        Version++;
    }

    /// <summary>Replaces the top with a matrix the caller has already built.</summary>
    public void Load(Matrix4X4<float> matrix)
    {
        Top = matrix;
        Version++;
    }

    public void Push()
    {
        _stack.Push(Top);
        Version++;
    }

    public void Pop()
    {
        if (_stack.Count > 0)
        {
            Top = _stack.Pop();
        }

        Version++;
    }

    public void Translate(float x, float y, float z)
    {
        Top = Matrix4X4.CreateTranslation(x, y, z) * Top;
        Version++;
    }

    public void Scale(float x, float y, float z)
    {
        Top = Matrix4X4.CreateScale(x, y, z) * Top;
        Version++;
    }

    public void Rotate(float angleDeg, float x, float y, float z)
    {
        Version++;
        var angleRad = angleDeg * (MathF.PI / 180.0f);
        var len = MathF.Sqrt(x * x + y * y + z * z);

        if (len > 0.0001f)
        {
            x /= len;
            y /= len;
            z /= len;
            Top = Matrix4X4.CreateFromAxisAngle(new Vector3D<float>(x, y, z), angleRad) * Top;
        }
    }

    public void Ortho(double left, double right, double bottom, double top, double zNear, double zFar)
    {
        Top *= Matrix4X4.CreateOrthographicOffCenter((float)left, (float)right, (float)bottom, (float)top, (float)zNear, (float)zFar);
        Version++;
    }

    public void Frustum(double left, double right, double bottom, double top, double zNear, double zFar)
    {
        Top *= Matrix4X4.CreatePerspectiveOffCenter((float)left, (float)right, (float)bottom, (float)top, (float)zNear, (float)zFar);
        Version++;
    }
}
