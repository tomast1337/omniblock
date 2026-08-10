using OmniBlock.Registries;
using OmniBlock.Registries.Data;

namespace OmniBlock.Tests;

/// <summary>
///     The contract <c>ServerPlayerEntity</c>'s NBT round trip depends on: it writes
///     <c>GameModeHolder.Value.ToString()</c> and reads it back through
///     <c>DataAssetLoader.TryGetHolder</c>. Those are two different types agreeing on a string
///     format by convention rather than by a shared method, so a change to either silently resets
///     every player to the server default on their next login.
/// </summary>
[Collection("RegistryAccess")]
public class GameModePersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public GameModePersistenceTests()
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
            Directory.Delete(_tempDir, recursive: true);

        GC.SuppressFinalize(this);
    }

    private DataAssetLoader<GameMode> BuildWithGameModes(params string[] names)
    {
        string dir = Path.Combine(_tempDir, "assets", "gamemode");
        Directory.CreateDirectory(dir);
        foreach (string name in names)
        {
            File.WriteAllText(Path.Combine(dir, $"{name}.json"), "{}");
        }

        return RegistryAccess.Build(basePath: _tempDir).GetOrThrow(RegistryKeys.GameModes).AsAssetLoader();
    }

    [Theory]
    [InlineData("creative")]
    [InlineData("survival")]
    [InlineData("adventure")]
    [InlineData("spectator")]
    public void The_written_name_resolves_back_to_the_same_game_mode(string name)
    {
        DataAssetLoader<GameMode> loader = BuildWithGameModes("survival", "creative", "adventure", "spectator");

        Assert.True(loader.TryGetHolder(name, out Holder<GameMode>? original));

        // Exactly what WriteNbt stores and ReadNbt looks up.
        string written = original.Value.ToString();

        Assert.True(
            loader.TryGetHolder(written, out Holder<GameMode>? restored),
            $"'{written}' did not resolve; a player saved in {name} would load as the server default.");
        Assert.Same(original.Value, restored.Value);
    }

    /// <summary>
    ///     The stored form is namespaced. Worth pinning: an unqualified name would resolve to
    ///     whichever namespace happened to win, which is the wrong entry as soon as a mod registers
    ///     a game mode under a name the base game also uses.
    /// </summary>
    [Fact]
    public void The_written_name_is_namespace_qualified()
    {
        DataAssetLoader<GameMode> loader = BuildWithGameModes("creative");

        Assert.True(loader.TryGetHolder("creative", out Holder<GameMode>? creative));

        Assert.Equal("omniblock:creative", creative.Value.ToString());
    }
}
