using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Fence: rests on solid ground or another fence post, and always collides as a single raised
/// box (1.5 blocks tall) regardless of which sides visually connect — the connection-dependent
/// bar rendering lives entirely client-side in <c>FenceRenderer</c>.
/// </summary>
internal sealed class FenceBehavior : IBlockPhysics
{
    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
        => @event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z) == block.id
        || @event.World.Reader.GetMaterial(@event.X, @event.Y - 1, @event.Z).IsSolid;

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
        => new Box(x, y, z, x + 1, y + 1.5F, z + 1);
}
