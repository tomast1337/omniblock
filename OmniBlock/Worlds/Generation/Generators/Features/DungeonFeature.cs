using OmniBlock.Blocks.Entities;
using OmniBlock.Items;
using OmniBlock.Registries;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class DungeonFeature : Feature
{
    public override bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z)
    {
        byte height = 3;
        var radiusX = rand.NextInt(2) + 2;
        var radiusZ = rand.NextInt(2) + 2;
        var openingsCount = 0;


        for (var cx = x - radiusX - 1; cx <= x + radiusX + 1; ++cx)
        {
            for (var cy = y - 1; cy <= y + height + 1; ++cy)
            {
                for (var cz = z - radiusZ - 1; cz <= z + radiusZ + 1; ++cz)
                {
                    var mat = level.Reader.GetMaterial(cx, cy, cz);

                    if ((cy == y - 1 || cy == y + height + 1) && !mat.IsSolid)
                    {
                        return false;
                    }

                    var isWall = cx == x - radiusX - 1 ||
                                 cx == x + radiusX + 1 ||
                                 cz == z - radiusZ - 1 ||
                                 cz == z + radiusZ + 1;

                    if (isWall && cy == y && level.Reader.IsAir(cx, cy, cz) && level.Reader.IsAir(cx, cy + 1, cz))
                    {
                        ++openingsCount;
                    }
                }
            }
        }

        if (openingsCount < 1 || openingsCount > 5)
        {
            return false;
        }

        for (var cx = x - radiusX - 1; cx <= x + radiusX + 1; ++cx)
        {
            for (var cy = y + height; cy >= y - 1; --cy)
            {
                for (var cz = z - radiusZ - 1; cz <= z + radiusZ + 1; ++cz)
                {
                    var isInside = cx != x - radiusX - 1 &&
                                   cy != y - 1 &&
                                   cz != z - radiusZ - 1 &&
                                   cx != x + radiusX + 1 &&
                                   cy != y + height + 1 &&
                                   cz != z + radiusZ + 1;
                    if (isInside)
                    {
                        level.Writer.SetBlock(cx, cy, cz, 0, 0, false);
                    }
                    else if (cy >= 0 && !level.Reader.GetMaterial(cx, cy - 1, cz).IsSolid)
                    {
                        level.Writer.SetBlock(cx, cy, cz, 0, 0, false);
                    }
                    else if (level.Reader.GetMaterial(cx, cy, cz).IsSolid)
                    {
                        if (cy == y - 1 && rand.NextInt(4) != 0)
                        {
                            level.Writer.SetBlock(cx, cy, cz, level.Content.Blocks.Get("mossy_cobblestone").Id, 0, false);
                        }
                        else
                        {
                            level.Writer.SetBlock(cx, cy, cz, level.Content.Blocks.Get("cobblestone").Id, 0, false);
                        }
                    }
                }
            }
        }


        for (var i = 0; i < 2; ++i)
        {
            for (var j = 0; j < 3; ++j)
            {
                var chestX = x + rand.NextInt(radiusX * 2 + 1) - radiusX;
                var chestZ = z + rand.NextInt(radiusZ * 2 + 1) - radiusZ;
                if (level.Reader.IsAir(chestX, y, chestZ))
                {
                    var neighbors = 0;
                    if (level.Reader.GetMaterial(chestX - 1, y, chestZ).IsSolid)
                    {
                        ++neighbors;
                    }

                    if (level.Reader.GetMaterial(chestX + 1, y, chestZ).IsSolid)
                    {
                        ++neighbors;
                    }

                    if (level.Reader.GetMaterial(chestX, y, chestZ - 1).IsSolid)
                    {
                        ++neighbors;
                    }

                    if (level.Reader.GetMaterial(chestX, y, chestZ + 1).IsSolid)
                    {
                        ++neighbors;
                    }

                    if (neighbors != 1)
                    {
                        continue;
                    }

                    level.Writer.SetBlock(chestX, y, chestZ, level.Content.Blocks.Get("chest").Id, 0, true);

                    var chest = level.Entities.GetBlockEntity<BlockEntityChest>(chestX, y, chestZ);
                    for (var k = 0; k < 8; ++k)
                    {
                        var loot = PickCheckLootItem(level.Content.Items, rand);
                        if (loot != null)
                        {
                            chest!.SetStack(rand.NextInt(chest!.Size), loot);
                        }
                    }
                }
            }
        }

        level.Writer.SetBlock(x, y, z, level.Content.Blocks.Get("spawner").Id, 0, true);
        var spawner = level.Entities.GetBlockEntity<BlockEntityMobSpawner>(x, y, z);
        spawner!.SetSpawnedEntityId(PickMobSpawner(rand));
        return true;
    }

    private static ItemStack? PickCheckLootItem(RuntimeItemRegistry items, JavaRandom rand)
    {
        var chance = rand.NextInt(11);

        return chance switch
        {
            0 => new ItemStack(items.Get("omniblock:saddle")),
            1 => new ItemStack(items.Get("omniblock:ingot_iron"), rand.NextInt(4) + 1),
            2 => new ItemStack(items.Get("omniblock:bread")),
            3 => new ItemStack(items.Get("omniblock:wheat"), rand.NextInt(4) + 1),
            4 => new ItemStack(items.Get("omniblock:gunpowder"), rand.NextInt(4) + 1),
            5 => new ItemStack(items.Get("omniblock:string"), rand.NextInt(4) + 1),
            6 => new ItemStack(items.Get("omniblock:bucket")),
            7 => rand.NextInt(100) == 0 ? new ItemStack(items.Get("omniblock:apple_gold")) : null,
            8 => rand.NextInt(2) == 0 ? new ItemStack(items.Get("omniblock:redstone"), rand.NextInt(4) + 1) : null,
            9 => rand.NextInt(10) == 0 ? new ItemStack(items.GetByProtocolId(items.Get("omniblock:record").Id + rand.NextInt(2))) : null,
            10 => new ItemStack(items.Get("omniblock:dye_powder"), 1, 3),
            _ => null
        };
    }

    private static string PickMobSpawner(JavaRandom rand) =>
        rand.NextInt(4) switch
        {
            0 => "Skeleton",
            1 => "Zombie",
            2 => "Zombie",
            3 => "Spider",
            _ => "Zombie"
        };
}
