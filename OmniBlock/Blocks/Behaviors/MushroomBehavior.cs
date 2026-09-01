using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Mushroom: darkness-gated survival/spread.
///     <para>
///         Valid growth substrate set is a required, (see <c>BehaviorRegistry</c>'s <c>"mushroom"</c> entry).
///     </para>
///     <para>
///         Spread chance (<paramref name="spreadChanceOneIn" />, 1-in-N per tick) and maximum
///         brightness it can survive at (<paramref name="maxBrightness" />).
///     </para>
/// </summary>
internal sealed class MushroomBehavior(Block[] validGround, int spreadChanceOneIn, int maxBrightness) : IBlockTicker, IBlockPhysics
{
    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
    {
        return CanPlantOnTop(@event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z));
    }

    public bool CanGrow(Block block, OnTickEvent ctx)
    {
        return ctx.Y >= 0 && ctx.Y < ChuckFormat.WorldHeight
                          && ctx.World.Reader.GetBrightness(ctx.X, ctx.Y, ctx.Z) < maxBrightness
                          && CanPlantOnTop(ctx.World.Reader.GetBlockId(ctx.X, ctx.Y - 1, ctx.Z));
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (CanGrow(block, @event)) return;

        block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (Random.Shared.Next(spreadChanceOneIn) != 0) return;

        var tryX = @event.X + Random.Shared.Next(3) - 1;
        var tryY = @event.Y + Random.Shared.Next(2) - Random.Shared.Next(2);
        var tryZ = @event.Z + Random.Shared.Next(3) - 1;

        OnTickEvent tryEvent = new(@event.World, tryX, tryY, tryZ, @event.World.Reader.GetBlockMeta(tryX, tryY, tryZ), @event.World.Reader.GetBlockId(tryX, tryY, tryZ));
        if (!@event.World.Reader.IsAir(tryX, tryY, tryZ) || !CanGrow(block, tryEvent)) return;

        @event.World.Writer.SetBlock(tryX, tryY, tryZ, block.Id);
    }

    private bool CanPlantOnTop(int id)
    {
        foreach (var ground in validGround)
            if (id == ground.Id)
                return true;

        return false;
    }
}