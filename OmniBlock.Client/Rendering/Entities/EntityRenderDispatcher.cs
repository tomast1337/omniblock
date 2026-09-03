using OmniBlock.Blocks;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Client.Rendering.Items;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Registries;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Client.Rendering.Entities;

public class EntityRenderDispatcher
{
    private static readonly ILogger s_logger = Log.Instance.For<EntityRenderDispatcher>();

    /// <summary>Renderer types already reported as unported, so the same one is not logged every frame.</summary>
    private readonly HashSet<Type> _reportedUnported = [];

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
        foreach (ResourceLocation key in ContentRuntime.Current.EntityTypes.Keys)
        {
            EntityType type = ContentRuntime.Current.EntityTypes.Get(key);
            if (type.Definition?.Renderer is not { } json) continue;
            try
            {
                _declaredRenderMap[type] = EntityRendererRegistry.Create(json);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    $"Entity '{key}' references an invalid renderer: {error.Message}", error);
            }
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
            if (blockId == BlockRegistry.Get("bed").Id)
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
        GLManager.Color = new(brightness, brightness, brightness, 1.0F);
        RenderEntityWithPosYaw(target, x - OffsetX, y - OffsetY, z - OffsetZ, yaw, tickDelta);
    }

    public void RenderEntityWithPosYaw(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        EntityRenderer entityRenderer = GetEntityRenderObject(target);
        if (entityRenderer == null) return;

        // A renderer not yet ported to the active backend throws rather than silently drawing
        // garbage. One bad entity is not allowed to take the whole frame down with it — the world
        // pass this runs inside still has terrain, sky and clouds queued behind it, and none of that
        // reaches the screen if the pass is aborted here. Reported once per renderer type rather than
        // swallowed outright, so the gap stays visible without drowning every other log line.
        try
        {
            entityRenderer.Render(target, x, y, z, yaw, tickDelta);
            entityRenderer.PostRender(target, new Vec3D(x, y, z), yaw, tickDelta);
            entityRenderer.RenderBoundingBox(target, new Vec3D(x, y, z), yaw, tickDelta);
        }
        catch (InvalidOperationException ex)
        {
            if (_reportedUnported.Add(entityRenderer.GetType()))
            {
                s_logger.LogWarning(ex, "Skipping {Renderer} for the rest of this session: {Message}",
                    entityRenderer.GetType().Name, ex.Message);
            }
        }
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
