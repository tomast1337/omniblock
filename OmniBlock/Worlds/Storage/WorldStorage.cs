using OmniBlock.Entities;
using OmniBlock.Server.Worlds;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Worlds.Storage;

public interface IWorldStorage
{
    WorldProperties? LoadProperties();

    void CheckSessionLock();

    IChunkStorage? GetChunkStorage(Dimension dimension);

    void Save(WorldProperties properties, List<EntityPlayer> players);

    void Save(WorldProperties properties);

    void ForceSave();

    IPlayerStorage? GetPlayerStorage();

    FileInfo? GetWorldPropertiesFile(string name);

    /// <summary>
    ///     Durable metadata owned by offline/background world-generation jobs. Implementations
    ///     without persistent storage may return <see langword="null" />.
    /// </summary>
    DirectoryInfo? GetWorldGenerationStateDirectory() => null;

    /// <summary>
    ///     Disposable distant-terrain data. Deleting this directory must never affect the
    ///     authoritative world save.
    /// </summary>
    DirectoryInfo? GetTerrainLodCacheDirectory() => null;
}
