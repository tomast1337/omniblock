using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     The slime chunk rule: spawns only deep underground, rarely, and only in chunks a fixed seed
///     marks out. The chunk's draw is seeded from its coordinates, so the same chunk answers the same
///     way every time.
///     <para>
///         Deliberately checks nothing about where the body fits: this replaces the placement rule
///         rather than adding to it.
///     </para>
/// </summary>
public sealed class SlimeChunkSpawnBehavior(long chunkSeed, int chanceOneIn, int chunkChanceOneIn, double maxHeight, int difficultyFreeSize) : IEntityPhysics
{
    public bool? CanSpawn(EntityLiving self)
    {
        int size = self.Synced<byte>("size")?.Value ?? 1;
        if (size != difficultyFreeSize && self.World.Difficulty <= 0)
        {
            return false;
        }

        if (self.Random.NextInt(chanceOneIn) != 0)
        {
            return false;
        }

        Chunk chunk = self.World.ChunkHost.GetChunkFromPos(MathHelper.Floor(self.X), MathHelper.Floor(self.Z));
        return chunk.GetSlimeRandom(chunkSeed).NextInt(chunkChanceOneIn) == 0 && self.Y < maxHeight;
    }
}
