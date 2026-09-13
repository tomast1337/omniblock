using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Entities;

public abstract class EntityRenderer
{
    internal IEntityLodProvider? LodProvider { get; init; }
    protected float ShadowRadius = 0.0F;
    protected float ShadowStrength = 1.0F;
    public EntityRenderDispatcher Dispatcher { get; set; } = null!;
    internal EntityPresentationPose? PresentationPose { get; set; }
    internal float PresentationOpacity { get; set; } = 1.0f;
    internal bool PresentationOnly { get; set; }

    protected RenderState PresentationState(RenderState state) => PresentationOpacity < 1.0f
        ? state with { Blend = BlendMode.Alpha }
        : state;

    protected World World => Dispatcher.World;
    public TextRenderer TextRenderer => Dispatcher.getTextRenderer();

    public abstract void Render(Entity target, double x, double y, double z, float yaw, float tickDelta);

    protected void loadTexture(string path)
    {
        var textureManager = Dispatcher.TextureManager;
        if (textureManager == null) return;

        var handle = textureManager.GetTextureId(path);

        // Every entity texture reaches the batch through here, so this is the one place the path
        // is known alongside the GL id the shader will see.
        EntityBatchRenderer.Instance.RegisterTextureByPath(path, (uint)handle.Id);

        // Drain the batch first: it still holds geometry belonging to the previous texture, and
        // flushing binds that texture. Binding ours before the flush would only get overwritten.
        EntityBatchRenderer.Instance.SetTexture((uint)handle.Id);
        textureManager.BindTexture(handle);
    }

    protected bool LoadDownloadableImageTexture(string? url, string fallbackPath)
    {
        if (!string.IsNullOrEmpty(url))
        {
            var skinHandle = Dispatcher.SkinManager?.GetTextureHandle(url);
            if (skinHandle != null)
            {
                EntityBatchRenderer.Instance.SetTexture((uint)skinHandle.Id);
                skinHandle.Bind();
                return true;
            }
        }

        if (string.IsNullOrEmpty(fallbackPath)) return false;

        loadTexture(fallbackPath);
        return true;
    }

    private void RenderOnFire(Entity ent, Vec3D pos, float tickDelta)
    {
        RenderSystem.LightingEnabled = false;

        var textureId = World.Content.Blocks.Get("fire").TextureId;
        var texX = (textureId & 15) << 4;
        var texY = textureId & 240;

        float minU;
        float maxU;
        float minV;
        float maxV;

        RenderSystem.ModelView.Push();
        RenderSystem.ModelView.Translate((float)pos.X, (float)pos.Y, (float)pos.Z);

        var scale = ent.Width * 1.4F;
        RenderSystem.ModelView.Scale(scale, scale, scale);

        loadTexture("/terrain.png");
        var tess = Tessellator.instance;

        var widthOffset = 0.5F;
        var depthOffset = 0.0F;
        var heightRatio = ent.Height / scale;
        var yOffset = (float)(ent.Y - ent.BoundingBox.MinY);

        RenderSystem.ModelView.Rotate(-Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
        RenderSystem.ModelView.Translate(0.0F, 0.0F, -0.3F + (int)heightRatio * 0.02F);
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);

        var zOffset = 0.0F;
        var pass = 0;

        tess.startDrawingQuads();

        while (heightRatio > 0.0F)
        {
            if (pass % 2 == 0)
            {
                minU = texX / 256.0F;
                maxU = (texX + 15.99F) / 256.0F;
                minV = texY / 256.0F;
                maxV = (texY + 15.99F) / 256.0F;
            }
            else
            {
                minU = texX / 256.0F;
                maxU = (texX + 15.99F) / 256.0F;
                minV = (texY + 16) / 256.0F;
                maxV = (texY + 16 + 15.99F) / 256.0F;
            }

            if (pass / 2 % 2 == 0)
            {
                (maxU, minU) = (minU, maxU);
            }

            tess.addVertexWithUV(widthOffset - depthOffset, 0.0F - yOffset, zOffset, maxU, maxV);
            tess.addVertexWithUV(-widthOffset - depthOffset, 0.0F - yOffset, zOffset, minU, maxV);
            tess.addVertexWithUV(-widthOffset - depthOffset, 1.4F - yOffset, zOffset, minU, minV);
            tess.addVertexWithUV(widthOffset - depthOffset, 1.4F - yOffset, zOffset, maxU, minV);

            heightRatio -= 0.45F;
            yOffset -= 0.45F;
            widthOffset *= 0.9F;
            zOffset += 0.03F;
            ++pass;
        }

        tess.draw(ProgramSlot.Entities);
        RenderSystem.ModelView.Pop();
        RenderSystem.LightingEnabled = true;
    }

    private void RenderShadow(Entity target, Vec3D pos, float shadowiness, float tickDelta)
    {
        // Blended and depth tested but not depth writing, and unculled like the models it sits under:
        // several shadows can overlap on the ground without the first one drawn hiding the rest.
        RenderSystem.State.Apply(RenderState.Entity with
        {
            Blend = BlendMode.Alpha,
            DepthWrite = false
        });

        var textureManager = Dispatcher.TextureManager;
        textureManager.BindTexture(textureManager.GetTextureId("%clamp%/misc/shadow.png"));

        var radius = ShadowRadius;

        var targetX = target.LastTickX + (target.X - target.LastTickX) * tickDelta;
        var targetY = target.LastTickY + (target.Y - target.LastTickY) * tickDelta + target.GetShadowRadius();
        var targetZ = target.LastTickZ + (target.Z - target.LastTickZ) * tickDelta;

        var minX = MathHelper.Floor(targetX - radius);
        var maxX = MathHelper.Floor(targetX + radius);
        var minY = MathHelper.Floor(targetY - radius);
        var maxY = MathHelper.Floor(targetY);
        var minZ = MathHelper.Floor(targetZ - radius);
        var maxZ = MathHelper.Floor(targetZ + radius);

        var dx = pos.X - targetX;
        var dy = pos.Y - targetY;
        var dz = pos.Z - targetZ;

        var tess = Tessellator.instance;
        tess.startDrawingQuads();

        for (var blockX = minX; blockX <= maxX; ++blockX)
        {
            for (var blockY = minY; blockY <= maxY; ++blockY)
            {
                for (var blockZ = minZ; blockZ <= maxZ; ++blockZ)
                {
                    var blockId = World.Reader.GetBlockId(blockX, blockY - 1, blockZ);
                    if (blockId > 0 && World.Lighting.GetLightLevel(blockX, blockY, blockZ) > 3)
                    {
                        renderShadowOnBlock(
                            World.Content.Blocks.GetByProtocolId(blockId),
                            new Vec3D(pos.X, pos.Y + target.GetShadowRadius(), pos.Z),
                            blockX, blockY, blockZ,
                            shadowiness,
                            radius,
                            new Vec3D(dx, dy + target.GetShadowRadius(), dz)
                        );
                    }
                }
            }
        }

        tess.draw(ProgramSlot.Entities);
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
        RenderSystem.State.Apply(RenderState.Entity);
    }

    private void renderShadowOnBlock(Block block, Vec3D pos, int blockX, int blockY, int blockZ, float shadowiness, float radius, Vec3D offset)
    {
        if (!block.IsFullCube()) return;

        var shadowDarkness = (shadowiness - (pos.Y - (blockY + offset.Y)) / 2.0D) * 0.5D * World.GetLuminance(blockX, blockY, blockZ);

        if (shadowDarkness < 0.0D) return;

        if (shadowDarkness > 1.0D)
            shadowDarkness = 1.0D;

        var tess = Tessellator.instance;
        tess.setColorRGBA_F(1.0F, 1.0F, 1.0F, (float)shadowDarkness);

        var minX = blockX + block.BoundingBox.MinX + offset.X;
        var maxX = blockX + block.BoundingBox.MaxX + offset.X;
        var minY = blockY + block.BoundingBox.MinY + offset.Y + 1.0D / 64.0D;
        var minZ = blockZ + block.BoundingBox.MinZ + offset.Z;
        var maxZ = blockZ + block.BoundingBox.MaxZ + offset.Z;

        var minU = (float)((pos.X - minX) / 2.0D / radius + 0.5D);
        var maxU = (float)((pos.X - maxX) / 2.0D / radius + 0.5D);
        var minV = (float)((pos.Z - minZ) / 2.0D / radius + 0.5D);
        var maxV = (float)((pos.Z - maxZ) / 2.0D / radius + 0.5D);

        tess.addVertexWithUV(minX, minY, minZ, minU, minV);
        tess.addVertexWithUV(minX, minY, maxZ, minU, maxV);
        tess.addVertexWithUV(maxX, minY, maxZ, maxU, maxV);
        tess.addVertexWithUV(maxX, minY, minZ, maxU, minV);
    }

    public static void renderShape(Box aabb, Vec3D pos)
    {
        RenderSystem.TextureEnabled = false;
        var tess = Tessellator.instance;
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);

        tess.startDrawingQuads();
        tess.setTranslationD(pos.X, pos.Y, pos.Z);

        tess.setNormal(0.0F, 0.0F, -1.0F);

        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MinZ);
        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MinZ);

        tess.setNormal(0.0F, 0.0F, 1.0F);
        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MaxZ);
        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MaxZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MaxZ);
        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MaxZ);

        tess.setNormal(0.0F, -1.0F, 0.0F);
        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MaxZ);
        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MaxZ);

        tess.setNormal(0.0F, 1.0F, 0.0F);
        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MaxZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MaxZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MinZ);
        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MinZ);

        tess.setNormal(-1.0F, 0.0F, 0.0F);
        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MaxZ);
        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MaxZ);
        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MinZ);
        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MinZ);

        tess.setNormal(1.0F, 0.0F, 0.0F);
        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MaxZ);
        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MaxZ);

        tess.setTranslationD(0.0D, 0.0D, 0.0D);
        tess.draw(ProgramSlot.Basic);
        RenderSystem.TextureEnabled = true;
    }

    public static void renderShapeFlat(Box aabb)
    {
        var tess = Tessellator.instance;
        tess.startDrawingQuads();

        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MinZ);
        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MinZ);

        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MaxZ);
        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MaxZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MaxZ);
        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MaxZ);

        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MaxZ);
        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MaxZ);

        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MaxZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MaxZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MinZ);
        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MinZ);

        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MaxZ);
        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MaxZ);
        tess.addVertex(aabb.MinX, aabb.MaxY, aabb.MinZ);
        tess.addVertex(aabb.MinX, aabb.MinY, aabb.MinZ);

        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MinZ);
        tess.addVertex(aabb.MaxX, aabb.MaxY, aabb.MaxZ);
        tess.addVertex(aabb.MaxX, aabb.MinY, aabb.MaxZ);

        tess.draw(ProgramSlot.Basic);
    }

    public void PostRender(Entity target, Vec3D pos, float yaw, float tickDelta)
    {
        if (PresentationOnly) return;
        if (ShadowRadius > 0.0F)
        {
            var distance = Dispatcher.GetSquareDistanceTo(target.X, target.Y, target.Z);
            var shadowiness = (float)((1.0D - distance / 256.0D) * ShadowStrength);
            if (shadowiness > 0.0F)
            {
                RenderShadow(target, pos, shadowiness, tickDelta);
            }
        }

        if (target.IsOnFire)
        {
            RenderOnFire(target, pos, tickDelta);
        }
    }

    public void RenderBoundingBox(Entity target, Vec3D pos, float yaw, float tickDelta)
    {
        if (PresentationOnly || !Dispatcher.Options.ShowDebugInfo) return;

        RenderSystem.LightingEnabled = false;
        RenderSystem.TextureEnabled = false;
        RenderSystem.ModelView.Push();
        RenderSystem.ModelView.Translate((float)pos.X, (float)pos.Y, (float)pos.Z);
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);

        var bb = target.BoundingBox;
        var minX = bb.MinX - target.X;
        var maxX = bb.MaxX - target.X;
        var minY = bb.MinY - target.Y;
        var maxY = bb.MaxY - target.Y;
        var minZ = bb.MinZ - target.Z;
        var maxZ = bb.MaxZ - target.Z;

        var tess = Tessellator.instance;
        tess.startDrawing(1);

        tess.addVertex(minX, minY, minZ);
        tess.addVertex(maxX, minY, minZ);
        tess.addVertex(maxX, minY, minZ);
        tess.addVertex(maxX, minY, maxZ);
        tess.addVertex(maxX, minY, maxZ);
        tess.addVertex(minX, minY, maxZ);
        tess.addVertex(minX, minY, maxZ);
        tess.addVertex(minX, minY, minZ);

        tess.addVertex(minX, maxY, minZ);
        tess.addVertex(maxX, maxY, minZ);
        tess.addVertex(maxX, maxY, minZ);
        tess.addVertex(maxX, maxY, maxZ);
        tess.addVertex(maxX, maxY, maxZ);
        tess.addVertex(minX, maxY, maxZ);
        tess.addVertex(minX, maxY, maxZ);
        tess.addVertex(minX, maxY, minZ);

        tess.addVertex(minX, minY, minZ);
        tess.addVertex(minX, maxY, minZ);
        tess.addVertex(maxX, minY, minZ);
        tess.addVertex(maxX, maxY, minZ);
        tess.addVertex(maxX, minY, maxZ);
        tess.addVertex(maxX, maxY, maxZ);
        tess.addVertex(minX, minY, maxZ);
        tess.addVertex(minX, maxY, maxZ);

        tess.draw(ProgramSlot.Basic);
        tess.startDrawing(1);
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 0, 1.0F);

        tess.addVertex(minX, target.EyeHeight, minZ);
        tess.addVertex(maxX, target.EyeHeight, minZ);
        tess.addVertex(maxX, target.EyeHeight, minZ);
        tess.addVertex(maxX, target.EyeHeight, maxZ);
        tess.addVertex(maxX, target.EyeHeight, maxZ);
        tess.addVertex(minX, target.EyeHeight, maxZ);
        tess.addVertex(minX, target.EyeHeight, maxZ);
        tess.addVertex(minX, target.EyeHeight, minZ);

        tess.draw(ProgramSlot.Line);
        tess.startDrawing(1);
        RenderSystem.Color = new Vector4D<float>(1.0F, 0, 0, 1.0F);

        const float toRad = -MathF.PI / 180.0F;
        yaw *= toRad;
        var pitchCos = MathHelper.Cos(target.Pitch * toRad);

        tess.addVertex(0, target.EyeHeight, 0);
        tess.addVertex(MathHelper.Sin(yaw) * pitchCos, target.EyeHeight + MathHelper.Sin(target.Pitch * toRad), MathHelper.Cos(yaw) * pitchCos);

        tess.draw(ProgramSlot.Line);
        RenderSystem.ModelView.Pop();
        RenderSystem.TextureEnabled = true;
        RenderSystem.LightingEnabled = true;
    }
}
