using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.PathMovers;

/// <summary>
///     Draws a <see cref="PathMoverBuffer" /> as billboarded item icons, reusing the exact
///     instanced-quad WebGPU pipeline <see cref="Particles.ParticleRenderer" /> already built
///     (<c>particle.wgsl</c> via <see cref="WgpuParticleRenderer" />) rather than a new shader. A
///     dedicated <see cref="WgpuParticleRenderer" /> instance is used — not
///     <see cref="Particles.ParticleRenderer" />'s own — so its per-layer storage buffers can't
///     collide with the real particle system drawing the same items.png layer in the same frame.
/// </summary>
public static class PathMoverRenderer
{
    private const string ItemsTexture = "/gui/items.png";

    private static WgpuParticleRenderer? s_wgpuRenderer;
    private static readonly ParticleInstance[] s_scratch = new ParticleInstance[PathMoverBuffer.MaxMovers];

    public static void Render(
        PathMoverBuffer buf,
        float yaw, float pitch,
        double x, double y, double z,
        double lastTickX, double lastTickY, double lastTickZ,
        float partialTick,
        TextureManager textureManager)
    {
        if (buf.Count == 0)
        {
            return;
        }

        WgpuTexture? texture = textureManager.GetTextureId(ItemsTexture).Texture?.Wgpu;
        if (texture is null)
        {
            return;
        }

        float radYaw = yaw * MathF.PI / 180.0f;
        float radPitch = pitch * MathF.PI / 180.0f;

        float cosYaw = MathHelper.Cos(radYaw);
        float sinYaw = MathHelper.Sin(radYaw);
        float cosPitch = MathHelper.Cos(radPitch);
        float sinPitch = MathHelper.Sin(radPitch);

        float upX = -sinYaw * sinPitch;
        float upZ = cosYaw * sinPitch;

        double interpX = lastTickX + (x - lastTickX) * partialTick;
        double interpY = lastTickY + (y - lastTickY) * partialTick;
        double interpZ = lastTickZ + (z - lastTickZ) * partialTick;

        s_wgpuRenderer ??= new WgpuParticleRenderer();
        s_wgpuRenderer.BeginFrame();

        for (int i = 0; i < buf.Count; i++)
        {
            float rx = (float)(buf.PrevX[i] + (buf.X[i] - buf.PrevX[i]) * partialTick - interpX);
            float ry = (float)(buf.PrevY[i] + (buf.Y[i] - buf.PrevY[i]) * partialTick - interpY);
            float rz = (float)(buf.PrevZ[i] + (buf.Z[i] - buf.PrevZ[i]) * partialTick - interpZ);

            int iconIndex = buf.IconIndex[i];
            // Same 16-column/256px-atlas math as ItemRenderer's icon lookup and
            // ParticleRenderer.ComputeUVs's UVModel.Standard16x16 case.
            float minU = (iconIndex % 16) / 16.0f;
            float maxU = minU + 0.999f / 16.0f;
            float minV = (iconIndex / 16) / 16.0f;
            float maxV = minV + 0.999f / 16.0f;

            s_scratch[i] = new ParticleInstance
            {
                Pos = new System.Numerics.Vector3(rx, ry, rz),
                Size = 0.1f * buf.Scale[i],
                Color = new System.Numerics.Vector4(1f, 1f, 1f, 1f),
                UvMin = new System.Numerics.Vector2(minU, minV),
                UvMax = new System.Numerics.Vector2(maxU, maxV),
            };
        }

        WebGpuDevice device = WebGpuDevice.Current!;
        System.Numerics.Vector3 right = new(cosYaw, 0.0f, sinYaw);
        System.Numerics.Vector3 up = new(upX, cosPitch, upZ);

        s_wgpuRenderer.DrawLayer(device, texture,
            GLManager.ModelView.Top, GLManager.Projection.Top,
            right, up, layer: 0, s_scratch.AsSpan(0, buf.Count));
    }
}
