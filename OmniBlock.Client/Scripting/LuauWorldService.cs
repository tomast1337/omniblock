using OmniBlock.Luau.Host;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Storage;

namespace OmniBlock.Client.Scripting;

internal sealed class LuauWorldService(IWorldStorageSource storage, Func<bool> canLoad)
{
    private LuauWorldLoadRequest? _pending;

    public IReadOnlyList<LuauWorldInfo> List() =>
    [
        .. storage.GetAll().Select(world => new LuauWorldInfo(
            world.FileName,
            world.DisplayName,
            world.LastPlayed,
            world.Size,
            world.IsUnsupported))
    ];

    public bool RequestLoad(string id)
    {
        if (_pending != null || !canLoad() || string.IsNullOrWhiteSpace(id)) return false;

        WorldSaveInfo? world = storage.GetAll()
            .FirstOrDefault(candidate => string.Equals(candidate.FileName, id, StringComparison.Ordinal));
        if (world == null || world.IsUnsupported) return false;

        WorldProperties? properties = storage.GetProperties(world.FileName);
        WorldSettings settings = properties == null
            ? new WorldSettings(0L, WorldType.Default)
            : new WorldSettings(properties.RandomSeed, properties.TerrainType, properties.GeneratorOptions);

        _pending = new LuauWorldLoadRequest(world.FileName, world.DisplayName, settings);
        return true;
    }

    public bool TryTakePending(out LuauWorldLoadRequest? request)
    {
        request = _pending;
        _pending = null;
        return request != null;
    }
}

internal sealed record LuauWorldLoadRequest(string Id, string DisplayName, WorldSettings Settings);
