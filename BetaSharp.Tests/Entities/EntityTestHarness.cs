using System.Linq;
using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Helpers for entity tests using <see cref="FakeWorldContext"/> (shared with block tests).
/// </summary>
public static class EntityTestHarness
{
    public static double HorizontalSpeed(Entity entity) =>
        Math.Sqrt(entity.VelocityX * entity.VelocityX + entity.VelocityZ * entity.VelocityZ);

    public static int AliveEntityCount(FakeWorldContext world) =>
        world.Entities.Entities.Count(entity => !entity.Dead);

    /// <summary>Fills a horizontal rectangle at <paramref name="floorY"/> with stone so entities have solid ground.</summary>
    public static void PlaceStoneFloor(FakeWorldContext world, int minX, int maxX, int minZ, int maxZ, int floorY)
    {
        int stoneId = BlockRegistry.Get("stone").id;
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                world.Writer.SetBlock(x, floorY, z, stoneId);
            }
        }
    }

    /// <summary>Advances simulated world time and runs the full entity tick pass (same order as the game server).</summary>
    public static void AdvanceGameTicks(FakeWorldContext world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            world.SimulatedWorldTime++;
            world.Entities.TickEntities();
        }
    }

    /// <summary>Creates an entity from the registry, positions it above the floor, and registers it with <see cref="EntityManager"/>.</summary>
    public static Entity CreateSpawned(FakeWorldContext world, EntityType type, double x, double y, double z)
    {
        Entity entity = type.Create(world);
        entity.SetPositionAndAngles(x, y, z, 0f, 0f);
        if (!world.Entities.SpawnEntity(entity))
        {
            throw new InvalidOperationException($"SpawnEntity failed for {type.Id}.");
        }

        return entity;
    }

    /// <summary>Places flat rails along Z at <paramref name="railY"/> (inclusive X range).</summary>
    public static void PlaceRailRunX(FakeWorldContext world, int x0, int x1, int railY, int z)
    {
        int railId = BlockRegistry.Get("rail").id;
        int step = x0 <= x1 ? 1 : -1;
        for (int x = x0; x != x1 + step; x += step)
        {
            world.Writer.SetBlock(x, railY, z, railId);
        }
    }

    /// <summary>Fills an inclusive Y column with stationary water (for squid / fluid tests).</summary>
    public static void FillWaterColumn(FakeWorldContext world, int x, int z, int yMin, int yMax)
    {
        int waterId = BlockRegistry.Get("water").id;
        for (int y = yMin; y <= yMax; y++)
        {
            world.Writer.SetBlock(x, y, z, waterId);
        }
    }

    /// <summary>Builds a short stone wall segment used as a painting backing (single-column Kebab-sized).</summary>
    public static void PlaceStoneWallStrip(FakeWorldContext world, int x, int z, int yMin, int yMax)
    {
        int stoneId = BlockRegistry.Get("stone").id;
        for (int y = yMin; y <= yMax; y++)
        {
            world.Writer.SetBlock(x, y, z, stoneId);
        }
    }

    /// <summary>Whether this entity is a dropped item — the capability check that replaced `is EntityItem`.</summary>
    public static bool IsDroppedItem(Entity entity) => entity.Behaviors.Find<DroppedItemBehavior>() is not null;

    /// <summary>The stack a dropped item carries, or null for anything that is not one.</summary>
    public static ItemStack? DroppedStack(Entity entity) =>
        entity.Behaviors.Find<DroppedItemBehavior>() is { } dropped ? dropped.Stack(entity) : null;

    /// <summary>Creates an entity suitable for NBT save/load (registry items with invalid default state get a safe stack).</summary>
    public static Entity CreateForNbtRoundTrip(EntityType type, FakeWorldContext world)
    {
        if (type == EntityRegistry.ByName("item"))
        {
            return DroppedItemBehavior.Create(world, 8.5, 65.0, 8.5, new ItemStack(Item.ByName("stick"), 1));
        }

        if (type == EntityRegistry.ByName("primedtnt"))
        {
            Entity primed = type.Create(world);
            primed.SetPositionAndAngles(8.5, 66.0, 8.5, 0.0F, 0.0F);
            return primed;
        }

        if (type == EntityRegistry.ByName("painting"))
        {
            return HangingArtBehavior.HangAt(world, 8, 65, 8, 2, "Kebab");
        }

        if (type == EntityRegistry.ByName("fallingsand"))
        {
            Entity sand = type.Create(world);
            sand.Behaviors.Find<SettleAsBlockBehavior>()!.SetBlock(sand, BlockRegistry.Get("sand").id);
            sand.SetPositionAndAngles(8.5, 70.0, 8.5, 0.0F, 0.0F);
            return sand;
        }

        if (type == EntityRegistry.ByName("minecart"))
        {
            return new EntityMinecart(world, 8.5, 65.0, 8.5, 0);
        }

        if (type == EntityRegistry.ByName("boat"))
        {
            return new EntityBoat(world, 8.5, 65.0, 8.5);
        }

        if (type == EntityRegistry.ByName("fishhook"))
        {
            return new EntityFish(world, 8.5, 65.0, 8.5);
        }

        Entity entity = type.Create(world);
        entity.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        return entity;
    }
}
