using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Biomes.Source;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using Silk.NET.Maths;

namespace OmniBlock.Worlds.Dimensions;

public abstract class Dimension
{
    private readonly float[] _backgroundColor = new float[4];
    public bool EvaporatesWater = false;
    public bool HasCeiling = false;
    public int Id = 0;
    public bool IsNether = false;

    public float[] LightLevelToLuminance = new float[16];
    public IWorldContext World { get; set; } = null!;
    public BiomeSource BiomeSource { get; set; } = null!;

    public virtual float CloudHeight { get; } = 108.0F;
    public virtual bool HasGround => true;
    public virtual bool HasWorldSpawn => true;

    public void SetWorld(IWorldContext world)
    {
        World = world;
        InitBiomeSource();
        InitBrightnessTable();
    }

    protected virtual void InitBrightnessTable()
    {
        var offset = 0.05F;

        for (var i = 0; i <= 15; ++i)
        {
            var factor = 1.0F - i / 15.0F;
            LightLevelToLuminance[i] = (1.0F - factor) / (factor * 3.0F + 1.0F) * (1.0F - offset) + offset;
        }
    }

    public virtual void InitBiomeSource() => BiomeSource = new BiomeSource(World);

    public abstract IChunkSource CreateChunkGenerator();

    public virtual bool IsValidSpawnPoint(int x, int z)
    {
        var y = World.Reader.GetTopY(x, z);
        var topBlockId = World.Reader.GetBlockId(x, y, z);

        return topBlockId != 0
               && World.Content.Blocks.TryGetByProtocolId(topBlockId, out var block)
               && block.Material.BlocksMovement;
    }

    public virtual float GetTimeOfDay(long time, float tickDelta)
    {
        var ticks = (int)(time % 24000L);
        var phase = (ticks + tickDelta) / 24000.0F - 0.25F;

        if (phase < 0.0F)
        {
            phase++;
        }

        if (phase > 1.0F)
        {
            phase--;
        }

        var phaseCopy = phase;

        phase = 1.0F - (float)((Math.Cos(phase * Math.PI) + 1.0D) / 2.0D);
        phase = phaseCopy + (phase - phaseCopy) / 3.0F;

        return phase;
    }

    public virtual float[]? GetBackgroundColor(float celestialAngle, float partialTicks)
    {
        var offset = 0.4F;
        var cosAngle = MathHelper.Cos(celestialAngle * (float)Math.PI * 2.0F);

        if (cosAngle is >= -0.4F and <= 0.4F)
        {
            var fade = cosAngle / offset * 0.5F + 0.5F;
            var multiplier = 1.0F - (1.0F - MathHelper.Sin(fade * (float)Math.PI)) * 0.99F;
            multiplier *= multiplier;

            _backgroundColor[0] = fade * 0.3F + 0.7F;
            _backgroundColor[1] = fade * fade * 0.7F + 0.2F;
            _backgroundColor[2] = fade * fade * 0.0F + 0.2F;
            _backgroundColor[3] = multiplier;

            return _backgroundColor;
        }

        return null;
    }

    public virtual Vector3D<double> GetFogColor(float celestialAngle, float partialTicks)
    {
        var cosAngle = MathHelper.Cos(celestialAngle * (float)Math.PI * 2.0F) * 2.0F + 0.5F;

        cosAngle = Math.Clamp(cosAngle, 0.0F, 1.0F);

        var r = 192.0F / 255.0F;
        var g = 216.0F / 255.0F;
        var b = 1.0F;

        r *= cosAngle * 0.94F + 0.06F;
        g *= cosAngle * 0.94F + 0.06F;
        b *= cosAngle * 0.91F + 0.09F;

        return new Vector3D<double>(r, g, b);
    }

    public static Dimension FromId(int id) => id switch
    {
        -1 => new NetherDimension(),
        0 => new OverworldDimension(),
        _ => throw new ArgumentOutOfRangeException($"Invalid Dimension:{id}")
    };
}
