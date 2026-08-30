using OmniBlock.Client.Scripting;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Storage;

namespace OmniBlock.Tests.Luau;

public sealed class LuauWorldServiceTests
{
    [Fact]
    public void ListsSavesAndQueuesSupportedWorldWithStoredSettings()
    {
        StubStorage storage = new();
        LuauWorldService service = new(storage, () => true);

        Assert.Equal(["World1", "Unsupported"], service.List().Select(world => world.Id));
        Assert.True(service.RequestLoad("World1"));
        Assert.False(service.RequestLoad("World1"));
        Assert.True(service.TryTakePending(out LuauWorldLoadRequest? request));
        Assert.NotNull(request);
        Assert.Equal("World1", request.Id);
        Assert.Equal("My World", request.DisplayName);
        Assert.Equal(8675309, request.Settings.Seed);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Unsupported")]
    public void RejectsMissingAndUnsupportedWorlds(string id)
    {
        Assert.False(new LuauWorldService(new StubStorage(), () => true).RequestLoad(id));
    }

    [Fact]
    public void RejectsLoadsOutsideSafeMainMenuState()
    {
        Assert.False(new LuauWorldService(new StubStorage(), () => false).RequestLoad("World1"));
    }

    private sealed class StubStorage : IWorldStorageSource
    {
        public string Name => "test";
        public List<WorldSaveInfo> GetAll() =>
        [
            new("World1", "My World", 100, 200, false),
            new("Unsupported", "Old", 50, 75, true)
        ];

        public WorldProperties? GetProperties(string saveName) =>
            saveName == "World1" ? new WorldProperties(8675309, "My World") : null;

        public IWorldStorage Get(string saveName, bool createPlayerDataDir) => throw new NotSupportedException();
        public void Flush() { }
        public void Delete(string saveName) => throw new NotSupportedException();
        public void Rename(string saveName, string newName) => throw new NotSupportedException();
    }
}
