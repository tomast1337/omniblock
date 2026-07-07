using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Mushroom: darkness-gated survival/spread. Self-contained (not composed with
/// <see cref="PlantSurvivalBehavior"/>) since its <see cref="CanGrow"/> requires a light check
/// beyond plain ground validity, and — with no subclass left to shadow it — the capability hook
/// is now the single source of truth for both placement and the neighbor-update break check.
/// </summary>
internal sealed class MushroomBehavior : IBlockTicker, IBlockPhysics
{
    // ── IBlockTicker ──────────────────────────────────────────────

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (Random.Shared.Next(100) != 0) return;

        int tryX = @event.X + Random.Shared.Next(3) - 1;
        int tryY = @event.Y + Random.Shared.Next(2) - Random.Shared.Next(2);
        int tryZ = @event.Z + Random.Shared.Next(3) - 1;

        OnTickEvent tryEvent = new(@event.World, tryX, tryY, tryZ, @event.World.Reader.GetBlockMeta(tryX, tryY, tryZ), @event.World.Reader.GetBlockId(tryX, tryY, tryZ));
        if (!@event.World.Reader.IsAir(tryX, tryY, tryZ) || !CanGrow(block, tryEvent))
        {
            return;
        }

        @event.World.Writer.SetBlock(tryX, tryY, tryZ, block.id);
    }

    // ── IBlockPhysics ─────────────────────────────────────────────

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
        => CanPlantOnTop(@event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z));

    public bool CanGrow(Block block, OnTickEvent ctx)
        => ctx.Y >= 0 && ctx.Y < ChuckFormat.WorldHeight
           && ctx.World.Reader.GetBrightness(ctx.X, ctx.Y, ctx.Z) < 13
           && CanPlantOnTop(ctx.World.Reader.GetBlockId(ctx.X, ctx.Y - 1, ctx.Z));

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (CanGrow(block, @event)) return;

        block.dropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    private static bool CanPlantOnTop(int id)
        => id == Block.GrassBlock.id || id == Block.Dirt.id || id == Block.Stone.id || id == Block.Gravel.id || id == Block.Cobblestone.id;
}
