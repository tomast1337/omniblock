namespace OmniBlock.Worlds.Core.Systems;

/// <summary>
///     Primitive-only world access for tick effects. Wraps <see cref="IWorldContext" /> but never
///     exposes it — a future Luau binding surfaces these methods as global <c>Host.*</c> functions,
///     never this struct itself, keeping the FFI boundary IDs-only per CLAUDE.md.
/// </summary>
public readonly struct TickHost(IWorldContext world)
{
    public int GetBlockId(int x, int y, int z) => world.Reader.GetBlockId(x, y, z);

    public int GetBlockMeta(int x, int y, int z) => world.Reader.GetBlockMeta(x, y, z);

    public void SetBlock(int x, int y, int z, int blockId) => world.Writer.SetBlock(x, y, z, blockId);

    public void SetBlockMeta(int x, int y, int z, int meta) => world.Writer.SetBlockMeta(x, y, z, meta);

    public void ScheduleTick(int x, int y, int z, int blockId, int tickRate) =>
        world.TickScheduler.ScheduleBlockUpdate(x, y, z, blockId, tickRate);
}
