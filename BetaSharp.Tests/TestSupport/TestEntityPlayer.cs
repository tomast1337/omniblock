using OmniBlock.Entities;
using OmniBlock.Stats;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Tests.TestSupport;

/// <summary>Minimal concrete player for tests that need a real <see cref="EntityPlayer"/> in the world (AI, interaction).</summary>
public sealed class TestEntityPlayer : EntityPlayer
{
    public TestEntityPlayer(IWorldContext world) : base(world)
    {
    }

    public override EntityType Type => EntityRegistry.ByName("player");

    /// <summary>Stats awarded to this player. The base implementation is a no-op, so tests record them here.</summary>
    private readonly Dictionary<StatBase, int> _stats = [];

    public override void IncreaseStat(StatBase stat, int amount) =>
        _stats[stat] = _stats.GetValueOrDefault(stat) + amount;

    public bool HasStat(StatBase stat) => _stats.ContainsKey(stat);

    public override void Spawn()
    {
    }
}
