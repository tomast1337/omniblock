using Microsoft.Extensions.Logging;
using OmniBlock.Network.Messages;
using OmniBlock.Registries;
using OmniBlock.Server.Network;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Server.Internal;

public enum TerrainLodScaleFixtureStatus
{
    Idle,
    Queued,
    Preparing,
    Complete,
    Failed
}

public sealed record TerrainLodScaleFixtureSnapshot(
    TerrainLodScaleFixtureStatus Status,
    int Tiles,
    string? Error);

public class InternalServer : OmniBlockServer
{
    private readonly Lock _difficultyLock = new();
    private readonly int _initialDifficulty;
    private readonly ILogger<InternalServer> _logger = Log.Instance.For<InternalServer>();
    private readonly string _worldPath;
    private TerrainLodScaleFixtureSnapshot _terrainLodScaleFixture =
        new(TerrainLodScaleFixtureStatus.Idle, 0, null);

    private int _lastDifficulty;

    public volatile bool isReady;

    public InternalServer(
        string worldPath,
        string levelName,
        WorldSettings settings,
        int viewDistance,
        int simulationDistance,
        int initialDifficulty,
        ContentRuntime content) :
        base(new InternalServerConfiguration(
            levelName, settings.TerrainType.Name, settings.Seed.ToString(), settings.GeneratorOptions,
            viewDistance, simulationDistance), content)
    {
        _worldPath = worldPath;
        logHelp = false;
        _initialDifficulty = initialDifficulty;
        _lastDifficulty = _initialDifficulty;
    }

    public void SetViewDistance(int viewDistanceChunks)
    {
        var serverConfiguration = (InternalServerConfiguration)config;
        serverConfiguration.SetViewDistance(viewDistanceChunks);
        playerManager?.SetViewDistance(viewDistanceChunks);
        RequestSimulationDistance(serverConfiguration.GetSimulationDistance(9));
    }

    public void SetSimulationDistance(int simulationDistanceChunks)
    {
        var serverConfiguration = (InternalServerConfiguration)config;
        serverConfiguration.SetSimulationDistance(simulationDistanceChunks);
        RequestSimulationDistance(simulationDistanceChunks);
    }

    public TerrainLodScaleFixtureSnapshot TerrainLodScaleFixture =>
        Volatile.Read(ref _terrainLodScaleFixture);

    /// <summary>
    ///     E2E-only preparation entry point. The Luau test capability is the sole caller; work is
    ///     serialized onto the integrated server thread before the benchmark starts.
    /// </summary>
    public bool QueueTerrainLodScaleFixture(
        int dimension,
        double centerChunkX,
        double centerChunkZ,
        int nearDistanceChunks,
        int horizonDistanceChunks)
    {
        if (dimension is not (0 or -1) || nearDistanceChunks < 0 ||
            horizonDistanceChunks is <= 0 or > 64 ||
            nearDistanceChunks >= horizonDistanceChunks)
            return false;
        var current = TerrainLodScaleFixture;
        if (current.Status is TerrainLodScaleFixtureStatus.Queued or
            TerrainLodScaleFixtureStatus.Preparing)
            return false;
        Volatile.Write(ref _terrainLodScaleFixture,
            new TerrainLodScaleFixtureSnapshot(TerrainLodScaleFixtureStatus.Queued, 0, null));
        QueueServerAction(_ =>
        {
            Volatile.Write(ref _terrainLodScaleFixture,
                new TerrainLodScaleFixtureSnapshot(
                    TerrainLodScaleFixtureStatus.Preparing, 0, null));
            try
            {
                var tiles = getWorld(dimension).PrepareTerrainLodScaleFixture(
                    centerChunkX,
                    centerChunkZ,
                    nearDistanceChunks,
                    horizonDistanceChunks);
                Volatile.Write(ref _terrainLodScaleFixture,
                    new TerrainLodScaleFixtureSnapshot(
                        TerrainLodScaleFixtureStatus.Complete, tiles, null));
                _logger.LogInformation(
                    "Prepared terrain LOD scale fixture: {Tiles} tiles around {ChunkX:F3},{ChunkZ:F3}",
                    tiles,
                    centerChunkX,
                    centerChunkZ);
            }
            catch (Exception error)
            {
                Volatile.Write(ref _terrainLodScaleFixture,
                    new TerrainLodScaleFixtureSnapshot(
                        TerrainLodScaleFixtureStatus.Failed, 0,
                        error.GetBaseException().Message));
                _logger.LogError(error, "Could not prepare terrain LOD scale fixture");
            }
        });
        return true;
    }

    protected override bool Init()
    {
        connections = new ConnectionListener(this);

        _logger.LogInformation("Starting internal server");

        var result = base.Init();

        if (result)
        {
            for (var i = 0; i < worlds.Length; ++i)
            {
                if (worlds[i] != null)
                {
                    worlds[i].SetDifficulty(_initialDifficulty);
                    worlds[i].allowSpawning(_initialDifficulty > 0, true);
                }
            }

            isReady = true;
        }

        return result;
    }

    public override FileInfo GetFile(string path) => new(Path.Combine(_worldPath, path));

    public void SetDifficulty(int difficulty)
    {
        lock (_difficultyLock)
        {
            if (_lastDifficulty != difficulty)
            {
                _lastDifficulty = difficulty;
                for (var i = 0; i < worlds.Length; ++i)
                {
                    if (worlds[i] != null)
                    {
                        worlds[i].SetDifficulty(difficulty);
                        worlds[i].allowSpawning(difficulty > 0, true);
                    }
                }

                var difficultyName = difficulty switch
                {
                    0 => "Peaceful",
                    1 => "Easy",
                    2 => "Normal",
                    3 => "Hard",
                    _ => "Unknown"
                };

                playerManager?.sendToAll(new ChatMessage
                {
                    Text = $"Difficulty set to {difficultyName}"
                });
            }
        }
    }
}
