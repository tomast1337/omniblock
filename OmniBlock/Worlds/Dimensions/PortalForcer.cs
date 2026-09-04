using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Worlds.Dimensions;

internal class PortalForcer
{
    public void MoveToPortal(World world, Entity entity)
    {
        if (!TeleportToValidPortal(world, entity))
        {
            CreatePortal(world, entity);
            TeleportToValidPortal(world, entity);
        }
    }

    public static bool TeleportToValidPortal(World world, Entity entity)
    {
        var portalId = world.Content.Blocks.Get("omniblock:nether_portal").Id;
        short searchRadius = 128;
        var closestDistance = -1.0D;
        var foundX = 0;
        var foundY = 0;
        var foundZ = 0;

        var entityX = MathHelper.Floor(entity.X);
        var entityZ = MathHelper.Floor(entity.Z);

        // An existing portal wins outright; only build one if the search comes back empty.
        for (var x = entityX - searchRadius; x <= entityX + searchRadius; ++x)
        {
            var dx = x + 0.5D - entity.X;

            for (var z = entityZ - searchRadius; z <= entityZ + searchRadius; ++z)
            {
                var dz = z + 0.5D - entity.Z;

                for (var y = 127; y >= 0; --y)
                {
                    if (world.Reader.GetBlockId(x, y, z) == portalId)
                    {
                        // Walk down to the bottom obsidian block of the portal frame
                        while (world.Reader.GetBlockId(x, y - 1, z) == portalId)
                        {
                            --y;
                        }

                        var dy = y + 0.5D - entity.Y;
                        var distanceSq = dx * dx + dy * dy + dz * dz;

                        if (closestDistance < 0.0D || distanceSq < closestDistance)
                        {
                            closestDistance = distanceSq;
                            foundX = x;
                            foundY = y;
                            foundZ = z;
                        }
                    }
                }
            }
        }

        if (closestDistance >= 0.0D)
        {
            var targetX = foundX + 0.5D;
            var targetY = foundY + 0.5D;
            var targetZ = foundZ + 0.5D;

            // Offset the player so they don't spawn inside the obsidian frame
            if (world.Reader.GetBlockId(foundX - 1, foundY, foundZ) == portalId)
            {
                targetX -= 0.5D;
            }

            if (world.Reader.GetBlockId(foundX + 1, foundY, foundZ) == portalId)
            {
                targetX += 0.5D;
            }

            if (world.Reader.GetBlockId(foundX, foundY, foundZ - 1) == portalId)
            {
                targetZ -= 0.5D;
            }

            if (world.Reader.GetBlockId(foundX, foundY, foundZ + 1) == portalId)
            {
                targetZ += 0.5D;
            }

            entity.SetPositionAndAnglesKeepPrevAngles(targetX, targetY, targetZ, entity.Yaw, 0.0F);
            entity.VelocityX = entity.VelocityY = entity.VelocityZ = 0.0D;
            return true;
        }

        return false;
    }

    public static bool CreatePortal(World world, Entity entity)
    {
        var obsidianId = world.Content.Blocks.Get("omniblock:obsidian").Id;
        var portalId = world.Content.Blocks.Get("omniblock:nether_portal").Id;
        byte searchRadius = 16;
        var closestDistance = -1.0D;

        var entityX = MathHelper.Floor(entity.X);
        var entityY = MathHelper.Floor(entity.Y);
        var entityZ = MathHelper.Floor(entity.Z);

        var bestX = entityX;
        var bestY = entityY;
        var bestZ = entityZ;
        var bestDirection = 0;

        var randomDirection = Random.Shared.Next(4);
        var h1 = ChuckFormat.WorldHeight - 1;

        // First choice: a flat 3x4 area of solid ground.
        for (var x = entityX - searchRadius; x <= entityX + searchRadius; ++x)
        {
            var dx = x + 0.5D - entity.X;

            for (var z = entityZ - searchRadius; z <= entityZ + searchRadius; ++z)
            {
                var dz = z + 0.5D - entity.Z;

                for (var y = h1; y >= 0; --y)
                {
                    if (world.Reader.IsAir(x, y, z))
                    {
                        while (y > 0 && world.Reader.IsAir(x, y - 1, z))
                        {
                            --y;
                        }

                        for (var dirOffset = randomDirection; dirOffset < randomDirection + 4; ++dirOffset)
                        {
                            var dirX = dirOffset % 2;
                            var dirZ = 1 - dirX;
                            if (dirOffset % 4 >= 2)
                            {
                                dirX = -dirX;
                                dirZ = -dirZ;
                            }

                            var validLocation = true;
                            for (var width = 0; width < 3 && validLocation; ++width)
                            {
                                for (var widthDepth = 0; widthDepth < 4 && validLocation; ++widthDepth)
                                {
                                    for (var height = -1; height < 4 && validLocation; ++height)
                                    {
                                        var checkX = x + (widthDepth - 1) * dirX + width * dirZ;
                                        var checkY = y + height;
                                        var checkZ = z + (widthDepth - 1) * dirZ - width * dirX;

                                        if ((height < 0 && !world.Reader.GetMaterial(checkX, checkY, checkZ).IsSolid) || (height >= 0 && !world.Reader.IsAir(checkX, checkY, checkZ)))
                                        {
                                            validLocation = false;
                                        }
                                    }
                                }
                            }

                            if (validLocation)
                            {
                                var dy = y + 0.5D - entity.Y;
                                var distanceSq = dx * dx + dy * dy + dz * dz;
                                if (closestDistance < 0.0D || distanceSq < closestDistance)
                                {
                                    closestDistance = distanceSq;
                                    bestX = x;
                                    bestY = y;
                                    bestZ = z;
                                    bestDirection = dirOffset % 4;
                                }
                            }
                        }
                    }
                }
            }
        }

        // Nothing flat enough, so settle for a tighter 1x4 area.
        if (closestDistance < 0.0D)
        {
            for (var x = entityX - searchRadius; x <= entityX + searchRadius; ++x)
            {
                var dx = x + 0.5D - entity.X;

                for (var z = entityZ - searchRadius; z <= entityZ + searchRadius; ++z)
                {
                    var dz = z + 0.5D - entity.Z;

                    for (var y = h1; y >= 0; --y)
                    {
                        if (world.Reader.IsAir(x, y, z))
                        {
                            while (world.Reader.IsAir(x, y - 1, z))
                            {
                                --y;
                            }

                            for (var dirOffset = randomDirection; dirOffset < randomDirection + 2; ++dirOffset)
                            {
                                var dirX = dirOffset % 2;
                                var dirZ = 1 - dirX;

                                var validLocation = true;
                                for (var widthDepth = 0; widthDepth < 4 && validLocation; ++widthDepth)
                                {
                                    for (var height = -1; height < 4 && validLocation; ++height)
                                    {
                                        var checkX = x + (widthDepth - 1) * dirX;
                                        var checkY = y + height;
                                        var checkZ = z + (widthDepth - 1) * dirZ;

                                        if ((height < 0 && !world.Reader.GetMaterial(checkX, checkY, checkZ).IsSolid) || (height >= 0 && !world.Reader.IsAir(checkX, checkY, checkZ)))
                                        {
                                            validLocation = false;
                                        }
                                    }
                                }

                                if (validLocation)
                                {
                                    var dy = y + 0.5D - entity.Y;
                                    var distanceSq = dx * dx + dy * dy + dz * dz;
                                    if (closestDistance < 0.0D || distanceSq < closestDistance)
                                    {
                                        closestDistance = distanceSq;
                                        bestX = x;
                                        bestY = y;
                                        bestZ = z;
                                        bestDirection = dirOffset % 2;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Nowhere suitable at all, so build where the entity is and force the terrain to accept it.
        var finalX = bestX;
        var finalY = bestY;
        var finalZ = bestZ;

        var finalDirX = bestDirection % 2;
        var finalDirZ = 1 - finalDirX;

        if (bestDirection % 4 >= 2)
        {
            finalDirX = -finalDirX;
            finalDirZ = -finalDirZ;
        }

        // If no valid spot was found, carve one out in the sky/ground.
        if (closestDistance < 0.0D)
        {
            finalY = Math.Clamp(finalY, 70, 118);

            for (var w = -1; w <= 1; ++w)
            {
                for (var wDepth = 1; wDepth < 3; ++wDepth)
                {
                    for (var h = -1; h < 3; ++h)
                    {
                        var buildX = finalX + (wDepth - 1) * finalDirX + w * finalDirZ;
                        var buildY = finalY + h;
                        var buildZ = finalZ + (wDepth - 1) * finalDirZ - w * finalDirX;

                        var isFloor = h < 0;
                        world.Writer.SetBlock(buildX, buildY, buildZ, isFloor ? obsidianId : 0);
                    }
                }
            }
        }

        // Frame first, then the portal blocks inside it.
        for (var pass = 0; pass < 4; ++pass)
        {
            for (var wDepth = 0; wDepth < 4; ++wDepth)
            {
                for (var h = -1; h < 4; ++h)
                {
                    var buildX = finalX + (wDepth - 1) * finalDirX;
                    var buildY = finalY + h;
                    var buildZ = finalZ + (wDepth - 1) * finalDirZ;

                    var isFrameEdge = wDepth == 0 || wDepth == 3 || h == -1 || h == 3;
                    world.Writer.SetBlockInternal(buildX, buildY, buildZ, isFrameEdge ? obsidianId : portalId);
                }
            }

            // Block updates (lighting, neighbor checks)
            for (var wDepth = 0; wDepth < 4; ++wDepth)
            {
                for (var h = -1; h < 4; ++h)
                {
                    var buildX = finalX + (wDepth - 1) * finalDirX;
                    var buildY = finalY + h;
                    var buildZ = finalZ + (wDepth - 1) * finalDirZ;

                    world.Broadcaster.NotifyNeighbors(buildX, buildY, buildZ, world.Reader.GetBlockId(buildX, buildY, buildZ));
                }
            }
        }

        return true;
    }
}
