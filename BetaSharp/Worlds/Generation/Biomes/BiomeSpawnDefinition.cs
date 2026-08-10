using OmniBlock.Registries.Data;

namespace OmniBlock.Worlds.Generation.Biomes;

/// <summary>One weighted mob in a biome's spawn list. <c>Entity</c> is a registry path, e.g. <c>"omniblock:wolf"</c>.</summary>
public sealed record BiomeSpawnEntry(string Entity, int Weight);

/// <summary>
///     A biome's natural-spawn lists, loaded from <c>assets/biome_spawn/{biome}.json</c>.
///     <para>
///         Each biome states its lists in full rather than inheriting a shared default: vanilla
///         biomes both replace the base list (Sky, Hell) and extend it (Forest, Taiga adding wolves),
///         and a defaults-merge cannot express both without inventing array-merge semantics.
///     </para>
/// </summary>
public sealed class BiomeSpawnDefinition : DataAsset
{
    public BiomeSpawnEntry[] Monsters { get; init; } = [];
    public BiomeSpawnEntry[] Creatures { get; init; } = [];
    public BiomeSpawnEntry[] WaterCreatures { get; init; } = [];
}
