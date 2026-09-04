using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock;

/// <summary>
///     A natural-spawn budget. <paramref name="Category" /> matches
///     <see cref="EntityDefinition.SpawnCategory" />, so which mobs belong to a kind is declared in
///     <c>assets/entity/*.json</c> rather than inferred from the class hierarchy.
/// </summary>
public sealed record CreatureKind(string Category, int MobCap, Material SpawnMaterial, bool Peaceful)
{
    public const string MonsterCategory = "monster";
    public const string CreatureCategory = "creature";
    public const string WaterCreatureCategory = "water_creature";

    public static readonly CreatureKind Monster = new(MonsterCategory, 70, Material.Air, false);
    public static readonly CreatureKind Creature = new(CreatureCategory, 15, Material.Air, true);
    public static readonly CreatureKind WaterCreature = new(WaterCreatureCategory, 5, Material.Water, true);

    public static readonly CreatureKind[] Values = [Monster, Creature, WaterCreature];

    public bool CanSpawnAtLocation(IBlockReader world, int x, int y, int z)
    {
        if (SpawnMaterial == Material.Water)
        {
            return world.GetMaterial(x, y, z).IsFluid && !world.ShouldSuffocate(x, y + 1, z);
        }

        return world.ShouldSuffocate(x, y - 1, z) && !world.ShouldSuffocate(x, y, z) &&
               !world.GetMaterial(x, y, z).IsFluid && !world.ShouldSuffocate(x, y + 1, z);
    }
}
