using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockFireTests
{
    private static OnTickEvent Tick(FakeWorldContext world, int x = 0, int y = 64, int z = 0) => new(world, x, y, z, world.Reader.GetBlockMeta(x, y, z), world.Reader.GetBlockId(x, y, z));

    // Regression: FireBehavior is wired into the Ticker, Physics, and Lifecycle slots
    // independently, and AttachBehaviors builds a separate instance per slot. portal_base/
    // portal_fill/eternal_fuel/explosive are required constructor params resolved once per
    // slot from that slot's own JSON blob (fire.json declares them identically on all
    // three) — not instance fields set via OnInit (which only ever runs on the
    // Lifecycle-slot instance), which is what originally NRE'd on the Ticker-slot instance.
    [Fact]
    public void OnTick_DoesNotThrow()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("fire").Id);

        BlockRegistry.Get("fire").OnTick(Tick(world));
    }

    [Fact]
    public void OnPlaced_DoesNotThrow()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("fire").Id);

        BlockRegistry.Get("fire").OnPlaced(new OnPlacedEvent(world, null, Side.Up, Side.Up, 0, 64, 0));
    }

    // portal_base/portal_fill/eternal_fuel/explosive have no built-in vanilla fallback — an
    // omitted or unknown name must throw immediately at BehaviorRegistry.Build time (server
    // boot).
    [Fact]
    public void BehaviorRegistry_Build_MissingField_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"fire","eternal_fuel":"betasharp:netherrack","explosive":"betasharp:tnt"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("fire", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"fire","portal_base":"not_a_real_block","portal_fill":"betasharp:nether_portal","eternal_fuel":"betasharp:netherrack","explosive":"betasharp:tnt"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("fire", json.RootElement));
    }
}
