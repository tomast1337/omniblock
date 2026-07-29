using System;
using System.Collections.Generic;
using System.Reflection;
using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Characterization test pinning every mob's resolved configuration to the values Beta 1.7.3 gives
/// it. Written against a dump of the real values, not assumed ones, so it catches transcription
/// errors in <c>assets/entity/*.json</c>.
/// <para>
/// Excluded, because they are genuinely dynamic rather than configuration: the slime (stats derive
/// from a randomly chosen size), the wolf's living sound (a random roll over four clips), and the
/// ghast's texture (swapped per tick while charging).
/// </para>
/// </summary>
[Collection("EntityTests")]
public sealed class MobConfigurationTests
{
    public sealed record MobConfig(
        int Health,
        float MovementSpeed,
        int? AttackStrength,
        float Width,
        float Height,
        string Texture,
        string? LivingSound,
        string? HurtSound,
        string? DeathSound,
        float SoundVolume,
        bool FireImmune,
        int MaxSpawnedInChunk,
        bool CanDespawn,
        int TalkInterval);

    public static TheoryData<string, MobConfig> ExpectedConfigs() => new()
    {
        { "zombie", new MobConfig(20, 0.5f, 5, 0.6f, 1.8f, "/mob/zombie.png", "mob.zombie", "mob.zombiehurt", "mob.zombiedeath", 1f, false, 4, true, 80) },
        { "giant", new MobConfig(200, 0.5f, 50, 3.6000001f, 10.799999f, "/mob/zombie.png", null, "random.hurt", "random.hurt", 1f, false, 4, true, 80) },
        { "pig_zombie", new MobConfig(20, 0.5f, 5, 0.6f, 1.8f, "/mob/pigzombie.png", "mob.zombiepig.zpig", "mob.zombiepig.zpighurt", "mob.zombiepig.zpigdeath", 1f, true, 4, true, 80) },
        { "skeleton", new MobConfig(20, 0.7f, 2, 0.6f, 1.8f, "/mob/skeleton.png", "mob.skeleton", "mob.skeletonhurt", "mob.skeletonhurt", 1f, false, 4, true, 80) },
        { "creeper", new MobConfig(20, 0.7f, 2, 0.6f, 1.8f, "/mob/creeper.png", null, "mob.creeper", "mob.creeperdeath", 1f, false, 4, true, 80) },
        { "spider", new MobConfig(20, 0.8f, 2, 1.4f, 0.9f, "/mob/spider.png", "mob.spider", "mob.spider", "mob.spiderdeath", 1f, false, 4, true, 80) },
        { "ghast", new MobConfig(10, 0.7f, null, 4f, 4f, "/mob/ghast.png", "mob.ghast.moan", "mob.ghast.scream", "mob.ghast.death", 10f, true, 1, true, 80) },
        { "pig", new MobConfig(10, 0.7f, 2, 0.9f, 0.9f, "/mob/pig.png", "mob.pig", "mob.pig", "mob.pigdeath", 1f, false, 4, true, 120) },
        { "cow", new MobConfig(10, 0.7f, 2, 0.9f, 1.3f, "/mob/cow.png", "mob.cow", "mob.cowhurt", "mob.cowhurt", 0.4f, false, 4, true, 120) },
        { "sheep", new MobConfig(10, 0.7f, 2, 0.9f, 1.3f, "/mob/sheep.png", "mob.sheep", "mob.sheep", "mob.sheep", 1f, false, 4, true, 120) },
        { "chicken", new MobConfig(4, 0.7f, 2, 0.3f, 0.4f, "/mob/chicken.png", "mob.chicken", "mob.chickenhurt", "mob.chickenhurt", 1f, false, 4, true, 120) },
        // AttackStrength is null where it was 2: the squid is no longer an EntityCreature, and the
        // value was never reachable — it has neither an attack nor targeting to spend it on.
        { "squid", new MobConfig(10, 0.7f, null, 0.95f, 0.95f, "/mob/squid.png", null, null, null, 0.4f, false, 4, true, 120) },
    };

    private static EntityLiving CreateMob(string name, IWorldContext world) => name switch
    {
        "zombie" => (EntityCreature)EntityRegistry.ByName("zombie").Create(world),
        "giant" => (EntityCreature)EntityRegistry.ByName("giant").Create(world),
        "pig_zombie" => (EntityCreature)EntityRegistry.ByName("pigzombie").Create(world),
        "skeleton" => (EntityCreature)EntityRegistry.ByName("skeleton").Create(world),
        "creeper" => (EntityCreature)EntityRegistry.ByName("creeper").Create(world),
        "spider" => (EntityCreature)EntityRegistry.ByName("spider").Create(world),
        "ghast" => (EntityLiving)EntityRegistry.ByName("ghast").Create(world),
        "pig" => (EntityCreature)EntityRegistry.ByName("pig").Create(world),
        "cow" => (EntityCreature)EntityRegistry.ByName("cow").Create(world),
        "sheep" => (EntityCreature)EntityRegistry.ByName("sheep").Create(world),
        "chicken" => (EntityCreature)EntityRegistry.ByName("chicken").Create(world),
        "squid" => (EntityLiving)EntityRegistry.ByName("squid").Create(world),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown mob.")
    };

    [Theory]
    [MemberData(nameof(ExpectedConfigs))]
    public void Mob_configuration_matches_vanilla_values(string name, MobConfig expected)
    {
        FakeWorldContext world = new();
        EntityLiving mob = CreateMob(name, world);

        Assert.Equal(expected, Describe(mob));
    }

    [Fact]
    public void Wolf_configuration_matches_except_its_randomised_living_sound()
    {
        FakeWorldContext world = new();
        EntityCreature wolf = (EntityCreature)EntityRegistry.ByName("wolf").Create(world);
        MobConfig actual = Describe(wolf);

        Assert.Equal(
            new MobConfig(8, 1.1f, 2, 0.8f, 0.8f, "/mob/wolf.png", actual.LivingSound, "mob.wolf.hurt", "mob.wolf.death", 0.4f, false, 8, true, 120),
            actual);

        Assert.Contains(actual.LivingSound, new[] { "mob.wolf.bark", "mob.wolf.panting", "mob.wolf.whine", "mob.wolf.growl" });
    }

    [Fact]
    public void Slime_stats_derive_from_its_randomly_chosen_size()
    {
        FakeWorldContext world = new();

        for (int attempt = 0; attempt < 20; attempt++)
        {
            EntityLiving slime = (EntityLiving)EntityRegistry.ByName("slime").Create(world);
            int size = slime.Synced<byte>("size")!.Value;

            Assert.Contains(size, new[] { 1, 2, 4 });
            Assert.Equal(size * size, slime.Health);
            Assert.Equal(0.6f * size, slime.Width, 5);
            Assert.Equal(0.6f * size, slime.Height, 5);
        }
    }

    private static MobConfig Describe(EntityLiving mob)
    {
        T Read<T>(string name, T fallback)
        {
            PropertyInfo? property = mob.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            return property?.GetValue(mob) is T value ? value : fallback;
        }

        return new MobConfig(
            mob.Health,
            Read("MovementSpeed", 0f),
            mob is EntityCreature ? Read("AttackStrength", 0) : null,
            mob.Width,
            mob.Height,
            Read<string?>("Texture", null) ?? "",
            Read<string?>("LivingSound", null),
            Read<string?>("HurtSound", null),
            Read<string?>("DeathSound", null),
            Read("SoundVolume", 0f),
            Read("IsImmuneToFire", false),
            mob.MaxSpawnedInChunk,
            Read("CanDespawn", true),
            Read("TalkInterval", 0));
    }
}
