using BetaSharp.Blocks;
using BetaSharp.Entities.State;
using BetaSharp.Util.Maths;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     A lightning bolt: it strikes fire into the ground, thunders, flashes a few times and is gone.
///     One behavior across the Ticker, Lifecycle and Physics slots because every hook reads the same
///     flash countdown.
///     <para>
///         The strike itself — fire at and around the impact point — runs on the first tick rather
///         than at creation, because at creation the bolt has not been positioned yet. Each re-flash
///         re-rolls the render seed the client draws its jagged path from.
///     </para>
/// </summary>
public sealed class LightningStrikeBehavior : IEntityTicker, IEntityLifecycle, IEntityPhysics
{
    private readonly StateHandle<int> _flashTimer;
    private readonly StateHandle<int> _flashCount;
    private readonly StateHandle<long> _renderSeed;
    private readonly StateHandle<bool> _struck;

    private readonly string _thunderSound;
    private readonly string _explodeSound;
    private readonly int _minimumFireDifficulty;
    private readonly int _extraFires;
    private readonly double _strikeRadius;

    public LightningStrikeBehavior(in EntityBehaviorContext context)
    {
        _thunderSound = context.Json.TryGetProperty("thunder_sound", out System.Text.Json.JsonElement thunder)
            ? thunder.GetString() ?? "ambient.weather.thunder"
            : "ambient.weather.thunder";
        _explodeSound = context.Json.TryGetProperty("explode_sound", out System.Text.Json.JsonElement explode)
            ? explode.GetString() ?? "random.explode"
            : "random.explode";
        _minimumFireDifficulty = context.Int("minimum_fire_difficulty", 2);
        _extraFires = context.Int("extra_fires", 4);
        _strikeRadius = context.Double("strike_radius", 3.0D);

        _flashTimer = context.DeclareInt(2);
        _flashCount = context.DeclareInt();
        _renderSeed = context.DeclareLong();
        _struck = context.DeclareBool();
    }

    /// <summary>Seed the client draws this flash's jagged path from; re-rolled per flash.</summary>
    public long RenderSeed(Entity self) => self.State[_renderSeed];

    public void OnCreated(Entity self)
    {
        self.State[_renderSeed] = self.Random.NextLong();
        self.State[_flashCount] = self.Random.NextInt(3) + 1;
    }

    public bool OnTickEntity(Entity self)
    {
        self.BaseTick();

        if (!self.State[_struck])
        {
            self.State[_struck] = true;
            StrikeFire(self);
        }

        if (self.State[_flashTimer] == 2)
        {
            self.World.Broadcaster.PlaySoundAtPos(self.X, self.Y, self.Z, _thunderSound, 10000.0F, 0.8F + self.Random.NextFloat() * 0.2F);
            self.World.Broadcaster.PlaySoundAtPos(self.X, self.Y, self.Z, _explodeSound, 2.0F, 0.5F + self.Random.NextFloat() * 0.2F);
        }

        --self.State[_flashTimer];
        if (self.State[_flashTimer] < 0)
        {
            if (self.State[_flashCount] == 0)
            {
                self.MarkDead();
            }
            else if (self.State[_flashTimer] < -self.Random.NextInt(10))
            {
                --self.State[_flashCount];
                self.State[_flashTimer] = 1;
                self.State[_renderSeed] = self.Random.NextLong();
                if (self.World.ChunkHost.IsRegionLoaded(MathHelper.Floor(self.X), MathHelper.Floor(self.Y), MathHelper.Floor(self.Z), 10))
                {
                    TryPlaceFire(self.World, MathHelper.Floor(self.X), MathHelper.Floor(self.Y), MathHelper.Floor(self.Z));
                }
            }
        }

        if (self.State[_flashTimer] < 0) return true;

        List<Entity> struck = self.World.Entities.GetEntities(self, new Box(
            self.X - _strikeRadius, self.Y - _strikeRadius, self.Z - _strikeRadius,
            self.X + _strikeRadius, self.Y + 6.0D + _strikeRadius, self.Z + _strikeRadius));

        foreach (Entity entity in struck)
        {
            entity.OnStruckByLightning(self);
        }

        self.World.Environment.LightningTicksLeft = 2;
        return true;
    }

    /// <summary>The impact: fire at the strike point and a few scattered around it.</summary>
    private void StrikeFire(Entity self)
    {
        if (self.World.Difficulty < _minimumFireDifficulty) return;
        if (!self.World.ChunkHost.IsRegionLoaded(MathHelper.Floor(self.X), MathHelper.Floor(self.Y), MathHelper.Floor(self.Z), 10)) return;

        TryPlaceFire(self.World, MathHelper.Floor(self.X), MathHelper.Floor(self.Y), MathHelper.Floor(self.Z));

        for (int i = 0; i < _extraFires; ++i)
        {
            int fireX = MathHelper.Floor(self.X) + self.Random.NextInt(3) - 1;
            int fireY = MathHelper.Floor(self.Y) + self.Random.NextInt(3) - 1;
            int fireZ = MathHelper.Floor(self.Z) + self.Random.NextInt(3) - 1;
            TryPlaceFire(self.World, fireX, fireY, fireZ);
        }
    }

    private static void TryPlaceFire(Worlds.Core.Systems.IWorldContext world, int x, int y, int z)
    {
        if (world.Reader.GetBlockId(x, y, z) == 0 && BlockRegistry.Get("fire").CanPlaceAt(new CanPlaceAtContext(world, 0, x, y, z)))
        {
            world.Writer.SetBlock(x, y, z, BlockRegistry.Get("fire").id);
        }
    }

    /// <summary>Visible only while a flash is on, wherever the camera is.</summary>
    public bool? ShouldRender(Entity self) => self.State[_flashTimer] >= 0;
}
