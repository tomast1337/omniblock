using System.Numerics;
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

        var texture = textureManager.GetTextureId(ItemsTexture).Texture?.Wgpu;
        if (texture is null)
        {
            return;
        }

        var radYaw = yaw * MathF.PI / 180.0f;
        var radPitch = pitch * MathF.PI / 180.0f;

        var cosYaw = MathHelper.Cos(radYaw);
        var sinYaw = MathHelper.Sin(radYaw);
        var cosPitch = MathHelper.Cos(radPitch);
        var sinPitch = MathHelper.Sin(radPitch);

        var upX = -sinYaw * sinPitch;
        var upZ = cosYaw * sinPitch;

        var interpX = lastTickX + (x - lastTickX) * partialTick;
        var interpY = lastTickY + (y - lastTickY) * partialTick;
        var interpZ = lastTickZ + (z - lastTickZ) * partialTick;

        s_wgpuRenderer ??= new WgpuParticleRenderer();
        s_wgpuRenderer.BeginFrame();

        for (var i = 0; i < buf.Count; i++)
        {
            var rx = (float)(buf.PrevX[i] + (buf.X[i] - buf.PrevX[i]) * partialTick - interpX);
            var ry = (float)(buf.PrevY[i] + (buf.Y[i] - buf.PrevY[i]) * partialTick - interpY);
            var rz = (float)(buf.PrevZ[i] + (buf.Z[i] - buf.PrevZ[i]) * partialTick - interpZ);

            var iconIndex = buf.IconIndex[i];
            // Same 16-column/256px-atlas math as ItemRenderer's icon lookup and
            // ParticleRenderer.ComputeUVs's UVModel.Standard16x16 case.
            var minU = iconIndex % 16 / 16.0f;
            var maxU = minU + 0.999f / 16.0f;
            var minV = iconIndex / 16 / 16.0f;
            var maxV = minV + 0.999f / 16.0f;

            s_scratch[i] = new ParticleInstance
            {
                Pos = new Vector3(rx, ry, rz),
                Size = 0.1f * buf.Scale[i],
                Color = new Vector4(1f, 1f, 1f, 1f),
                UvMin = new Vector2(minU, minV),
                UvMax = new Vector2(maxU, maxV)
            };
        }

        var device = WebGpuDevice.Current!;
        Vector3 right = new(cosYaw, 0.0f, sinYaw);
        Vector3 up = new(upX, cosPitch, upZ);

        s_wgpuRenderer.DrawLayer(device, texture,
            RenderSystem.ModelView.Top, RenderSystem.Projection.Top,
            right, up, 0, s_scratch.AsSpan(0, buf.Count));
    }
}
