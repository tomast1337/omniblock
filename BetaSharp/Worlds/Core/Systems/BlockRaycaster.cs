using BetaSharp.Blocks;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;

namespace BetaSharp.Worlds.Core.Systems;

/// <summary>
///     Walks a ray through the block grid, stopping at the first block whose shape it actually hits.
///     <para>
///         Extracted from <see cref="WorldReader" /> so anything holding an
///         <see cref="IBlockReader" /> can raycast with the real algorithm rather than an
///         approximation of it — the traversal never needed a world, only block lookups.
///     </para>
/// </summary>
public static class BlockRaycaster
{
    public static HitResult Cast(IBlockReader reader, EntityManager entities, Vec3D start, Vec3D end, bool includeFluids, bool ignoreNonSolid)
    {
        if (double.IsNaN(start.x) || double.IsNaN(start.y) || double.IsNaN(start.z) ||
            double.IsNaN(end.x) || double.IsNaN(end.y) || double.IsNaN(end.z))
        {
            return new HitResult(HitResultType.Miss);
        }

        int targetX = MathHelper.Floor(end.x);
        int targetY = MathHelper.Floor(end.y);
        int targetZ = MathHelper.Floor(end.z);
        int currentX = MathHelper.Floor(start.x);
        int currentY = MathHelper.Floor(start.y);
        int currentZ = MathHelper.Floor(start.z);

        int initialId = reader.GetBlockId(currentX, currentY, currentZ);
        int initialMeta = reader.GetBlockMeta(currentX, currentY, currentZ);
        Block? initialBlock = Block.Blocks[initialId];

        if ((!ignoreNonSolid || initialBlock == null ||
             initialBlock.GetCollisionShape(reader, entities, currentX, currentY, currentZ) != null) &&
            initialId > 0 && initialBlock!.HasCollision(initialMeta, includeFluids))
        {
            HitResult result = initialBlock.Raycast(reader, entities, currentX, currentY, currentZ, start, end);
            if (result.Type != HitResultType.Miss)
            {
                return result;
            }
        }

        int iterationsRemaining = 200;
        while (iterationsRemaining-- >= 0)
        {
            if (double.IsNaN(start.x) || double.IsNaN(start.y) || double.IsNaN(start.z) || currentX == targetX && currentY == targetY && currentZ == targetZ)
            {
                return new HitResult(HitResultType.Miss);
            }

            bool canMoveX = true, canMoveY = true, canMoveZ = true;
            double nextBoundaryX = 999.0D, nextBoundaryY = 999.0D, nextBoundaryZ = 999.0D;

            if (targetX > currentX)
            {
                nextBoundaryX = currentX + 1.0D;
            }
            else if (targetX < currentX)
            {
                nextBoundaryX = currentX + 0.0D;
            }
            else
            {
                canMoveX = false;
            }

            if (targetY > currentY)
            {
                nextBoundaryY = currentY + 1.0D;
            }
            else if (targetY < currentY)
            {
                nextBoundaryY = currentY + 0.0D;
            }
            else
            {
                canMoveY = false;
            }

            if (targetZ > currentZ)
            {
                nextBoundaryZ = currentZ + 1.0D;
            }
            else if (targetZ < currentZ)
            {
                nextBoundaryZ = currentZ + 0.0D;
            }
            else
            {
                canMoveZ = false;
            }

            double deltaX = end.x - start.x;
            double deltaY = end.y - start.y;
            double deltaZ = end.z - start.z;

            double scaleX = 999.0D, scaleY = 999.0D, scaleZ = 999.0D;
            if (canMoveX)
            {
                scaleX = (nextBoundaryX - start.x) / deltaX;
            }

            if (canMoveY)
            {
                scaleY = (nextBoundaryY - start.y) / deltaY;
            }

            if (canMoveZ)
            {
                scaleZ = (nextBoundaryZ - start.z) / deltaZ;
            }

            byte hitSide;
            if (scaleX < scaleY && scaleX < scaleZ)
            {
                hitSide = (byte)(targetX > currentX ? 4 : 5);
                start.x = nextBoundaryX;
                start.y += deltaY * scaleX;
                start.z += deltaZ * scaleX;
            }
            else if (scaleY < scaleZ)
            {
                hitSide = (byte)(targetY > currentY ? 0 : 1);
                start.x += deltaX * scaleY;
                start.y = nextBoundaryY;
                start.z += deltaZ * scaleY;
            }
            else
            {
                hitSide = (byte)(targetZ > currentZ ? 2 : 3);
                start.x += deltaX * scaleZ;
                start.y += deltaY * scaleZ;
                start.z = nextBoundaryZ;
            }

            Vec3D currentStepPos = new(start.x, start.y, start.z);
            currentX = (int)(currentStepPos.x = MathHelper.Floor(start.x));
            if (hitSide == 5)
            {
                currentX--;
                currentStepPos.x++;
            }

            currentY = (int)(currentStepPos.y = MathHelper.Floor(start.y));
            if (hitSide == 1)
            {
                currentY--;
                currentStepPos.y++;
            }

            currentZ = (int)(currentStepPos.z = MathHelper.Floor(start.z));
            if (hitSide == 3)
            {
                currentZ--;
                currentStepPos.z++;
            }

            int blockIdAtStep = reader.GetBlockId(currentX, currentY, currentZ);
            int metaAtStep = reader.GetBlockMeta(currentX, currentY, currentZ);
            Block? blockAtStep = Block.Blocks[blockIdAtStep];

            if ((!ignoreNonSolid || blockAtStep == null ||
                 blockAtStep.GetCollisionShape(reader, entities, currentX, currentY, currentZ) != null) &&
                blockIdAtStep > 0 && blockAtStep!.HasCollision(metaAtStep, includeFluids))
            {
                HitResult hit = blockAtStep.Raycast(reader, entities, currentX, currentY, currentZ, start, end);
                if (hit.Type != HitResultType.Miss)
                {
                    return hit;
                }
            }
        }

        return new HitResult(HitResultType.Miss);
    }
}
