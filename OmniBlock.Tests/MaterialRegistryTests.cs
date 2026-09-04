using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Registries.Data;
using OmniBlock.Worlds.Maps;

namespace OmniBlock.Tests;

/// <summary>
///     Pins the data-driven <see cref="Material" /> and <see cref="BlockSoundGroup" /> registries
///     against the pre-migration hardcoded values, so a JSON typo cannot silently change physics.
/// </summary>
public class MaterialRegistryTests
{
    // Source of truth: the static singletons the data-driven migration deleted.
    private static readonly Dictionary<string, ExpectedMaterial> s_expected = new()
    {
        ["air"] = new ExpectedMaterial(MapColor.Air, IsSolid: false, BlocksVision: false, BlocksMovement: false, IsReplaceable: true),
        ["solid_organic"] = new ExpectedMaterial(MapColor.Grass),
        ["soil"] = new ExpectedMaterial(MapColor.Dirt),
        ["wood"] = new ExpectedMaterial(MapColor.Wood, IsBurnable: true),
        ["stone"] = new ExpectedMaterial(MapColor.Stone, IsHandHarvestable: false),
        ["metal"] = new ExpectedMaterial(MapColor.Iron, IsHandHarvestable: false),
        ["water"] = new ExpectedMaterial(MapColor.Water, true, false, BlocksMovement: false, IsReplaceable: true, PistonBehavior: PistonBehavior.Destroy),
        ["lava"] = new ExpectedMaterial(MapColor.TNT, true, false, BlocksMovement: false, IsReplaceable: true, PistonBehavior: PistonBehavior.Destroy),
        ["leaves"] = new ExpectedMaterial(MapColor.Foliage, IsBurnable: true, IsTransparent: true, PistonBehavior: PistonBehavior.Destroy),
        ["plant"] = new ExpectedMaterial(MapColor.Foliage, IsSolid: false, BlocksVision: false, BlocksMovement: false, PistonBehavior: PistonBehavior.Destroy),
        ["sponge"] = new ExpectedMaterial(MapColor.Cloth),
        ["wool"] = new ExpectedMaterial(MapColor.Cloth, IsBurnable: true),
        ["fire"] = new ExpectedMaterial(MapColor.Air, IsSolid: false, BlocksVision: false, BlocksMovement: false, IsReplaceable: true, PistonBehavior: PistonBehavior.Destroy),
        ["sand"] = new ExpectedMaterial(MapColor.Sand),
        ["piston_breakable"] = new ExpectedMaterial(MapColor.Air, IsSolid: false, BlocksVision: false, BlocksMovement: false, PistonBehavior: PistonBehavior.Destroy),
        ["glass"] = new ExpectedMaterial(MapColor.Air, IsTransparent: true),
        ["tnt"] = new ExpectedMaterial(MapColor.TNT, IsBurnable: true, IsTransparent: true),
        ["foliage"] = new ExpectedMaterial(MapColor.Foliage, PistonBehavior: PistonBehavior.Destroy),
        ["ice"] = new ExpectedMaterial(MapColor.Ice, IsTransparent: true),
        ["snow_layer"] = new ExpectedMaterial(MapColor.Snow, IsSolid: false, BlocksVision: false, BlocksMovement: false, IsReplaceable: true, IsHandHarvestable: false, IsTransparent: true, PistonBehavior: PistonBehavior.Destroy),
        ["snow_block"] = new ExpectedMaterial(MapColor.Snow, IsHandHarvestable: false),
        ["cactus"] = new ExpectedMaterial(MapColor.Foliage, IsTransparent: true, PistonBehavior: PistonBehavior.Destroy),
        ["clay"] = new ExpectedMaterial(MapColor.Clay),
        ["pumpkin"] = new ExpectedMaterial(MapColor.Foliage, PistonBehavior: PistonBehavior.Destroy),
        ["nether_portal"] = new ExpectedMaterial(MapColor.Air, IsSolid: false, BlocksVision: false, BlocksMovement: false, PistonBehavior: PistonBehavior.Unpushable),
        ["cake"] = new ExpectedMaterial(MapColor.Air, PistonBehavior: PistonBehavior.Destroy),
        ["cobweb"] = new ExpectedMaterial(MapColor.Cloth, IsHandHarvestable: false, PistonBehavior: PistonBehavior.Destroy),
        ["piston"] = new ExpectedMaterial(MapColor.Stone, PistonBehavior: PistonBehavior.Unpushable)
    };

    public static TheoryData<string> MaterialKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in s_expected.Keys) data.Add(key);
        return data;
    }

    [Theory]
    [MemberData(nameof(MaterialKeys))]
    public void LoadedFlags_MatchPreMigrationValues(string key)
    {
        var expected = s_expected[key];
        var actual = MaterialRegistry.Get(key);

        Assert.Equal(expected.MapColor, actual.MapColor);
        Assert.Equal(expected.IsFluid, actual.IsFluid);
        Assert.Equal(expected.IsSolid, actual.IsSolid);
        Assert.Equal(expected.BlocksVision, actual.BlocksVision);
        Assert.Equal(expected.BlocksMovement, actual.BlocksMovement);
        Assert.Equal(expected.IsBurnable, actual.IsBurnable);
        Assert.Equal(expected.IsReplaceable, actual.IsReplaceable);
        Assert.Equal(expected.IsHandHarvestable, actual.IsHandHarvestable);
        Assert.Equal(expected.IsTransparent, actual.IsTransparent);
        Assert.Equal(expected.PistonBehavior, actual.PistonBehavior);
    }

    [Fact]
    public void FacadeAndRegistry_ReturnSameCanonicalInstance()
    {
        Assert.Same(MaterialRegistry.Get("water"), Material.Water);
        Assert.Same(MaterialRegistry.Get("water"), MaterialRegistry.Get("water"));
        Assert.Same(MaterialRegistry.Get("stone"), TestBlocks.Get("stone").Material);
    }

    [Fact]
    public void Liquids_KeepBlockingVision()
    {
        // Regression trap: MaterialLiquid never overrode BlocksVision — do not "fix" this.
        Assert.True(Material.Water.BlocksVision);
        Assert.True(Material.Lava.BlocksVision);
    }

    [Fact]
    public void Suffocates_IsPureFunctionOfTransparencyAndMovement()
    {
        Assert.True(Material.Stone.Suffocates);
        Assert.False(Material.Glass.Suffocates); // transparent
        Assert.False(Material.Plant.Suffocates); // doesn't block movement
    }

    [Fact]
    public void Get_UnknownKey_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => MaterialRegistry.Get("bedrockium"));
        Assert.False(MaterialRegistry.TryGet("bedrockium", out _));
    }

    [Fact]
    public void UninitializedRegistry_FailsLoudWithBootstrapHint()
    {
        // Guard logic verified on a fresh instance: the process-global registries are
        // already initialized by the assembly bootstrap and must never be reset.
        var registry = new CanonicalRegistry<Material>("material");
        var ex = Assert.Throws<InvalidOperationException>(() => registry.Get("water"));
        Assert.Contains("Bootstrap.Initialize", ex.Message);
    }

    [Fact]
    public void SoundGroups_MatchPreMigrationValues()
    {
        Assert.Equal(new BlockSoundGroup("stone", 1.0F, 1.0F), SoundGroupRegistry.Get("powder"));
        Assert.Equal(new BlockSoundGroup("wood", 1.0F, 1.0F), SoundGroupRegistry.Get("wood"));
        Assert.Equal(new BlockSoundGroup("gravel", 1.0F, 1.0F), SoundGroupRegistry.Get("gravel"));
        Assert.Equal(new BlockSoundGroup("grass", 1.0F, 1.0F), SoundGroupRegistry.Get("grass"));
        Assert.Equal(new BlockSoundGroup("stone", 1.0F, 1.0F), SoundGroupRegistry.Get("stone"));
        Assert.Equal(new BlockSoundGroup("stone", 1.0F, 1.5F), SoundGroupRegistry.Get("metal"));
        Assert.Equal(new BlockSoundGroup("stone", 1.0F, 1.0F, "random.glass"), SoundGroupRegistry.Get("glass"));
        Assert.Equal(new BlockSoundGroup("cloth", 1.0F, 1.0F), SoundGroupRegistry.Get("cloth"));
        Assert.Equal(new BlockSoundGroup("sand", 1.0F, 1.0F, "step.gravel"), SoundGroupRegistry.Get("sand"));
    }

    [Fact]
    public void SoundGroups_AreCanonicalAndWiredIntoBlock()
    {
        Assert.Same(SoundGroupRegistry.Get("stone"), Block.SoundStoneFootstep);
        Assert.Equal("step.grass", SoundGroupRegistry.Get("grass").StepSound);
        Assert.Equal("random.glass", SoundGroupRegistry.Get("glass").BreakSound);
        Assert.Equal("step.gravel", SoundGroupRegistry.Get("sand").BreakSound);
    }

    private sealed record ExpectedMaterial(
        MapColor MapColor,
        bool IsFluid = false,
        bool IsSolid = true,
        bool BlocksVision = true,
        bool BlocksMovement = true,
        bool IsBurnable = false,
        bool IsReplaceable = false,
        bool IsHandHarvestable = true,
        bool IsTransparent = false,
        PistonBehavior PistonBehavior = PistonBehavior.Normal);
}
