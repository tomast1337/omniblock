using System.Text.Json;
using System.Text.Json.Serialization;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Entities.State;
using OmniBlock.Registries.Data;

namespace OmniBlock.Entities;

/// <summary>
///     The data half of an entity: fixed values loaded from <c>assets/entity/*.json</c>. Anything
///     that varies per individual belongs in <see cref="State.EntityState" />; anything that is
///     conditional logic belongs in a capability slot (see <see cref="Behaviors" />).
/// </summary>
public sealed record EntityDefinition : IDataAsset
{
    /// <summary>Applied to any <see cref="EntityLiving" /> constructed without one (e.g. players).</summary>
    public static readonly EntityDefinition Default = new();

    /// <summary>
    ///     Wire protocol id. Spawn packets transmit it as a signed byte, so the loader rejects
    ///     anything outside 1..127; <c>-1</c> means "not declared" and fails validation.
    /// </summary>
    public int ProtocolId { get; init; } = -1;

    /// <summary>
    ///     Which natural-spawn budget this mob counts against and spawns from:
    ///     <c>"monster"</c>, <c>"creature"</c>, <c>"water_creature"</c>, or empty for mobs that never
    ///     spawn naturally. Parsed into <see cref="CreatureKind" />.
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
    ///     Uniform size multiplier applied to the bounding box and eye height. A multiplier rather
    ///     than authored dimensions because the giant's box is <c>3.6000001</c> by <c>10.799999</c>:
    ///     float artifacts of this multiplication that authored values would not reproduce.
    /// </summary>
    public float Scale { get; init; } = 1.0F;

    /// <summary>
    ///     Vertical offset applied to where a passenger sits, on top of the default three-quarter
    ///     height. A spider carries its skeleton rider lower than its back.
    /// </summary>
    public double PassengerRideOffset { get; init; }

    /// <summary>
    ///     Whether moving accumulates walk distance and plays footstep sounds. False for a spider,
    ///     which makes no sound as it walks.
    /// </summary>
    public bool MakesStepSounds { get; init; } = true;

    public string Texture { get; init; } = "/mob/char.png";

    /// <summary>
    ///     Item this mob is drawn holding (<c>"omniblock:bow"</c>), or <c>null</c> for empty-handed.
    ///     Fixed per type: no vanilla mob changes what it carries.
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
    ///     Whether the mob's air supply is untouched by being submerged. True for a squid.
    /// </summary>
    public bool BreathesUnderwater { get; init; }

    /// <summary>
    ///     Whether the server keeps the client's copy of this mob's velocity up to date. Needed when
    ///     the motion is imposed by the server rather than produced by client-side AI: a squid drifts
    ///     on a server-picked velocity, and would otherwise sit still between position updates.
    /// </summary>
    public bool TracksVelocity { get; init; }

    /// <summary>
    ///     Whether a player's wolves refuse to be set on this mob. True for creepers and ghasts.
    /// </summary>
    public bool WolfPackIgnores { get; init; }

    public int MaxSpawnedInChunk { get; init; } = 4;
    public bool CanDespawn { get; init; } = true;

    /// <summary>Whether other entities are blocked from spawning inside this one's box.</summary>
    public bool PreventEntitySpawning { get; init; }

    /// <summary>Whether other entities collide with this one's box while it is alive.</summary>
    public bool Collidable { get; init; }

    /// <summary>
    ///     Multiplier on the distance past which the client stops drawing this entity. A thrown
    ///     projectile's box is tiny, so it quadruples the default range to stay visible in flight.
    /// </summary>
    public double RenderDistanceWeight { get; init; } = 1.0;

    /// <summary>
    ///     Extra reach a player's swing gets when aiming at this entity. A fireball uses a full
    ///     block's margin, which is what makes deflecting one feasible.
    /// </summary>
    public float TargetingMargin { get; init; } = 0.1F;

    /// <summary>
    ///     Whether a synced position from the server is nudged up out of anything it landed inside.
    ///     False for an arrow, which must stay exactly where the server buried it.
    /// </summary>
    public bool PositionSyncAvoidsEntities { get; init; } = true;

    /// <summary>
    ///     Whether the tracker sends rotation with every movement update instead of only when the
    ///     entity has visibly turned. True for an arrow, whose angle changes on every step of its arc.
    /// </summary>
    public bool AlwaysSyncsRotation { get; init; }

    /// <summary>
    ///     Whether the client draws this entity without testing it against the view frustum. True
    ///     for a fishing bobber, whose line is drawn even when the float is off-screen.
    /// </summary>
    public bool IgnoreFrustumCheck { get; init; }

    /// <summary>
    ///     Fraction of this entity's height a passenger sits at, before
    ///     <see cref="PassengerRideOffset" /> is added. Three-quarters up for anything you sit on
    ///     the back of; zero for a vehicle you sit down inside.
    /// </summary>
    public double PassengerRideHeightScale { get; init; } = 0.75D;

    /// <summary>Whether other entities can shove this one. True for a boat.</summary>
    public bool Pushable { get; init; }

    /// <summary>
    ///     Whether others physically collide with this entity's box instead of passing through it.
    ///     True for a boat's hull.
    /// </summary>
    public bool SolidCollisionShape { get; init; }

    /// <summary>
    ///     Wire id in the object-spawn packet (<c>50</c> for primed TNT). A second id space from
    ///     <see cref="ProtocolId" />: non-living entities spawn on the client through
    ///     <c>EntitySpawnS2CPacket</c>, not the living-entity packet. <c>0</c> means unused.
    /// </summary>
    public int SpawnObjectId { get; init; }

    /// <summary>
    ///     How far away players are sent this entity, in blocks. <c>0</c> means the server tracker
    ///     falls back to its by-kind defaults.
    /// </summary>
    public int TrackingRange { get; init; }

    /// <summary>Ticks between tracker position updates, read only when <see cref="TrackingRange" /> is set.</summary>
    public int TrackingFrequency { get; init; } = 3;

    /// <summary>
    ///     Wire id in the global-entity spawn packet (<c>1</c> for a lightning bolt). A third id
    ///     space, for effects broadcast to everyone instead of tracked per player. <c>0</c> means
    ///     unused.
    /// </summary>
    public int GlobalSpawnId { get; init; }

    /// <summary>
    ///     Network-synchronised per-entity state. Declared here so the wire ids stay visible data;
    ///     they are protocol facts shared with the client. See
    ///     <see cref="State.SyncedPropertyDefinition" />.
    /// </summary>
    public SyncedPropertyDefinition[] SyncedProperties { get; init; } = [];

    /// <summary>
    ///     How the client draws this entity: a <c>"Type"</c> naming a renderer factory plus whatever
    ///     that factory reads (<c>"Model"</c>, <c>"Shadow"</c>). Raw JSON because the shape belongs to
    ///     the factory, not to this record. Absent means the client falls back to its by-class
    ///     renderer table.
    /// </summary>
    public JsonElement? Renderer { get; init; }

    /// <summary>
    ///     One entry per behavior <em>instance</em>, not per slot, matching
    ///     <c>BlockDefinition.Behaviors</c>. Each entry carries a <c>"Slots"</c> array
    ///     (<c>"Attack"</c>, <c>"Targeting"</c>, <c>"Loot"</c>, <c>"Lifecycle"</c>) and a
    ///     <c>"Type"</c> naming the definition class to deserialize into.
    /// </summary>
    public List<EntityBehaviorDefinition> Behaviors { get; init; } = [];

    /// <summary>Set by the loader from the JSON filename.</summary>
    [JsonIgnore]
    public string Name { get; set; } = "";

    [JsonIgnore] public Namespace Namespace { get; set; } = Namespace.OmniBlock;
}
