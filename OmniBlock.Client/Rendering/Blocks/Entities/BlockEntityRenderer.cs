using OmniBlock.Blocks.Entities;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Entities;
using OmniBlock.Worlds.Core;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Entities;

public class BlockEntityRenderer
{
    public static double StaticPlayerX;
    public static double StaticPlayerY;
    public static double StaticPlayerZ;
    private readonly Dictionary<Type, BlockEntitySpecialRenderer?> _specialRendererMap = [];
    private TextRenderer _fontRenderer;

    private BlockEntityRenderer()
    {
        _specialRendererMap.Add(typeof(BlockEntitySign), new BlockEntitySignRenderer());
        _specialRendererMap.Add(typeof(BlockEntityMobSpawner), new BlockEntityMobSpawnerRenderer());
        _specialRendererMap.Add(typeof(BlockEntityPiston), new BlockEntityRendererPiston());

        foreach (var renderer in _specialRendererMap.Values)
        {
            renderer!.setTileEntityRenderer(this);
        }
    }

    public static BlockEntityRenderer Instance { get; } = new();
    public TextureManager TextureManager { get; set; }
    public World World { get; set; }
    public EntityLiving PlayerEntity { get; set; }
    public float PlayerYaw { get; set; }
    public float PlayerPitch { get; set; }
    public double PlayerX { get; set; }
    public double PlayerY { get; set; }
    public double PlayerZ { get; set; }

    public BlockEntitySpecialRenderer? GetSpecialRendererForClass(Type t)
    {
        _specialRendererMap.TryGetValue(t, out var renderer);
        if (renderer == null && t != typeof(BlockEntity))
        {
            renderer = GetSpecialRendererForClass(t.BaseType);
            _specialRendererMap[t] = renderer;
        }

        return renderer;
    }

    public BlockEntitySpecialRenderer? GetSpecialRendererForEntity(BlockEntity? be) => be == null ? null : GetSpecialRendererForClass(be.GetType());

    public void CacheActiveRenderInfo(World world, TextureManager textureManager, TextRenderer fontRenderer, EntityLiving player, float tickDelta)
    {
        if (World != world)
        {
            func_31072_a(world);
        }

        TextureManager = textureManager;
        PlayerEntity = player;
        _fontRenderer = fontRenderer;
        PlayerYaw = player.PrevYaw + (player.Yaw - player.PrevYaw) * tickDelta;
        PlayerPitch = player.PrevPitch + (player.Pitch - player.PrevPitch) * tickDelta;
        PlayerX = player.LastTickX + (player.X - player.LastTickX) * tickDelta;
        PlayerY = player.LastTickY + (player.Y - player.LastTickY) * tickDelta;
        PlayerZ = player.LastTickZ + (player.Z - player.LastTickZ) * tickDelta;
    }

    public void RenderTileEntity(BlockEntity blockEntity, float tickDelta)
    {
        if (blockEntity.distanceFrom(PlayerX, PlayerY, PlayerZ) < 4096.0D)
        {
            var brightness = World.GetLuminance(blockEntity.X, blockEntity.Y, blockEntity.Z);
            RenderSystem.Color = new Vector4D<float>(brightness, brightness, brightness, 1.0F);
            RenderTileEntityAt(blockEntity, blockEntity.X - StaticPlayerX, blockEntity.Y - StaticPlayerY, blockEntity.Z - StaticPlayerZ, tickDelta);
        }
    }

    public void RenderTileEntityAt(BlockEntity blockEntity, double x, double y, double z, float tickDelta)
    {
        var renderer = GetSpecialRendererForEntity(blockEntity);
        renderer?.renderTileEntityAt(blockEntity, x, y, z, tickDelta);
    }

    public void func_31072_a(World world)
    {
        World = world;
        foreach (var renderer in _specialRendererMap.Values)
        {
            renderer?.func_31069_a(world);
        }
    }

    public TextRenderer GetFontRenderer() => _fontRenderer;
}
