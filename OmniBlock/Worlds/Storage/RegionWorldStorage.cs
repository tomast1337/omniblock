using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using OmniBlock.Entities;
using OmniBlock.NBT;
using OmniBlock.Server.Worlds;
using OmniBlock.Worlds.Chunks.Storage;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Worlds.Storage;

internal class RegionWorldStorage : IWorldStorage, IPlayerStorage
{
    private readonly DirectoryInfo _dataDir;
    private readonly ILogger<RegionWorldStorage> _logger = Log.Instance.For<RegionWorldStorage>();


    private readonly long _now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly DirectoryInfo _playersDirectory;
    private readonly DirectoryInfo _saveDirectory;

    public RegionWorldStorage(string baseDir, string worldName, bool createPlayersDir) :
        this(new DirectoryInfo(Path.Combine(baseDir, worldName)), createPlayersDir)
    {
    }

    public RegionWorldStorage(DirectoryInfo saveDirectory, bool createPlayersDir)
    {
        _saveDirectory = saveDirectory;
        if (!_saveDirectory.Exists) _saveDirectory.Create();

        _playersDirectory = new DirectoryInfo(Path.Combine(_saveDirectory.FullName, "players"));

        _dataDir = new DirectoryInfo(Path.Combine(_saveDirectory.FullName, "data"));
        if (!_dataDir.Exists) _dataDir.Create();

        if (createPlayersDir && !_playersDirectory.Exists)
        {
            _playersDirectory.Create();
        }

        WriteSessionLock();
    }

    public void SavePlayerData(EntityPlayer player)
    {
        try
        {
            NBTTagCompound tag = new();
            player.Write(tag);

            var tempFile = Path.Combine(_playersDirectory.FullName, "_tmp_.dat");
            var finalFile = Path.Combine(_playersDirectory.FullName, $"{HashPlayerFileName(player.Name ?? throw new InvalidOperationException("Cannot save player data without a name."))}.dat");

            using (var stream = File.Create(tempFile))
            {
                NbtIo.WriteCompressed(tag, stream);
            }

            File.Move(tempFile, finalFile, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to save player data for {player.Name}");
        }
    }

    public void LoadPlayerData(EntityPlayer player)
    {
        if (player.Name is not { } playerName) return;
        var tag = loadPlayerData(playerName);
        if (tag != null)
        {
            player.Read(tag);
        }
    }

    public void CheckSessionLock()
    {
        try
        {
            var lockFile = Path.Combine(_saveDirectory.FullName, "session.lock");
            using var stream = File.OpenRead(lockFile);
            using var reader = new BinaryReader(stream);

            if (reader.ReadInt64() != _now)
            {
                throw new Exception("The save is being accessed from another location, aborting");
            }
        }
        catch (IOException ex)
        {
            throw new Exception("Failed to check session lock, aborting", ex);
        }
    }

    public virtual IChunkStorage GetChunkStorage(Dimension dimension)
    {
        if (dimension is NetherDimension)
        {
            var netherDir = new DirectoryInfo(Path.Combine(_saveDirectory.FullName, "DIM-1"));
            if (!netherDir.Exists) netherDir.Create();

            return new RegionChunkStorage(netherDir.FullName);
        }

        return new RegionChunkStorage(_saveDirectory.FullName);
    }

    public virtual void Save(WorldProperties properties, List<EntityPlayer> players)
    {
        properties.SaveVersion = 19132;

        NBTTagCompound rootTag = new();
        NBTTagCompound dataTag;

        if (players.Count > 0)
        {
            dataTag = properties.getNBTTagCompoundWithPlayer(players);
            AdjustPlayerYForSingleplayer(dataTag.GetCompoundTag("Player"));
        }
        else
        {
            dataTag = properties.getNBTTagCompound();
            var mostRecentPlayer = GetMostRecentPlayerData();
            if (mostRecentPlayer != null)
            {
                AdjustPlayerYForSingleplayer(mostRecentPlayer);
                dataTag.SetCompoundTag("Player", mostRecentPlayer);
            }
            else if (properties.PlayerTag != null)
            {
                dataTag.SetCompoundTag("Player", properties.PlayerTag);
            }
        }

        rootTag.SetTag("Data", dataTag);

        WriteLevelDat(rootTag);
    }

    public void Save(WorldProperties properties)
    {
        var dataTag = properties.getNBTTagCompound();
        NBTTagCompound rootTag = new();
        rootTag.SetTag("Data", dataTag);

        WriteLevelDat(rootTag);
    }

    public WorldProperties? LoadProperties()
    {
        var levelDat = Path.Combine(_saveDirectory.FullName, "level.dat");
        var levelDatOld = Path.Combine(_saveDirectory.FullName, "level.dat_old");

        string[] filesToTry = { levelDat, levelDatOld };

        foreach (var file in filesToTry)
        {
            if (!File.Exists(file)) continue;

            try
            {
                using var stream = File.OpenRead(file);
                var root = NbtIo.ReadCompressed(stream);
                var data = root.GetCompoundTag("Data");
                return new WorldProperties(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception loading properties from {file}");
            }
        }

        return null;
    }

    public FileInfo GetWorldPropertiesFile(string name) => new(Path.Combine(_dataDir.FullName, $"{name}.dat"));

    public DirectoryInfo GetWorldGenerationStateDirectory()
    {
        var directory = new DirectoryInfo(Path.Combine(_dataDir.FullName, "worldgen"));
        if (!directory.Exists) directory.Create();
        return directory;
    }

    public DirectoryInfo GetTerrainLodCacheDirectory()
    {
        var directory = new DirectoryInfo(Path.Combine(_dataDir.FullName, "terrain_lod"));
        if (!directory.Exists) directory.Create();
        return directory;
    }

    public IPlayerStorage GetPlayerStorage() => this;

    public void ForceSave()
    {
    }

    private void WriteSessionLock()
    {
        try
        {
            var lockFile = Path.Combine(_saveDirectory.FullName, "session.lock");

            using var stream = File.Create(lockFile);
            using var writer = new BinaryWriter(stream);
            writer.Write(_now);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to check session lock, aborting.");
            throw new InvalidOperationException("Failed to check session lock, aborting", ex);
        }
    }

    private static void AdjustPlayerYForSingleplayer(NBTTagCompound? player)
    {
        if (player != null && player.HasKey("Pos"))
        {
            var posList = player.GetTagList("Pos");
            if (posList.TagCount() >= 3)
            {
                var x = ((NBTTagDouble)posList.TagAt(0)).Value;
                var y = ((NBTTagDouble)posList.TagAt(1)).Value;
                var z = ((NBTTagDouble)posList.TagAt(2)).Value;

                var newPos = new NBTTagList();
                newPos.SetTag(new NBTTagDouble(x));
                newPos.SetTag(new NBTTagDouble(y + 3.24D)); // Vanilla SP saves foot + yOffset (1.62) + ySize (1.62)
                newPos.SetTag(new NBTTagDouble(z));
                player.SetTag("Pos", newPos);
            }
        }
    }

    private NBTTagCompound? GetMostRecentPlayerData()
    {
        try
        {
            if (_playersDirectory.Exists)
            {
                var file = _playersDirectory.GetFiles("*.dat").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                if (file != null)
                {
                    using var stream = file.OpenRead();
                    return NbtIo.ReadCompressed(stream);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read fallback player data for level.dat");
        }

        return null;
    }

    private void WriteLevelDat(NBTTagCompound rootTag)
    {
        try
        {
            var levelDatNew = Path.Combine(_saveDirectory.FullName, "level.dat_new");
            var levelDatOld = Path.Combine(_saveDirectory.FullName, "level.dat_old");
            var levelDat = Path.Combine(_saveDirectory.FullName, "level.dat");

            using (var stream = File.Create(levelDatNew))
            {
                NbtIo.WriteCompressed(rootTag, stream);
            }

            if (File.Exists(levelDatOld)) File.Delete(levelDatOld);
            if (File.Exists(levelDat)) File.Move(levelDat, levelDatOld);
            if (File.Exists(levelDatNew)) File.Move(levelDatNew, levelDat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception writing level.dat");
        }
    }

    // Usernames may contain characters that aren't safe as filenames (or aren't safe across every
    // OS's filesystem); hash them so the file layout doesn't depend on what a player is allowed to
    // name themselves.
    private static string HashPlayerFileName(string playerName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(playerName));
        return Convert.ToHexStringLower(hash);
    }

    public NBTTagCompound? loadPlayerData(string playerName)
    {
        try
        {
            var playerFile = Path.Combine(_playersDirectory.FullName, $"{HashPlayerFileName(playerName)}.dat");
            if (File.Exists(playerFile))
            {
                using var stream = File.OpenRead(playerFile);
                return NbtIo.ReadCompressed(stream);
            }

            // Back-compat: saves written before player files were hash-named stored them by the
            // literal (untrusted) username. Read that once and migrate it to the hashed name.
            var legacyNamedFile = Path.Combine(_playersDirectory.FullName, $"{playerName}.dat");
            if (File.Exists(legacyNamedFile))
            {
                using var stream = File.OpenRead(legacyNamedFile);
                var legacyTag = NbtIo.ReadCompressed(stream);

                using var writeStream = File.Create(playerFile);
                NbtIo.WriteCompressed(legacyTag, writeStream);

                _logger.LogInformation($"Migrated player data for {playerName} to hashed filename");
                return legacyTag;
            }

            var levelFile = Path.Combine(_saveDirectory.FullName, "level.dat");
            if (File.Exists(levelFile))
            {
                try
                {
                    using var stream = File.OpenRead(levelFile);
                    var levelDat = NbtIo.ReadCompressed(stream);
                    var data = levelDat.GetCompoundTag("Data");

                    if (data.HasKey("Player"))
                    {
                        var playerTag = data.GetCompoundTag("Player");

                        using var writeStream = File.Create(playerFile);
                        NbtIo.WriteCompressed(playerTag, writeStream);

                        _logger.LogInformation($"Migrated singleplayer player data from level.dat for {playerName}");
                        return playerTag;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Failed to migrate player data from level.dat");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to load player data for {playerName}");
        }

        return null;
    }
}
