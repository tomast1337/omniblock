namespace BetaSharp.Entities;

/// <summary>
///     Every mob's configuration in one place. Phase 2 of the mob data-driven migration keeps these
///     as static C# records; Phase 3 moves them behind a registry and Phase 4 loads them from JSON,
///     at which point this class goes away.
/// </summary>
internal static class MobDefinitions
{
    private const string HurtSound = "random.hurt";

    public static readonly EntityDefinition Zombie = new()
    {
        Health = 20,
        MovementSpeed = 0.5F,
        AttackStrength = 5,
        Texture = "/mob/zombie.png",
        LivingSound = "mob.zombie",
        HurtSound = "mob.zombiehurt",
        DeathSound = "mob.zombiedeath"
    };

    /// <summary>
    ///     Dimensions are deliberately absent: the giant scales the inherited box by six at
    ///     construction, and the resulting floats (3.6000001 / 10.799999) are artifacts of that
    ///     multiplication rather than authored values.
    /// </summary>
    public static readonly EntityDefinition Giant = new()
    {
        Health = 200,
        MovementSpeed = 0.5F,
        AttackStrength = 50,
        Texture = "/mob/zombie.png",
        HurtSound = HurtSound,
        DeathSound = HurtSound
    };

    public static readonly EntityDefinition PigZombie = new()
    {
        Health = 20,
        MovementSpeed = 0.5F,
        AttackStrength = 5,
        Texture = "/mob/pigzombie.png",
        LivingSound = "mob.zombiepig.zpig",
        HurtSound = "mob.zombiepig.zpighurt",
        DeathSound = "mob.zombiepig.zpigdeath",
        FireImmune = true
    };

    public static readonly EntityDefinition Skeleton = new()
    {
        Health = 20,
        Texture = "/mob/skeleton.png",
        LivingSound = "mob.skeleton",
        HurtSound = "mob.skeletonhurt",
        DeathSound = "mob.skeletonhurt"
    };

    public static readonly EntityDefinition Creeper = new()
    {
        Health = 20,
        Texture = "/mob/creeper.png",
        HurtSound = "mob.creeper",
        DeathSound = "mob.creeperdeath"
    };

    public static readonly EntityDefinition Spider = new()
    {
        Health = 20,
        MovementSpeed = 0.8F,
        Width = 1.4F,
        Height = 0.9F,
        Texture = "/mob/spider.png",
        LivingSound = "mob.spider",
        HurtSound = "mob.spider",
        DeathSound = "mob.spiderdeath"
    };

    /// <summary>Health and dimensions are omitted — a slime derives both from its randomly chosen size.</summary>
    public static readonly EntityDefinition Slime = new()
    {
        Texture = "/mob/slime.png",
        HurtSound = "mob.slime",
        DeathSound = "mob.slime",
        SoundVolume = 0.6F
    };

    public static readonly EntityDefinition Ghast = new()
    {
        Width = 4.0F,
        Height = 4.0F,
        Texture = "/mob/ghast.png",
        LivingSound = "mob.ghast.moan",
        HurtSound = "mob.ghast.scream",
        DeathSound = "mob.ghast.death",
        SoundVolume = 10.0F,
        FireImmune = true,
        MaxSpawnedInChunk = 1
    };

    public static readonly EntityDefinition Pig = new()
    {
        Width = 0.9F,
        Height = 0.9F,
        Texture = "/mob/pig.png",
        LivingSound = "mob.pig",
        HurtSound = "mob.pig",
        DeathSound = "mob.pigdeath",
        TalkInterval = AnimalTalkInterval
    };

    public static readonly EntityDefinition Cow = new()
    {
        Width = 0.9F,
        Height = 1.3F,
        Texture = "/mob/cow.png",
        LivingSound = "mob.cow",
        HurtSound = "mob.cowhurt",
        DeathSound = "mob.cowhurt",
        SoundVolume = 0.4F,
        TalkInterval = AnimalTalkInterval
    };

    public static readonly EntityDefinition Sheep = new()
    {
        Width = 0.9F,
        Height = 1.3F,
        Texture = "/mob/sheep.png",
        LivingSound = "mob.sheep",
        HurtSound = "mob.sheep",
        DeathSound = "mob.sheep",
        TalkInterval = AnimalTalkInterval
    };

    public static readonly EntityDefinition Chicken = new()
    {
        Health = 4,
        Width = 0.3F,
        Height = 0.4F,
        Texture = "/mob/chicken.png",
        LivingSound = "mob.chicken",
        HurtSound = "mob.chickenhurt",
        DeathSound = "mob.chickenhurt",
        TalkInterval = AnimalTalkInterval
    };

    public static readonly EntityDefinition Squid = new()
    {
        Width = 0.95F,
        Height = 0.95F,
        Texture = "/mob/squid.png",
        HurtSound = null,
        DeathSound = null,
        SoundVolume = 0.4F,
        TalkInterval = AnimalTalkInterval
    };

    /// <summary>Living sound and despawn eligibility are omitted — both read the wolf's tamed/angry state.</summary>
    public static readonly EntityDefinition Wolf = new()
    {
        Health = 8,
        MovementSpeed = 1.1F,
        Width = 0.8F,
        Height = 0.8F,
        Texture = "/mob/wolf.png",
        HurtSound = "mob.wolf.hurt",
        DeathSound = "mob.wolf.death",
        SoundVolume = 0.4F,
        MaxSpawnedInChunk = 8,
        TalkInterval = AnimalTalkInterval
    };

    private const int AnimalTalkInterval = 120;
}
