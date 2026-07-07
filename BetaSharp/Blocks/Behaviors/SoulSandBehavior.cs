using BetaSharp.Entities;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>Soul sand: sits 2/16 short of a full block, and drags entities standing on it.</summary>
internal sealed class SoulSandBehavior : IBlockPhysics, IBlockInteractable
{
    private const float Height = 2.0F / 16.0F;

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
        => new Box(x, y, z, x + 1, y + 1 - Height, z + 1);

    public void OnEntityCollision(Block block, OnEntityCollisionEvent @event)
    {
        @event.Entity.VelocityX *= 0.4;
        @event.Entity.VelocityZ *= 0.4;
    }
}
