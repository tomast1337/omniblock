using BetaSharp.Blocks;
using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.Textures;
using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Client.Rendering.Items;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Registries;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core;

namespace BetaSharp.Client.Rendering.Entities;

public class EntityRenderDispatcher
{
    private readonly Dictionary<Type, EntityRenderer> _entityRenderMap = [];
    private readonly Dictionary<EntityType, EntityRenderer> _declaredRenderMap = [];
    public static readonly EntityRenderDispatcher Instance = new();
    private TextRenderer _fontRenderer;
    public static double OffsetX { get; set; }
    public static double OffsetY { get; set; }
    public static double OffsetZ { get; set; }
    public TextureManager TextureManager { get; private set; }
    public SkinManager SkinManager { get; set; }
    public HeldItemRenderer HeldItemRenderer { get; set; }
    public World World { get; set; }
    public EntityLiving CameraEntity { get; private set; }
    public float PlayerViewY { get; set; }
    public float PlayerViewX { get; private set; }
    public GameOptions Options { get; private set; }
    private double _x;
    private double _y;
    private double _z;

    private EntityRenderDispatcher()
    {
        RegisterRenderer(typeof(EntityPlayer), new PlayerEntityRenderer());
        RegisterRenderer(typeof(EntityLiving), new LivingEntityRenderer(new ModelBiped(), 0.5F));
        RegisterRenderer(typeof(Entity), new BoxEntityRenderer());

        RegisterDeclaredRenderers();

        foreach (EntityRenderer render in _entityRenderMap.Values.Concat(_declaredRenderMap.Values))
        {
            render.Dispatcher = this;
        }
    }

    /// <summary>
    ///     Builds a renderer for every registered type whose definition declares one. These take
    ///     precedence over the by-class table, which is what lets several types share a class: a cow
    ///     and a sheep are both an <c>EntityCreature</c>, so the class cannot choose the model.
    /// </summary>
    private void RegisterDeclaredRenderers()
    {
        foreach (EntityType type in DefaultRegistries.EntityTypes)
        {
            if (type.Definition?.Renderer is not { } json) continue;

            _declaredRenderMap[type] = EntityRendererRegistry.Create(json);
        }
    }

    private void RegisterRenderer(Type type, EntityRenderer render)
    {
        _entityRenderMap[type] = render;
    }

    public EntityRenderer GetEntityClassRenderObject(Type type)
    {
        if (!_entityRenderMap.TryGetValue(type, out EntityRenderer? entityRenderer) && type != typeof(Entity))
        {
            entityRenderer = GetEntityClassRenderObject(type.BaseType);
            RegisterRenderer(type, entityRenderer);
        }

        return entityRenderer;
    }

    public EntityRenderer GetEntityRenderObject(Entity entity)
    {
        if (entity.Type is { } type && _declaredRenderMap.TryGetValue(type, out EntityRenderer? declared))
        {
            return declared;
        }

        return GetEntityClassRenderObject(entity.GetType());
    }

    public void CacheRenderInfo(World world, TextureManager textureManager, TextRenderer textRenderer, EntityLiving camera, GameOptions options, float tickDelta)
    {
        World = world;
        TextureManager = textureManager;
        Options = options;
        CameraEntity = camera;
        _fontRenderer = textRenderer;
        if (camera.IsSleeping)
        {
            int blockId = world.Reader.GetBlockId(MathHelper.Floor(camera.X), MathHelper.Floor(camera.Y), MathHelper.Floor(camera.Z));
            if (blockId == BlockRegistry.Get("bed").id)
            {
                int bedMeta = world.Reader.GetBlockMeta(MathHelper.Floor(camera.X), MathHelper.Floor(camera.Y), MathHelper.Floor(camera.Z));
                int bedFacing = bedMeta & 3;
                PlayerViewY = bedFacing * 90 + 180;
                PlayerViewX = 0.0F;
            }
        }
        else
        {
            PlayerViewY = camera.PrevYaw + (camera.Yaw - camera.PrevYaw) * tickDelta;
            PlayerViewX = camera.PrevPitch + (camera.Pitch - camera.PrevPitch) * tickDelta;
        }

        _x = camera.LastTickX + (camera.X - camera.LastTickX) * (double)tickDelta;
        _y = camera.LastTickY + (camera.Y - camera.LastTickY) * (double)tickDelta;
        _z = camera.LastTickZ + (camera.Z - camera.LastTickZ) * (double)tickDelta;
    }

    public void RenderEntity(Entity target, float tickDelta)
    {
        double x = target.LastTickX + (target.X - target.LastTickX) * (double)tickDelta;
        double y = target.LastTickY + (target.Y - target.LastTickY) * (double)tickDelta;
        double z = target.LastTickZ + (target.Z - target.LastTickZ) * (double)tickDelta;
        float yaw = target.PrevYaw + (target.Yaw - target.PrevYaw) * tickDelta;
        float brightness = target.GetBrightnessAtEyes(tickDelta);
        GLManager.GL.Color3(brightness, brightness, brightness);
        RenderEntityWithPosYaw(target, x - OffsetX, y - OffsetY, z - OffsetZ, yaw, tickDelta);
    }

    public void RenderEntityWithPosYaw(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        EntityRenderer entityRenderer = GetEntityRenderObject(target);
        if (entityRenderer == null) return;

        entityRenderer.Render(target, x, y, z, yaw, tickDelta);
        entityRenderer.PostRender(target, new Vec3D(x, y, z), yaw, tickDelta);
        entityRenderer.RenderBoundingBox(target, new Vec3D(x, y, z), yaw, tickDelta);
    }

    public double GetSquareDistanceTo(double x, double y, double z)
    {
        double xDelta = x - _x;
        double yDelta = y - _y;
        double zDelta = z - _z;
        return xDelta * xDelta + yDelta * yDelta + zDelta * zDelta;
    }

    public TextRenderer getTextRenderer()
    {
        return _fontRenderer;
    }
}
