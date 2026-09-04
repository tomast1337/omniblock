using OmniBlock.Server;

namespace OmniBlock.Tests;

/// <summary>
///     Tests for <see cref="OmniBlockServer.ResolveDefaultGameMode" /> — the logic that
///     picks the server default from the game-mode registry at startup and after /reload.
/// </summary>
[Collection("RegistryAccess")]
public class GameModesTests : IDisposable
{
    private readonly string _tempDir;

    public GameModesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);

        RegistryAccess.ClearDynamicEntries();
        RegistryAccess.AddDynamic(RegistryDefinitions.GameModes);
    }

    public void Dispose()
    {
        RegistryAccess.ClearDynamicEntries();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private RegistryAccess BuildWithGameModes(params string[] names)
    {
        var dir = Path.Combine(_tempDir, "assets", "gamemode");
        Directory.CreateDirectory(dir);
        foreach (var name in names)
            File.WriteAllText(Path.Combine(dir, $"{name}.json"), "{}");
        return RegistryAccess.Build(_tempDir);
    }

    private static IReadableRegistry<GameMode> Reg(RegistryAccess ra) =>
        ra.GetOrThrow(RegistryKeys.GameModes);

    // ---- Configured name ----

    [Fact]
    public void Resolves_explicitly_configured_mode()
    {
        var ra = BuildWithGameModes("survival", "creative");

        var result = DefaultGameModeListener.ResolveDefaultGameMode(Reg(ra), "creative");

        Assert.NotNull(result);
        Assert.Equal("creative", result.Value.Name);
    }

    [Fact]
    public void Configured_name_not_found_falls_back_to_survival()
    {
        var ra = BuildWithGameModes("survival", "creative");

        var result = DefaultGameModeListener.ResolveDefaultGameMode(Reg(ra), "adventure");

        Assert.NotNull(result);
        Assert.Equal("survival", result.Value.Name);
    }

    // ---- Fallback chain ----

    [Fact]
    public void Empty_configured_name_falls_back_to_survival()
    {
        var ra = BuildWithGameModes("survival", "creative");

        var result = DefaultGameModeListener.ResolveDefaultGameMode(Reg(ra), "");

        Assert.NotNull(result);
        Assert.Equal("survival", result.Value.Name);
    }

    [Fact]
    public void No_survival_falls_back_to_default()
    {
        var ra = BuildWithGameModes("default", "creative");

        var result = DefaultGameModeListener.ResolveDefaultGameMode(Reg(ra), "");

        Assert.NotNull(result);
        Assert.Equal("default", result.Value.Name);
    }

    [Fact]
    public void No_survival_or_default_falls_back_to_first_registered_entry()
    {
        var ra = BuildWithGameModes("adventure");

        var result = DefaultGameModeListener.ResolveDefaultGameMode(Reg(ra), "");

        Assert.NotNull(result);
        Assert.Equal("adventure", result.Value.Name);
    }

    [Fact]
    public void Empty_registry_returns_null()
    {
        var ra = BuildWithGameModes();

        var result = DefaultGameModeListener.ResolveDefaultGameMode(Reg(ra), "survival");

        Assert.Null(result);
    }

    // ---- Properties survive resolution ----

    [Fact]
    public void Resolved_mode_has_correct_properties_from_json()
    {
        var dir = Path.Combine(_tempDir, "assets", "gamemode");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "creative.json"), "{\"DisallowFlying\":false}");
        var ra = RegistryAccess.Build(_tempDir);

        var result = DefaultGameModeListener.ResolveDefaultGameMode(Reg(ra), "creative");

        Assert.NotNull(result);
        Assert.False(result.Value.DisallowFlying);
        Assert.True(result.Value.CanBreak); // default value preserved
    }

    [Fact]
    public void Resolved_mode_has_betasharp_namespace()
    {
        var ra = BuildWithGameModes("survival");

        var result = DefaultGameModeListener.ResolveDefaultGameMode(Reg(ra), "survival");

        Assert.NotNull(result);
        Assert.Equal(Namespace.OmniBlock, result.Value.Namespace);
    }

    // ---- String lookup via AsAssetLoader() ----

    [Fact]
    public void AsAssetLoader_TryGet_finds_mode_by_exact_name()
    {
        var ra = BuildWithGameModes("survival", "creative");

        var found = Reg(ra).AsAssetLoader().TryGet("survival", out var mode);

        Assert.True(found);
        Assert.Equal("survival", mode!.Name);
    }

    [Fact]
    public void AsAssetLoader_TryGet_returns_false_for_unknown_name()
    {
        var ra = BuildWithGameModes("survival");

        var found = Reg(ra).AsAssetLoader().TryGet("adventure", out _);

        Assert.False(found);
    }

    [Fact]
    public void AsAssetLoader_TryGetByPrefix_matches_prefix()
    {
        var ra = BuildWithGameModes("survival", "creative");

        var found = Reg(ra).AsAssetLoader().TryGetByPrefix("surv", out var mode);

        Assert.True(found);
        Assert.Equal("survival", mode!.Name);
    }

    [Fact]
    public void AsAssetLoader_TryGetByPrefix_matches_single_character()
    {
        var ra = BuildWithGameModes("survival", "creative");

        var found = Reg(ra).AsAssetLoader().TryGetByPrefix("s", out var mode);

        Assert.True(found);
        Assert.StartsWith("s", mode!.Name);
    }
}
