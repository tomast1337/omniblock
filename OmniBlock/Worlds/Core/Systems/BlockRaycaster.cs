using OmniBlock.Blocks;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;

namespace OmniBlock.Worlds.Core.Systems;

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
        if (double.IsNaN(start.X) || double.IsNaN(start.Y) || double.IsNaN(start.Z) ||
            double.IsNaN(end.X) || double.IsNaN(end.Y) || double.IsNaN(end.Z))
        {
            return new HitResult(HitResultType.Miss);
        }

        int targetX = MathHelper.Floor(end.X);
        int targetY = MathHelper.Floor(end.Y);
        int targetZ = MathHelper.Floor(end.Z);
        int currentX = MathHelper.Floor(start.X);
        int currentY = MathHelper.Floor(start.Y);
        int currentZ = MathHelper.Floor(start.Z);

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
            if (double.IsNaN(start.X) || double.IsNaN(start.Y) || double.IsNaN(start.Z) || currentX == targetX && currentY == targetY && currentZ == targetZ)
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

            double deltaX = end.X - start.X;
            double deltaY = end.Y - start.Y;
            double deltaZ = end.Z - start.Z;

            double scaleX = 999.0D, scaleY = 999.0D, scaleZ = 999.0D;
            if (canMoveX)
            {
                scaleX = (nextBoundaryX - start.X) / deltaX;
            }

            if (canMoveY)
            {
                scaleY = (nextBoundaryY - start.Y) / deltaY;
            }

            if (canMoveZ)
            {
                scaleZ = (nextBoundaryZ - start.Z) / deltaZ;
            }

            byte hitSide;
            if (scaleX < scaleY && scaleX < scaleZ)
            {
                hitSide = (byte)(targetX > currentX ? 4 : 5);
                start.X = nextBoundaryX;
                start.Y += deltaY * scaleX;
                start.Z += deltaZ * scaleX;
            }
            else if (scaleY < scaleZ)
            {
                hitSide = (byte)(targetY > currentY ? 0 : 1);
                start.X += deltaX * scaleY;
                start.Y = nextBoundaryY;
                start.Z += deltaZ * scaleY;
            }
            else
            {
                hitSide = (byte)(targetZ > currentZ ? 2 : 3);
                start.X += deltaX * scaleZ;
                start.Y += deltaY * scaleZ;
                start.Z = nextBoundaryZ;
            }

            Vec3D currentStepPos = new(start.X, start.Y, start.Z);
            currentX = (int)(currentStepPos.X = MathHelper.Floor(start.X));
            if (hitSide == 5)
            {
                currentX--;
                currentStepPos.X++;
            }

            currentY = (int)(currentStepPos.Y = MathHelper.Floor(start.Y));
            if (hitSide == 1)
            {
                currentY--;
                currentStepPos.Y++;
            }

            currentZ = (int)(currentStepPos.Z = MathHelper.Floor(start.Z));
            if (hitSide == 3)
            {
                currentZ--;
                currentStepPos.Z++;
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
