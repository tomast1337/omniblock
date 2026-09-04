using System.Numerics;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Rendering.Particles;

public static class ParticleRenderer
{
    private static readonly string[] s_layerTextures =
    [
        "/particles.png",
        "/terrain.png",
        "/gui/items.png"
    ];

    // Lazily built: constructing it touches WebGpuDevice.Current, which does not exist under GL.
    private static WgpuParticleRenderer? s_wgpuRenderer;

    // Reused across frames and layers rather than allocated per draw — the WebGPU path is the one
    // this exists to avoid a CPU-side per-frame allocation for in the first place.
    private static readonly ParticleInstance[] s_instanceScratch = new ParticleInstance[ParticleBuffer.MaxParticles];

    public static void Render(
        ParticleBuffer[] layers,
        float yaw, float pitch,
        double x, double y, double z,
        double lastTickX, double lastTickY, double lastTickZ,
        float partialTick,
        TextureManager textureManager,
        IWorldContext world)
    {
        var radYaw = yaw * MathF.PI / 180.0f;
        var radPitch = pitch * MathF.PI / 180.0f;

        var cosYaw = MathHelper.Cos(radYaw);
        var sinYaw = MathHelper.Sin(radYaw);
        var cosPitch = MathHelper.Cos(radPitch);
        var sinPitch = MathHelper.Sin(radPitch);

        // Billboarding vecs (vertical orientation of the particle quad)
        var upX = -sinYaw * sinPitch;
        var upZ = cosYaw * sinPitch;

        var interpX = lastTickX + (x - lastTickX) * partialTick;
        var interpY = lastTickY + (y - lastTickY) * partialTick;
        var interpZ = lastTickZ + (z - lastTickZ) * partialTick;

        RenderWebGpu(layers, cosYaw, sinYaw, cosPitch, upX, upZ,
            interpX, interpY, interpZ, partialTick, textureManager, world);
    }

    /// <summary>
    ///     The WebGPU path: for each layer, packs one <see cref="ParticleInstance" /> per particle
    ///     (no quad expansion) and hands the whole layer to <see cref="WgpuParticleRenderer" /> as a
    ///     single instanced draw.
    /// </summary>
    private static void RenderWebGpu(
        ParticleBuffer[] layers,
        float cosYaw, float sinYaw, float cosPitch, float upX, float upZ,
        double interpX, double interpY, double interpZ,
        float partialTick, TextureManager textureManager, IWorldContext world)
    {
        s_wgpuRenderer ??= new WgpuParticleRenderer();
        s_wgpuRenderer.BeginFrame();

        Vector3 right = new(cosYaw, 0.0f, sinYaw);
        Vector3 up = new(upX, cosPitch, upZ);

        var device = WebGpuDevice.Current!;

        for (var layer = 0; layer < 3; layer++)
        {
            var buf = layers[layer];
            if (buf.Count == 0)
            {
                continue;
            }

            var texture = textureManager.GetTextureId(s_layerTextures[layer]).Texture?.Wgpu;
            if (texture is null)
            {
                continue;
            }

            for (var i = 0; i < buf.Count; i++)
            {
                ref readonly var config = ref ParticleTypeConfig.Configs[(int)buf.Type[i]];

                var rx = (float)(buf.PrevX[i] + (buf.X[i] - buf.PrevX[i]) * partialTick - interpX);
                var ry = (float)(buf.PrevY[i] + (buf.Y[i] - buf.PrevY[i]) * partialTick - interpY);
                var rz = (float)(buf.PrevZ[i] + (buf.Z[i] - buf.PrevZ[i]) * partialTick - interpZ);

                var scale = ComputeScale(config.Scale, buf, i, partialTick);
                var size = 0.1f * scale;

                var brightness = ComputeBrightness(config.Brightness, buf, i, partialTick, world);

                ComputeUVs(config.UV, buf.TextureIndex[i], buf.TexJitterX[i], buf.TexJitterY[i],
                    out var minU, out var maxU, out var minV, out var maxV);

                s_instanceScratch[i] = new ParticleInstance
                {
                    Pos = new Vector3(rx, ry, rz),
                    Size = size,
                    Color = new Vector4(
                        buf.Red[i] * brightness, buf.Green[i] * brightness, buf.Blue[i] * brightness, 1.0f),
                    // The Tessellator path's corner order pairs (minU,minV) with the opposite corner
                    // from (maxU,maxV) — see the vertex order above — which is exactly uvMin/uvMax.
                    UvMin = new Vector2(minU, minV),
                    UvMax = new Vector2(maxU, maxV)
                };
            }

            s_wgpuRenderer.DrawLayer(device, texture,
                GLManager.ModelView.Top, GLManager.Projection.Top,
                right, up, layer, s_instanceScratch.AsSpan(0, buf.Count));
        }
    }

    public static void RenderSpecial(List<ISpecialParticle> specialParticles,
        double x, double y, double z,
        double lastTickX, double lastTickY, double lastTickZ,
        float partialTick)
    {
        if (specialParticles.Count == 0)
        {
            return;
        }

        var interpX = lastTickX + (x - lastTickX) * partialTick;
        var interpY = lastTickY + (y - lastTickY) * partialTick;
        var interpZ = lastTickZ + (z - lastTickZ) * partialTick;

        var t = Tessellator.instance;
        for (var i = 0; i < specialParticles.Count; i++)
        {
            specialParticles[i].Render(t, partialTick, interpX, interpY, interpZ);
        }
    }

    private static float ComputeScale(ScaleModel model, ParticleBuffer buf, int i, float partialTick)
    {
        var progress = (buf.Age[i] + partialTick) / buf.MaxAge[i];

        return model switch
        {
            ScaleModel.Constant => buf.BaseScale[i],
            ScaleModel.GrowToFull => buf.BaseScale[i] * Math.Clamp(progress * 32.0f, 0.0f, 1.0f),
            ScaleModel.ShrinkHalf => buf.BaseScale[i] * (1.0f - progress * progress * 0.5f),
            ScaleModel.ShrinkSquared => buf.BaseScale[i] * (1.0f - progress * progress),
            ScaleModel.PortalEase => buf.BaseScale[i] * (1.0f - (1.0f - progress) * (1.0f - progress)),
            _ => buf.BaseScale[i]
        };
    }

    private static float ComputeBrightness(BrightnessModel model, ParticleBuffer buf, int i,
        float partialTick, IWorldContext world)
    {
        switch (model)
        {
            case BrightnessModel.AlwaysFull: return 1.0f;
            case BrightnessModel.FadeFromFull:
                {
                    var p = Math.Clamp((buf.Age[i] + partialTick) / buf.MaxAge[i], 0.0f, 1.0f);
                    var worldBright = GetWorldBrightness(buf, i, world);
                    return worldBright * p + (1.0f - p);
                }
            case BrightnessModel.EaseToFull:
                {
                    var p = Math.Clamp((buf.Age[i] + partialTick) / buf.MaxAge[i], 0.0f, 1.0f);
                    var worldBright = GetWorldBrightness(buf, i, world);
                    var ease = p * p * p * p; // Quartic ease-in
                    return worldBright * (1.0f - ease) + ease;
                }
            default: return GetWorldBrightness(buf, i, world); // WorldBased
        }
    }

    private static float GetWorldBrightness(ParticleBuffer buf, int i, IWorldContext world)
    {
        // World sampling uses the Floor of coordinates to find the specific block voxel
        var bx = MathHelper.Floor(buf.X[i]);
        var by = MathHelper.Floor(buf.Y[i]);
        var bz = MathHelper.Floor(buf.Z[i]);
        return world.Lighting.GetLuminance(bx, by, bz);
    }

    private static void ComputeUVs(UVModel model, int textureIndex, float jitterX, float jitterY,
        out float minU, out float maxU, out float minV, out float maxV)
    {
        // 0.999f is used to clamp UVs slightly inside the tile boundary to prevent texture bleeding at quad edges
        switch (model)
        {
            case UVModel.Jittered4x4:
                {
                    minU = (textureIndex % 16 + jitterX / 4.0f) / 16.0f;
                    maxU = minU + 0.999f / 64.0f;
                    minV = (textureIndex / 16 + jitterY / 4.0f) / 16.0f;
                    maxV = minV + 0.999f / 64.0f;
                    break;
                }
            case UVModel.Standard16x16:
            default:
                {
                    minU = textureIndex % 16 / 16.0f;
                    maxU = minU + 0.999f / 16.0f;
                    minV = textureIndex / 16 / 16.0f;
                    maxV = minV + 0.999f / 16.0f;
                    break;
                }
        }
    }
}
