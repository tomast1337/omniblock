using BetaSharp.Blocks.Entities;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Worlds.Core.Systems;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Jukebox: right-click ejects the current record (insertion happens from <c>ItemRecord</c>),
/// metadata 1 marks "record loaded", and breaking ejects before the tile entity is removed.
/// Assign to the Interactable and Lifecycle slots.
/// </summary>
public sealed class JukeboxBehavior : IBlockInteractable, IBlockLifecycle
{
    private const float DropSpread = 0.7F;
    private static readonly ILogger<JukeboxBehavior> s_logger = BetaSharp.Log.Instance.For<JukeboxBehavior>();

    public bool OnUse(Block block, OnUseEvent @event)
    {
        if (@event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z) == 0) return false;

        tryEjectRecord(@event.World, @event.X, @event.Y, @event.Z);
        return true;
    }

    public static void insertRecord(IWorldContext world, int x, int y, int z, int id)
    {
        if (world.IsRemote) return;

        BlockEntityRecordPlayer? jukebox = world.Entities.GetBlockEntity<BlockEntityRecordPlayer>(x, y, z);
        if (jukebox == null)
        {
            s_logger.LogWarning("Jukebox at {x}, {y}, {z} is missing a block entity", x, y, z);
            return;
        }

        jukebox.recordId = id;
        jukebox.MarkDirty();
        world.Writer.SetBlockMeta(x, y, z, 1);
    }

    public static void tryEjectRecord(IWorldContext level, int x, int y, int z)
    {
        if (level.IsRemote) return;

        BlockEntityRecordPlayer? jukebox = level.Entities.GetBlockEntity<BlockEntityRecordPlayer>(x, y, z);
        int recordId = jukebox?.recordId ?? 0;
        if (recordId == 0) return;

        level.Broadcaster.WorldEvent(1005, x, y, z, 0);
        level.Broadcaster.PlayStreamingAtPos(null, x, y, z);
        jukebox!.recordId = 0;
        jukebox.MarkDirty();
        level.Writer.SetBlockMeta(x, y, z, 0);

        double offsetX = Random.Shared.NextSingle() * DropSpread + (1.0F - DropSpread) * 0.5D;
        double offsetY = Random.Shared.NextSingle() * DropSpread + (1.0F - DropSpread) * 0.2D + 0.6D;
        double offsetZ = Random.Shared.NextSingle() * DropSpread + (1.0F - DropSpread) * 0.5D;
        EntityItem entityItem = new(level, x + offsetX, y + offsetY, z + offsetZ, new ItemStack(recordId, 1, 0))
        {
            DelayBeforeCanPickup = 10
        };
        level.SpawnEntity(entityItem);
    }

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (block.getBlockEntity() is { } blockEntity)
        {
            @event.World.Entities.SetBlockEntity(@event.X, @event.Y, @event.Z, blockEntity);
        }
    }

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        tryEjectRecord(@event.World, @event.X, @event.Y, @event.Z);
        @event.World.Entities.RemoveBlockEntity(@event.X, @event.Y, @event.Z);
    }
}
