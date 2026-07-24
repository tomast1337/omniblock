using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Soul sand: sits 2/16 short of a full block, and drags entities standing on it.
///     <para>
///         Drag speed factor (<paramref name="speedFactor" />) is a required.
///     </para>
/// </summary>
internal sealed class SoulSandBehavior(double speedFactor) : IBlockPhysics, IBlockInteractable
{
    private const float Height = 2.0F / 16.0F;

    public void OnEntityCollision(Block block, OnEntityCollisionEvent @event)
    {
        @event.Entity.VelocityX *= speedFactor;
        @event.Entity.VelocityZ *= speedFactor;
    }

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
        => new Box(x, y, z, x + 1, y + 1 - Height, z + 1);
}
