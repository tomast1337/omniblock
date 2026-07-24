using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockRedstoneOreTests
{
    private static OnTickEvent Tick(FakeWorldContext world, int x = 0, int y = 64, int z = 0) => new(world, x, y, z, world.Reader.GetBlockMeta(x, y, z), world.Reader.GetBlockId(x, y, z));

    // Unlit/lit ore are required constructor params (JSON-configurable per variant, no built-in
    // vanilla fallback). Construct a differently configured instance directly (bypassing
    // BlockRegistry) to prove the override actually takes effect rather than silently defaulting.
    [Fact]
    public void OnTick_ConfiguredLitOre_RevertsToConfiguredUnlitOre()
    {
        FakeWorldContext world = new();
        Block customUnlit = BlockRegistry.Get("stone");
        Block customLit = BlockRegistry.Get("glowstone");
        world.ReaderWriter.SetInitial(0, 64, 0, customLit.id);

        RedstoneOreBehavior behavior = new(customUnlit, customLit);
        behavior.OnTick(customLit, Tick(world));

        Assert.Equal(customUnlit.id, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void OnTick_VanillaLitRedstoneOreNotInCustomConfig_DoesNotRevert()
    {
        FakeWorldContext world = new();
        Block customUnlit = BlockRegistry.Get("stone");
        Block customLit = BlockRegistry.Get("glowstone");
        Block vanillaLit = BlockRegistry.Get("lit_redstone_ore");
        world.ReaderWriter.SetInitial(0, 64, 0, vanillaLit.id);

        RedstoneOreBehavior behavior = new(customUnlit, customLit);
        behavior.OnTick(vanillaLit, Tick(world));

        Assert.Equal(vanillaLit.id, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void OnUse_ConfiguredUnlitOre_LightsToConfiguredLitOre()
    {
        FakeWorldContext world = new();
        Block customUnlit = BlockRegistry.Get("stone");
        Block customLit = BlockRegistry.Get("glowstone");
        world.ReaderWriter.SetInitial(0, 64, 0, customUnlit.id);

        RedstoneOreBehavior behavior = new(customUnlit, customLit);
        behavior.OnUse(customUnlit, new OnUseEvent(world, null!, 0, 64, 0));

        Assert.Equal(customLit.id, world.Reader.GetBlockId(0, 64, 0));
    }

    // No built-in default and no null fallback: an omitted or unknown "unlit_ore"/"lit_ore" in
    // JSON must throw immediately (at BehaviorRegistry.Build, i.e. server boot).
    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"redstone_ore","unlit_ore":"redstone_ore"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("redstone_ore", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"redstone_ore","unlit_ore":"not_a_real_block","lit_ore":"lit_redstone_ore"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("redstone_ore", json.RootElement));
    }
}
