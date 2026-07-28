using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Chunks;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Spawns only deep underground, rarely, and only in chunks a fixed seed marks out — the slime
///     chunk rule. The chunk's own draw is what makes it a property of the place rather than of the
///     attempt: the same chunk answers the same way every time.
///     <para>
///         Nothing about where the body fits is checked, which is deliberate: this replaces the
///         placement rule rather than adding to it, exactly as the class override did.
///     </para>
/// </summary>
public sealed class SlimeChunkSpawnBehavior(long chunkSeed, int chanceOneIn, int chunkChanceOneIn, double maxHeight, int difficultyFreeSize) : IEntityPhysics
{
    public bool? CanSpawn(EntityLiving self)
    {
        int size = self.Synced<byte>("size")?.Value ?? 1;
        if (size != difficultyFreeSize && self.World.Difficulty <= 0) return false;
        if (self.Random.NextInt(chanceOneIn) != 0) return false;

        Chunk chunk = self.World.ChunkHost.GetChunkFromPos(MathHelper.Floor(self.X), MathHelper.Floor(self.Z));
        return chunk.GetSlimeRandom(chunkSeed).NextInt(chunkChanceOneIn) == 0 && self.Y < maxHeight;
    }
}
