using Microsoft.Extensions.Logging;
using OmniBlock.Network.Messages;
using OmniBlock.Server.Network;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Server.Internal;

public class InternalServer : OmniBlockServer
{
    private readonly Lock _difficultyLock = new();
    private readonly int _initialDifficulty;
    private readonly ILogger<InternalServer> _logger = Log.Instance.For<InternalServer>();
    private readonly string _worldPath;

    private int _lastDifficulty;

    public volatile bool isReady;

    public InternalServer(string worldPath, string levelName, WorldSettings settings, int viewDistance, int initialDifficulty) :
        base(new InternalServerConfiguration(levelName, settings.TerrainType.Name, settings.Seed.ToString(), settings.GeneratorOptions, viewDistance))
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
