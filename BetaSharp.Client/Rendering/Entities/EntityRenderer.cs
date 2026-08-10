using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Rendering.Entities;

public abstract class EntityRenderer
{
    public EntityRenderDispatcher Dispatcher { get; set; } = null!;
    protected float ShadowRadius = 0.0F;
    protected float ShadowStrength = 1.0F;

    protected World World => Dispatcher.World;
    public TextRenderer TextRenderer => Dispatcher.getTextRenderer();

    public abstract void Render(Entity target, double x, double y, double z, float yaw, float tickDelta);

    protected void loadTexture(string path)
    {
        TextureManager? textureManager = Dispatcher.TextureManager;
        if (textureManager == null) return;

        TextureHandle handle = textureManager.GetTextureId(path);

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
            TextureHandle? skinHandle = Dispatcher.SkinManager?.GetTextureHandle(url);
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
        GLManager.LightingEnabled = false;

        int textureId = BlockRegistry.Get("fire").TextureId;
        int texX = (textureId & 15) << 4;
        int texY = textureId & 240;

        float minU;
        float maxU;
        float minV;
        float maxV;

        GLManager.ModelView.Push();
        GLManager.ModelView.Translate((float)pos.X, (float)pos.Y, (float)pos.Z);

        float scale = ent.Width * 1.4F;
        GLManager.ModelView.Scale(scale, scale, scale);

        loadTexture("/terrain.png");
        Tessellator tess = Tessellator.instance;

        float widthOffset = 0.5F;
        float depthOffset = 0.0F;
        float heightRatio = ent.Height / scale;
        float yOffset = (float)(ent.Y - ent.BoundingBox.MinY);

        GLManager.ModelView.Rotate(-Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
        GLManager.ModelView.Translate(0.0F, 0.0F, -0.3F + (int)heightRatio * 0.02F);
        GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);

        float zOffset = 0.0F;
        int pass = 0;

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
        GLManager.ModelView.Pop();
        GLManager.LightingEnabled = true;
    }

    private void RenderShadow(Entity target, Vec3D pos, float shadowiness, float tickDelta)
    {
        // Blended and depth tested but not depth writing, and unculled like the models it sits under:
        // several shadows can overlap on the ground without the first one drawn hiding the rest.
        GLManager.State.Apply(RenderState.Entity with { Blend = BlendMode.Alpha, DepthWrite = false });

        TextureManager textureManager = Dispatcher.TextureManager;
        textureManager.BindTexture(textureManager.GetTextureId("%clamp%/misc/shadow.png"));

        float radius = ShadowRadius;

        double targetX = target.LastTickX + (target.X - target.LastTickX) * tickDelta;
        double targetY = target.LastTickY + (target.Y - target.LastTickY) * tickDelta + target.GetShadowRadius();
        double targetZ = target.LastTickZ + (target.Z - target.LastTickZ) * tickDelta;

        int minX = MathHelper.Floor(targetX - radius);
        int maxX = MathHelper.Floor(targetX + radius);
        int minY = MathHelper.Floor(targetY - radius);
        int maxY = MathHelper.Floor(targetY);
        int minZ = MathHelper.Floor(targetZ - radius);
        int maxZ = MathHelper.Floor(targetZ + radius);

        double dx = pos.X - targetX;
        double dy = pos.Y - targetY;
        double dz = pos.Z - targetZ;

        Tessellator tess = Tessellator.instance;
        tess.startDrawingQuads();

        for (int blockX = minX; blockX <= maxX; ++blockX)
        {
            for (int blockY = minY; blockY <= maxY; ++blockY)
            {
                for (int blockZ = minZ; blockZ <= maxZ; ++blockZ)
                {
                    int blockId = World.Reader.GetBlockId(blockX, blockY - 1, blockZ);
                    if (blockId > 0 && World.Lighting.GetLightLevel(blockX, blockY, blockZ) > 3)
                    {
                        renderShadowOnBlock(
                            Block.Blocks[blockId],
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
        GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);
        GLManager.State.Apply(RenderState.Entity);
    }

    private void renderShadowOnBlock(Block block, Vec3D pos, int blockX, int blockY, int blockZ, float shadowiness, float radius, Vec3D offset)
    {
        if (!block.IsFullCube()) return;

        double shadowDarkness = (shadowiness - (pos.Y - (blockY + offset.Y)) / 2.0D) * 0.5D * World.GetLuminance(blockX, blockY, blockZ);

        if (shadowDarkness < 0.0D) return;

        if (shadowDarkness > 1.0D)
            shadowDarkness = 1.0D;

        Tessellator tess = Tessellator.instance;
        tess.setColorRGBA_F(1.0F, 1.0F, 1.0F, (float)shadowDarkness);

        double minX = blockX + block.BoundingBox.MinX + offset.X;
        double maxX = blockX + block.BoundingBox.MaxX + offset.X;
        double minY = blockY + block.BoundingBox.MinY + offset.Y + 1.0D / 64.0D;
        double minZ = blockZ + block.BoundingBox.MinZ + offset.Z;
        double maxZ = blockZ + block.BoundingBox.MaxZ + offset.Z;

        float minU = (float)((pos.X - minX) / 2.0D / (double)radius + 0.5D);
        float maxU = (float)((pos.X - maxX) / 2.0D / (double)radius + 0.5D);
        float minV = (float)((pos.Z - minZ) / 2.0D / (double)radius + 0.5D);
        float maxV = (float)((pos.Z - maxZ) / 2.0D / (double)radius + 0.5D);

        tess.addVertexWithUV(minX, minY, minZ, (double)minU, (double)minV);
        tess.addVertexWithUV(minX, minY, maxZ, (double)minU, (double)maxV);
        tess.addVertexWithUV(maxX, minY, maxZ, (double)maxU, (double)maxV);
        tess.addVertexWithUV(maxX, minY, minZ, (double)maxU, (double)minV);
    }

    public static void renderShape(Box aabb, Vec3D pos)
    {
        GLManager.TextureEnabled = false;
        Tessellator tess = Tessellator.instance;
        GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);

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
        GLManager.TextureEnabled = true;
    }

    public static void renderShapeFlat(Box aabb)
    {
        Tessellator tess = Tessellator.instance;
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
        if (ShadowRadius > 0.0F)
        {
            double distance = Dispatcher.GetSquareDistanceTo(target.X, target.Y, target.Z);
            float shadowiness = (float)((1.0D - distance / 256.0D) * ShadowStrength);
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
        if (!Dispatcher.Options.ShowDebugInfo) return;

        GLManager.LightingEnabled = false;
        GLManager.TextureEnabled = false;
        GLManager.ModelView.Push();
        GLManager.ModelView.Translate((float)pos.X, (float)pos.Y, (float)pos.Z);
        GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);

        Box bb = target.BoundingBox;
        double minX = bb.MinX - target.X;
        double maxX = bb.MaxX - target.X;
        double minY = bb.MinY - target.Y;
        double maxY = bb.MaxY - target.Y;
        double minZ = bb.MinZ - target.Z;
        double maxZ = bb.MaxZ - target.Z;

        Tessellator tess = Tessellator.instance;
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
        GLManager.Color = new(1.0F, 1.0F, 0, 1.0F);

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
        GLManager.Color = new(1.0F, 0, 0, 1.0F);

        const float toRad = -MathF.PI / 180.0F;
        yaw *= toRad;
        float pitchCos = MathHelper.Cos(target.Pitch * toRad);

        tess.addVertex(0, target.EyeHeight, 0);
        tess.addVertex(MathHelper.Sin(yaw) * pitchCos, target.EyeHeight + MathHelper.Sin(target.Pitch * toRad), MathHelper.Cos(yaw) * pitchCos);

        tess.draw(ProgramSlot.Line);
        GLManager.ModelView.Pop();
        GLManager.TextureEnabled = true;
        GLManager.LightingEnabled = true;
    }
}
