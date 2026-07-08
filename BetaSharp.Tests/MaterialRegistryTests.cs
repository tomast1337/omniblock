using BetaSharp.Blocks;
using BetaSharp.Blocks.Materials;
using BetaSharp.Registries.Data;
using BetaSharp.Worlds.Maps;

namespace BetaSharp.Tests;

/// <summary>
/// Pins the data-driven <see cref="Material"/> and <see cref="BlockSoundGroup"/> registries
/// against the pre-migration hardcoded values, so a JSON typo cannot silently change physics.
/// </summary>
public class MaterialRegistryTests
{
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

    // Source of truth: the static singletons deleted by the migration
    // (docs/dependencies-migration-plan.md §1.3).
    private static readonly Dictionary<string, ExpectedMaterial> s_expected = new()
    {
        ["air"] = new(MapColor.Air, IsSolid: false, BlocksVision: false, BlocksMovement: false, IsReplaceable: true),
        ["solid_organic"] = new(MapColor.Grass),
        ["soil"] = new(MapColor.Dirt),
        ["wood"] = new(MapColor.Wood, IsBurnable: true),
        ["stone"] = new(MapColor.Stone, IsHandHarvestable: false),
        ["metal"] = new(MapColor.Iron, IsHandHarvestable: false),
        ["water"] = new(MapColor.Water, IsFluid: true, IsSolid: false, BlocksMovement: false, IsReplaceable: true, PistonBehavior: PistonBehavior.Destroy),
        ["lava"] = new(MapColor.TNT, IsFluid: true, IsSolid: false, BlocksMovement: false, IsReplaceable: true, PistonBehavior: PistonBehavior.Destroy),
        ["leaves"] = new(MapColor.Foliage, IsBurnable: true, IsTransparent: true, PistonBehavior: PistonBehavior.Destroy),
        ["plant"] = new(MapColor.Foliage, IsSolid: false, BlocksVision: false, BlocksMovement: false, PistonBehavior: PistonBehavior.Destroy),
        ["sponge"] = new(MapColor.Cloth),
        ["wool"] = new(MapColor.Cloth, IsBurnable: true),
        ["fire"] = new(MapColor.Air, IsSolid: false, BlocksVision: false, BlocksMovement: false, IsReplaceable: true, PistonBehavior: PistonBehavior.Destroy),
        ["sand"] = new(MapColor.Sand),
        ["piston_breakable"] = new(MapColor.Air, IsSolid: false, BlocksVision: false, BlocksMovement: false, PistonBehavior: PistonBehavior.Destroy),
        ["glass"] = new(MapColor.Air, IsTransparent: true),
        ["tnt"] = new(MapColor.TNT, IsBurnable: true, IsTransparent: true),
        ["foliage"] = new(MapColor.Foliage, PistonBehavior: PistonBehavior.Destroy),
        ["ice"] = new(MapColor.Ice, IsTransparent: true),
        ["snow_layer"] = new(MapColor.Snow, IsSolid: false, BlocksVision: false, BlocksMovement: false, IsReplaceable: true, IsHandHarvestable: false, IsTransparent: true, PistonBehavior: PistonBehavior.Destroy),
        ["snow_block"] = new(MapColor.Snow, IsHandHarvestable: false),
        ["cactus"] = new(MapColor.Foliage, IsTransparent: true, PistonBehavior: PistonBehavior.Destroy),
        ["clay"] = new(MapColor.Clay),
        ["pumpkin"] = new(MapColor.Foliage, PistonBehavior: PistonBehavior.Destroy),
        ["nether_portal"] = new(MapColor.Air, IsSolid: false, BlocksVision: false, BlocksMovement: false, PistonBehavior: PistonBehavior.Unpushable),
        ["cake"] = new(MapColor.Air, PistonBehavior: PistonBehavior.Destroy),
        ["cobweb"] = new(MapColor.Cloth, IsHandHarvestable: false, PistonBehavior: PistonBehavior.Destroy),
        ["piston"] = new(MapColor.Stone, PistonBehavior: PistonBehavior.Unpushable),
    };

    public static TheoryData<string> MaterialKeys()
    {
        var data = new TheoryData<string>();
        foreach (string key in s_expected.Keys) data.Add(key);
        return data;
    }

    [Theory]
    [MemberData(nameof(MaterialKeys))]
    public void LoadedFlags_MatchPreMigrationValues(string key)
    {
        ExpectedMaterial expected = s_expected[key];
        Material actual = MaterialRegistry.Get(key);

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
        Assert.Same(MaterialRegistry.Get("stone"), BlockRegistry.Get("stone").Material);
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
        Assert.False(Material.Glass.Suffocates);   // transparent
        Assert.False(Material.Plant.Suffocates);   // doesn't block movement
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
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => registry.Get("water"));
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
}
