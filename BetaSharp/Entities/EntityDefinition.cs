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

    /// <summary>Eye height as a fraction of the mob's own height; a wolf's sits lower than most.</summary>
    public float EyeHeightScale { get; init; } = 0.85F;

    /// <summary>
    ///     Uniform size multiplier applied to the bounding box and eye height. Kept as a multiplier
    ///     rather than authored dimensions because the giant's real box is <c>3.6000001</c> by
    ///     <c>10.799999</c> — float artifacts of this multiplication that authoring would not
    ///     reproduce.
    /// </summary>
    public float Scale { get; init; } = 1.0F;

    /// <summary>
    ///     Vertical offset applied to where a passenger sits, on top of the default three-quarter
    ///     height. A spider carries its skeleton rider lower than its back.
    /// </summary>
    public double PassengerRideOffset { get; init; }

    /// <summary>
    ///     Whether moving accumulates walk distance and plays footstep sounds. False for mobs that
    ///     move without treading — a spider makes no sound as it walks.
    /// </summary>
    public bool MakesStepSounds { get; init; } = true;

    public string Texture { get; init; } = "/mob/char.png";

    /// <summary>
    ///     Item this mob is drawn holding (<c>"betasharp:bow"</c>), or <c>null</c> for empty-handed.
    ///     Fixed per type — no vanilla mob changes what it carries — so it is configuration rather
    ///     than a capability slot.
    /// </summary>
    public string? HeldItem { get; init; }

    public string? LivingSound { get; init; }
    public string? HurtSound { get; init; } = "random.hurt";
    public string? DeathSound { get; init; } = "random.hurt";
    public float SoundVolume { get; init; } = 1.0F;

    /// <summary>Ticks between idle-sound rolls; animals are quieter than monsters.</summary>
    public int TalkInterval { get; init; } = 80;

    public bool FireImmune { get; init; }

    /// <summary>
    ///     Whether the mob's air supply is untouched by being submerged. True for a squid, which
    ///     lives there. Configuration rather than a slot: no mob starts or stops being able to.
    /// </summary>
    public bool BreathesUnderwater { get; init; }

    /// <summary>
    ///     Whether the server keeps the client's copy of this mob's velocity up to date. Needed by a
    ///     mob whose motion is imposed rather than produced by client-side AI — the squid drifts on
    ///     a velocity the server picks, and would otherwise sit still until its next position update.
    /// </summary>
    public bool TracksVelocity { get; init; }

    /// <summary>
    ///     Whether a player's wolves refuse to be set on this mob — true for creepers and ghasts,
    ///     which a wolf pack would only make worse. Declared rather than sniffed from the class.
    /// </summary>
    public bool WolfPackIgnores { get; init; }
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
