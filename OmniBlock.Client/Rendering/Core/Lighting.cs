using OmniBlock.Util.Maths;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Core;

public class Lighting
{
    public static void turnOff() => RenderSystem.LightingEnabled = false;

    public static void turnOnGui()
    {
        RenderSystem.ModelView.Push();
        RenderSystem.ModelView.Rotate(120.0F, 1.0F, 0.0F, 0.0F);
        turnOn();
        RenderSystem.ModelView.Pop();
    }

    public static void turnOn(bool mirrored = false)
    {
        const float ambient = 0.4F;
        const float diffuse = 0.6F;
        var mx = mirrored ? -1.0f : 1.0f;

        RenderSystem.LightingEnabled = true;
        RenderSystem.ShadeModel = ShadeModel.Flat;
        RenderSystem.Lighting = new LightingState(
            EyeSpace(new Vec3D(0.2F * mx, 1.0D, -0.7F)),
            new Vector3D<float>(diffuse, diffuse, diffuse),
            EyeSpace(new Vec3D(-0.2F * mx, 1.0D, 0.7F)),
            new Vector3D<float>(diffuse, diffuse, diffuse),
            new Vector3D<float>(ambient, ambient, ambient));
    }

    /// <summary>
    ///     A world-space light direction through the model-view, which is what the two lights are
    ///     stated in.
    /// </summary>
    /// <remarks>
    ///     The fixed-function pipeline did this inside <c>glLight(GL_POSITION)</c>, and it is why
    ///     <see cref="turnOnGui" /> can light an inventory model differently by wrapping a rotation
    ///     around its call. A direction, so the translation row plays no part.
    /// </remarks>
    private static Vector3D<float> EyeSpace(Vec3D direction)
    {
        var unit = direction.Normalize();
        float x = (float)unit.X, y = (float)unit.Y, z = (float)unit.Z;

        var mv = RenderSystem.ModelView.Top;
        Vector3D<float> eye = new(
            x * mv.M11 + y * mv.M21 + z * mv.M31,
            x * mv.M12 + y * mv.M22 + z * mv.M32,
            x * mv.M13 + y * mv.M23 + z * mv.M33);

        var length = MathF.Sqrt(eye.X * eye.X + eye.Y * eye.Y + eye.Z * eye.Z);
        return length > 0 ? eye / length : eye;
    }
}
