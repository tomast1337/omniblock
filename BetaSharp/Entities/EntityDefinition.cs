using System.Text.Json;
using System.Text.Json.Serialization;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Entities;

/// <summary>
///     The configuration half of a mob: the values that are genuinely data rather than behavior.
///     A mob's class still owns its AI, state machines, and NBT — see
///     docs/mob-data-driven-migration.md for where that line sits and why.
///     <para>
///         Spawn category is still absent: it is derived from the class hierarchy by
///         <see cref="CreatureKind" /> rather than declared per mob, so a field here would compete
///         with that mechanism rather than describe it.
///     </para>
/// </summary>
public sealed record EntityDefinition : IDataAsset
{
    /// <summary>Applied to any <see cref="EntityLiving" /> constructed without one (e.g. players).</summary>
    public static readonly EntityDefinition Default = new();

    /// <summary>Set by the loader from the JSON filename.</summary>
    [JsonIgnore] public string Name { get; set; } = "";

    [JsonIgnore] public Namespace Namespace { get; set; } = Namespace.BetaSharp;

    /// <summary>
    ///     Wire protocol id. Spawn packets transmit it as a signed byte, so the loader rejects
    ///     anything outside 1..127; <c>-1</c> means "not declared" and fails validation.
    /// </summary>
    public int ProtocolId { get; init; } = -1;

    /// <summary>
    ///     Which natural-spawn budget this mob counts against and spawns from:
    ///     <c>"monster"</c>, <c>"creature"</c>, <c>"water_creature"</c>, or empty for mobs that never
    ///     spawn naturally. Replaces the class-hierarchy sniffing <see cref="CreatureKind" /> used to do.
    /// </summary>
    public string SpawnCategory { get; init; } = "";

    public int Health { get; init; } = 10;
    public float MovementSpeed { get; init; } = 0.7F;

    /// <summary>Melee damage. Only consulted for <see cref="EntityCreature" /> and below.</summary>
    public int AttackStrength { get; init; } = 2;

    public float Width { get; init; } = 0.6F;
    public float Height { get; init; } = 1.8F;

    public string Texture { get; init; } = "/mob/char.png";

    public string? LivingSound { get; init; }
    public string? HurtSound { get; init; } = "random.hurt";
    public string? DeathSound { get; init; } = "random.hurt";
    public float SoundVolume { get; init; } = 1.0F;

    /// <summary>Ticks between idle-sound rolls; animals are quieter than monsters.</summary>
    public int TalkInterval { get; init; } = 80;

    public bool FireImmune { get; init; }
    public int MaxSpawnedInChunk { get; init; } = 4;
    public bool CanDespawn { get; init; } = true;

    /// <summary>
    ///     Network-synchronised per-entity state, declared here rather than in behavior code so the
    ///     wire ids are visible data. See <see cref="State.SyncedPropertyDefinition" /> — these ids
    ///     are protocol facts shared with the client.
    /// </summary>
    public State.SyncedPropertyDefinition[] SyncedProperties { get; init; } = [];

    /// <summary>
    ///     How the client draws this entity: a <c>"Type"</c> naming a renderer factory plus whatever
    ///     that factory reads (<c>"Model"</c>, <c>"Shadow"</c>). Kept as raw JSON for the same reason
    ///     <see cref="Behaviors" /> is — the shape belongs to the factory, not to this record.
    ///     <para>
    ///         Absent means the client falls back to its by-class renderer table, so an entity is not
    ///         obliged to describe its rendering here to keep working.
    ///     </para>
    /// </summary>
    public JsonElement? Renderer { get; init; }

    /// <summary>
    ///     One entry per behavior <em>instance</em>, not per slot — same shape as
    ///     <c>BlockDefinition.Behaviors</c>. Each entry carries a <c>"Slots"</c> array
    ///     (<c>"Attack"</c>, <c>"Targeting"</c>, <c>"Loot"</c>, <c>"Lifecycle"</c>) and its own
    ///     <c>"Type"</c>, the <see cref="Behaviors.EntityBehaviorRegistry" /> key.
    /// </summary>
    public List<JsonElement> Behaviors { get; init; } = [];
}
