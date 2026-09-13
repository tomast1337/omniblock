using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OmniBlock.Diagnostics;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;
using OmniBlock.Profiling;
using OmniBlock.Registries;
using OmniBlock.Server.Command;
using OmniBlock.Server.Entities;
using OmniBlock.Server.Internal;
using OmniBlock.Server.Network;
using OmniBlock.Server.Worlds;
using OmniBlock.Util;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Storage;
using Silk.NET.Maths;
using ServerWorld = OmniBlock.Worlds.Core.ServerWorld;

namespace OmniBlock.Server;

public abstract class OmniBlockServer : ICommandOutput
{
    private readonly ILogger<OmniBlockServer> _logger = Log.Instance.For<OmniBlockServer>();
    private readonly Queue<PendingCommand> _pendingCommands = new();
    private readonly object _pendingCommandsLock = new();

    private readonly List<IRegistryReloadListener> _reloadListeners = [];
    private readonly object _tpsLock = new();
    private long _accumulatedTime;

    /// <summary>
    ///     The value last broadcast, so a fixed tick with no simulation between it and the
    ///     previous one does not re-announce a stamp the client already has.
    /// </summary>
    private long _broadcastSimulationTimeMs;

    private ServerCommandHandler _commandHandler;
    private float _currentTps;

    private volatile bool _isPaused;
    private long _lastTpsTime;
    private ContentRuntime? _pendingContent;

    /// <summary>
    ///     <see cref="MonotonicClock" /> reading at the start of the most recent simulation tick.
    ///     This is the instant that every entity position set during that tick describes, and it is
    ///     what <see cref="TickStampMessage" /> carries — see that message for why it is not the
    ///     send time.
    /// </summary>
    private long _simulationTimeMs;

    private long _tickLength = 50L;
    private int _ticks;
    private int _ticksThisSecond;
    private volatile int _pendingSimulationDistance = -1;
    public IServerConfiguration config;
    public ConnectionListener connections;
    public EntityTracker[] entityTrackers = new EntityTracker[2];
    public bool flightEnabled;

    public Dictionary<string, int> GIVE_COMMANDS_COOLDOWNS = [];
    protected bool logHelp = true;
    public bool onlineMode;
    public PlayerManager playerManager;
    public int progress;
    public string? progressMessage;
    public bool pvpEnabled;
    public bool running = true;
    public bool spawnAnimals;
    public bool stopped;
    public ServerWorld[] worlds;

    protected OmniBlockServer(IServerConfiguration config, ContentRuntime content)
    {
        this.config = config;
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public ContentRuntime Content { get; private set; }
    public int SimulationDistance { get; private set; } = 9;
    public int RenderDistance => Math.Clamp(config.GetViewDistance(10), 4, 32);
    public RegistryAccess RegistryAccess { get; set; } = RegistryAccess.Empty;

    /// <summary>
    ///     The extensible message table this server advertises. One instance for the whole server,
    ///     not one per connection: the advertised ordering is derived from the registered key set,
    ///     so it is identical for every client and is frozen once, at startup.
    ///     <para>
    ///         Content registers into this before <see cref="Init" /> completes. After negotiation
    ///         it is immutable — <see cref="MessageRegistry.Register" /> throws — because the table
    ///         has by then been promised to connecting clients.
    ///     </para>
    /// </summary>
    public MessageRegistry Messages { get; } = new();

    public Holder<GameMode> DefaultGameMode { get; set; } = new(new GameMode());

    /// <summary>
    ///     The same instant, for anything that has to place a client's statement about the past on
    ///     the server's own timeline — <see cref="EntityPositionHistory" /> and the rewind that reads
    ///     it. Exposed rather than re-read from the clock, because a fresh reading taken while
    ///     handling a packet is a different instant from the one the positions describe.
    /// </summary>
    public long SimulationTimeMs => _simulationTimeMs;

    public float Tps
    {
        get
        {
            lock (_tpsLock)
            {
                return _currentTps;
            }
        }
    }

    public int TickRate
    {
        get => 1000 / (int)_tickLength;
        set
        {
            _tickLength = 1000 / value;
            _accumulatedTime %= _tickLength;
        }
    }

    public bool Paused
    {
        get => _isPaused;
        set => _isPaused = value;
    }

    public void SendMessage(string message) => _logger.LogInformation(message);

    public string Name => "CONSOLE";
    public byte PermissionLevel => 255;

    protected virtual bool Init()
    {
        _commandHandler = new ServerCommandHandler(this);

        RegisterReloadListener(new DefaultGameModeListener(this));
        RegisterReloadListener(new ProcessReloadListener(this));

        // Freeze the message table before the listener accepts anyone. Every client is told this
        // ordering during configuration, so it must not be able to change afterwards. Mods register
        // between here and RegisterAll; ordering does not matter, since IDs come from the sorted key
        // set rather than from registration sequence.
        DefaultMessages.RegisterAll(Messages, Content.Items);
        Messages.NegotiateAsServer();

        onlineMode = config.GetOnlineMode(true);
        spawnAnimals = config.GetSpawnAnimals(true);
        pvpEnabled = config.GetPvpEnabled(true);
        flightEnabled = config.GetAllowFlight(false);

        playerManager = CreatePlayerManager();
        entityTrackers[0] = new EntityTracker(this, 0);
        entityTrackers[1] = new EntityTracker(this, -1);

        var startupSw = Stopwatch.StartNew();

        var worldName = config.GetLevelName("world");
        var seedString = config.GetLevelSeed("");
        var seed = Random.Shared.NextInt64();

        if (!string.IsNullOrEmpty(seedString))
        {
            if (!long.TryParse(seedString, out seed))
            {
                // Java-compatible String.hashCode() behavior
                var hash = 0;
                foreach (var c in seedString)
                {
                    hash = 31 * hash + c;
                }

                seed = hash;
            }
        }

        var typeString = config.GetLevelType("DEFAULT");
        var worldType = Content.WorldTypes.TryGet(typeString, out var configuredWorldType)
            ? configuredWorldType
            : Content.WorldTypes.Get("omniblock:default");
        var optionsString = config.GetLevelOptions("");

        _logger.LogInformation("Preparing level \"{WorldName}\"", worldName);
        loadWorld(worldName, new WorldSettings(seed, worldType, optionsString));

        foreach (var listener in _reloadListeners)
        {
            listener.OnRegistriesRebuilt(RegistryAccess);
        }

        CommitPendingContent();

        if (logHelp)
        {
            _logger.LogInformation(
                "Done ({ElapsedMs}ms)! For help, type \"help\" or \"?\"",
                startupSw.ElapsedMilliseconds);
        }

        return true;
    }

    private void loadWorld(string worldDir, WorldSettings settings)
    {
        worlds = new ServerWorld[2];
        SimulationDistance = GetEffectiveSimulationDistance(
            config.GetSimulationDistance(9), config.GetViewDistance(10));
        var dir = new DirectoryInfo(Path.Combine(GetFile(".").FullName, worldDir));
        RegionWorldStorage worldStorage = new(dir, true);
        RegistryAccess = RegistryAccess.WithWorldDatapacks(dir.FullName);

        for (var i = 0; i < worlds.Length; i++)
        {
            if (i == 0)
            {
                worlds[i] = new ServerWorld(this, worldStorage, worldDir, 0, settings, null, Content);
            }
            else
            {
                worlds[i] = new ReadOnlyServerWorld(this, worldStorage, worldDir, -1, settings, worlds[0]);
            }

            worlds[i].EventListeners.Add(new ServerWorldEventListener(this, worlds[i]));
            worlds[i].SetDifficulty(config.GetSpawnMonsters(true) ? 1 : 0);
            worlds[i].allowSpawning(config.GetSpawnMonsters(true), spawnAnimals);
            worlds[i].SetSimulationDistance(SimulationDistance);
            playerManager.saveAllPlayers(worlds);
        }

        var startRegionSize = config.GetSpawnRegionSize(196);
        var lastTimeLogged = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (var i = 0; i < worlds.Length; i++)
        {
            _logger.LogInformation("Preparing start region for level {Level}", i);

            // Only pre-generate the overworld spawn region. The nether is only accessible
            // via portal (which implies a teleport/load anyway), so on-demand generation
            // there is fine and avoids the 40+ second lava-sea light propagation cost.
            if (i == 0)
            {
                var world = worlds[i];
                var spawnPos = world.Properties.GetSpawnPos();

                var chunkList = new List<Vector2D<int>>();
                for (var x = -startRegionSize; x <= startRegionSize; x += 16)
                {
                    for (var z = -startRegionSize; z <= startRegionSize; z += 16)
                    {
                        chunkList.Add(new Vector2D<int>((spawnPos.X + x) >> 4, (spawnPos.Z + z) >> 4));
                    }
                }

                var totalChunks = chunkList.Count;
                var preGenerated = new Chunk[totalChunks];

                // Terrain, in parallel. Generation reads no neighbours, so it is the only stage
                // that can be.
                var sw1 = Stopwatch.StartNew();
                var threadLocalGen = new ThreadLocal<IChunkSource>(world.ChunkCache.CreateParallelGenerator, false);
                Parallel.For(0, totalChunks, idx =>
                {
                    if (!running)
                    {
                        return;
                    }

                    var chunkPos = chunkList[idx];
                    preGenerated[idx] = world.ChunkCache.GenerationTelemetry.Measure(
                        WorldGenerationStage.Terrain,
                        () => threadLocalGen.Value!.GetChunk(chunkPos.X, chunkPos.Y));
                });

                threadLocalGen.Dispose();
                sw1.Stop();
                _logger.LogInformation("  Level {Level} terrain: {ElapsedMs}ms", i, sw1.ElapsedMilliseconds);

                // Insert before decorating, all of them: decoration writes into neighbours, and a
                // neighbour not yet inserted reads back as EmptyChunk.
                var sw2 = Stopwatch.StartNew();
                for (var idx = 0; idx < totalChunks && running; idx++)
                {
                    var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (currentTime > lastTimeLogged + 1000L)
                    {
                        logProgress(Translations.Get("loading.preparingSpawnArea"), (idx + 1) * 100 / totalChunks);
                        lastTimeLogged = currentTime;
                    }

                    var chunkPos = chunkList[idx];
                    world.ChunkCache.InsertPreGeneratedChunk(chunkPos.X, chunkPos.Y, preGenerated[idx]);
                    world.ChunkCache.DecorateIfReady(chunkPos.X, chunkPos.Y);
                }

                sw2.Stop();
                _logger.LogInformation("  Level {Level} decoration: {ElapsedMs}ms", i, sw2.ElapsedMilliseconds);

                // Lighting last, in one drain. Every neighbour is loaded by now, so sky light
                // propagates across borders once instead of being re-queued at each edge.
                var sw3 = Stopwatch.StartNew();
                world.ChunkCache.GenerationTelemetry.Measure(WorldGenerationStage.LightPropagation, () =>
                {
                    while (world.Lighting.DoLightingUpdates() && running)
                    {
                    }
                });

                sw3.Stop();
                _logger.LogInformation("  Level {Level} lighting: {ElapsedMs}ms", i, sw3.ElapsedMilliseconds);
            }
        }

        clearProgress();
    }

    private void logProgress(string progressType, int progress)
    {
        progressMessage = progressType;
        this.progress = progress;
        _logger.LogInformation("{ProgressType}: {Progress}%", progressType, progress);
    }

    private void clearProgress()
    {
        progressMessage = null;
        progress = 0;
    }

    private void saveWorlds()
    {
        _logger.LogInformation("Saving chunks");

        foreach (var world in worlds)
        {
            world.SaveWithLoadingDisplay(true, null);
            world.forceSave();
        }
    }

    private void shutdown()
    {
        if (stopped)
        {
            return;
        }

        _logger.LogInformation("Stopping server");

        // Before saving, so no player is accepted into a world that is mid-save. The stream
        // listener leaked its socket here; a UDP transport holds a bound port and a receive thread,
        // and leaving those behind makes a restart on the same port fail.
        connections?.StopAsync().GetAwaiter().GetResult();

        playerManager?.savePlayers();

        foreach (var world in worlds)
        {
            if (world != null)
            {
                saveWorlds();
                break;
            }
        }

        if (this is InternalServer)
        {
            RegistryAccess = RegistryAccess.WithoutWorldDatapacks();
        }
    }

    public void Stop() => running = false;

    public void RunThreaded(string threadName)
    {
        Thread thread = new(run)
        {
            Name = threadName
        };
        thread.Start();
    }

    private void run()
    {
        Profiler.RegisterServerThread();
        try
        {
            if (Init())
            {
                var lastTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var accumulatedFixedTime = 0L;
                _lastTpsTime = lastTime;
                _ticksThisSecond = 0;
                var tickStopwatch = new Stopwatch();
                var fixedTickStopwatch = new Stopwatch();

                while (running)
                {
                    var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var tickLength = currentTime - lastTime;
                    if (tickLength > 2000L)
                    {
                        _logger.LogWarning("Can't keep up! Did the system time change, or is the server overloaded?");
                        tickLength = 2000L;
                    }

                    if (tickLength < 0L)
                    {
                        _logger.LogWarning("Time ran backwards! Did the system time change?");
                        tickLength = 0L;
                    }

                    _accumulatedTime += tickLength;
                    accumulatedFixedTime += tickLength;
                    lastTime = currentTime;

                    if (accumulatedFixedTime >= 50L)
                    {
                        accumulatedFixedTime -= 50L;
                        fixedTickStopwatch.Restart();
                        TickFixed();
                        fixedTickStopwatch.Stop();
                        MetricRegistry.Set(ServerMetrics.Mspft, (float)fixedTickStopwatch.Elapsed.TotalMilliseconds);
                    }

                    if (_isPaused)
                    {
                        _accumulatedTime = 0L;
                        lock (_tpsLock)
                        {
                            _currentTps = 0.0f;
                        }

                        MetricRegistry.Set(ServerMetrics.Tps, 0.0f);
                        Thread.Sleep(50);
                        continue;
                    }

                    while (_accumulatedTime >= _tickLength && running)
                    {
                        _accumulatedTime -= _tickLength;
                        tickStopwatch.Restart();
                        Tick();
                        tickStopwatch.Stop();
                        MetricRegistry.Set(ServerMetrics.Mspt, (float)tickStopwatch.Elapsed.TotalMilliseconds);
                        _ticksThisSecond++;
                    }

                    var tpsNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var tpsElapsed = tpsNow - _lastTpsTime;
                    if (tpsElapsed >= 1000L)
                    {
                        lock (_tpsLock)
                        {
                            _currentTps = _ticksThisSecond * 1000.0f / tpsElapsed;
                        }

                        _ticksThisSecond = 0;
                        _lastTpsTime = tpsNow;
                        var playerCount = playerManager.players.Count;
                        var entityCount = worlds[0].Entities.Entities.Count;

                        MetricRegistry.Set(ServerMetrics.Tps, _currentTps);
                        MetricRegistry.Set(ServerMetrics.PlayerCount, playerCount);
                        MetricRegistry.Set(ServerMetrics.EntityCount, entityCount);

                        // The same numbers, pushed to anyone who is not in this process. Here rather
                        // than on its own timer because this is already the once-a-second point where
                        // they are recomputed, and a second timer would either duplicate that work or
                        // read values from a moment it did not choose.
                        playerManager.sendToAll(new ServerStatusMessage
                        {
                            Tps = _currentTps,
                            Mspt = MetricRegistry.Get(ServerMetrics.Mspt),
                            EntityCount = entityCount,
                            PlayerCount = playerCount
                        });
                    }

                    Thread.Sleep(1);
                }
            }
            else
            {
                while (running)
                {
                    RunPendingCommands();

                    try
                    {
                        Thread.Sleep(10);
                    }
                    catch (ThreadInterruptedException ex)
                    {
                        _logger.LogWarning(ex, "Server thread interrupted while idle.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception");

            while (running)
            {
                RunPendingCommands();

                try
                {
                    Thread.Sleep(10);
                }
                catch (ThreadInterruptedException interruptedEx)
                {
                    _logger.LogWarning(interruptedEx, "Server thread interrupted after failure.");
                }
            }
        }
        finally
        {
            try
            {
                shutdown();
                stopped = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception during shutdown.");
            }
            finally
            {
                if (this is not InternalServer)
                {
                    Environment.Exit(0);
                }
            }
        }
    }

    private void TickFixed()
    {
        // Snapshot keys to allow safe mutation during iteration.
        var keysSnapshot = new List<string>(GIVE_COMMANDS_COOLDOWNS.Keys);
        foreach (var key in keysSnapshot)
        {
            if (GIVE_COMMANDS_COOLDOWNS.TryGetValue(key, out var cooldown))
            {
                if (cooldown > 0)
                    GIVE_COMMANDS_COOLDOWNS[key] = cooldown - 1;
                else
                    GIVE_COMMANDS_COOLDOWNS.Remove(key);
            }
        }

        try
        {
            RunPendingCommands();
        }
        catch (Exception e)
        {
            _logger.LogWarning($"Unexpected exception while parsing console command: {e}");
        }

        connections.Tick();
        playerManager.updateAllChunks();
        playerManager.flushPendingChunkUpdates();

        // Ahead of the tracker, so that TCP's ordering guarantee makes the stamp cover every entity
        // update that follows it. Skipped when no simulation tick has run since the last broadcast:
        // Tick and TickFixed are driven by separate accumulators, so they do not interleave 1:1.
        if (_simulationTimeMs != _broadcastSimulationTimeMs)
        {
            _broadcastSimulationTimeMs = _simulationTimeMs;

            var stamp = OmniMessagePacket.For(
                Messages, new TickStampMessage
                {
                    ServerTimeMs = _simulationTimeMs
                });

            if (stamp is not null)
            {
                playerManager.sendToAll(stamp);
            }
        }

        foreach (var t in entityTrackers)
        {
            t.tick();
        }
    }

    public void Tick()
    {
        ApplyPendingSimulationDistance();
        CommitPendingContent();
        _ticks++;

        // Captured before anything moves. Every position written during this call describes this
        // instant, whenever the tracker gets around to broadcasting it.
        _simulationTimeMs = MonotonicClock.NowMs();

        for (var i = 0; i < worlds.Length; i++)
        {
            if (i == 0 || config.GetAllowNether(true))
            {
                var world = worlds[i];
                if (_ticks % 20 == 0)
                {
                    playerManager.sendToDimension(new WorldTimeUpdateMessage
                    {
                        Time = world.GetTime()
                    }, world.Dimension.Id);
                }

                world.Tick();

                // Cap lighting updates to avoid spending the entire tick (and beyond)
                // draining the queue. The nether's lava seas can generate thousands
                // of lighting entries per tick; processing them all in one go causes
                // >2-second stalls and "Can't keep up" spam. Any remaining work
                // carries over and is processed across subsequent ticks.
                if (world.Lighting.PendingUpdateCount > 0)
                    world.ChunkCache.GenerationTelemetry.Measure(WorldGenerationStage.LightPropagation, () =>
                {
                    var lightSw = Stopwatch.StartNew();
                    while (lightSw.ElapsedMilliseconds < 15L && world.Lighting.DoLightingUpdates())
                    {
                    }
                });

                world.Entities.TickEntities();
            }
        }
    }

    public void RequestSimulationDistance(int chunks) =>
        _pendingSimulationDistance = Math.Clamp(chunks, 2, 32);

    private void ApplyPendingSimulationDistance()
    {
        var requested = Interlocked.Exchange(ref _pendingSimulationDistance, -1);
        if (requested < 0) return;

        SimulationDistance = GetEffectiveSimulationDistance(requested, config.GetViewDistance(10));
        if (worlds == null) return;
        foreach (var world in worlds)
            world?.SetSimulationDistance(SimulationDistance);

        if (playerManager != null)
            playerManager.sendToAll(new SessionDistanceMessage
            {
                RenderDistance = RenderDistance,
                SimulationDistance = SimulationDistance
            });
    }

    internal static int GetEffectiveSimulationDistance(int requested, int renderDistance) =>
        Math.Min(Math.Clamp(requested, 2, 32), Math.Clamp(renderDistance, 4, 32));

    internal void StageContent(ContentRuntime candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Volatile.Write(ref _pendingContent, candidate);
    }

    private void CommitPendingContent()
    {
        var candidate = Interlocked.Exchange(ref _pendingContent, null);
        if (candidate is null) return;
        Content = candidate;
        if (worlds is null) return;
        foreach (var world in worlds)
            world?.ReplaceContent(candidate);
    }

    public void QueueCommands(string str, ICommandOutput cmd)
    {
        lock (_pendingCommandsLock)
        {
            _pendingCommands.Enqueue(new PendingCommand(str, cmd));
        }
    }

    private void RunPendingCommands()
    {
        while (true)
        {
            PendingCommand cmd;
            lock (_pendingCommandsLock)
            {
                if (_pendingCommands.Count == 0) break;
                cmd = _pendingCommands.Dequeue();
            }

            _commandHandler.ExecuteCommand(cmd);
        }
    }

    public abstract FileInfo GetFile(string path);

    public void Warn(string message) => _logger.LogWarning(message);

    public ServerWorld getWorld(int dimensionId) => dimensionId == -1 ? worlds[1] : worlds[0];

    public EntityTracker getEntityTracker(int dimensionId) => dimensionId == -1 ? entityTrackers[1] : entityTrackers[0];

    protected virtual PlayerManager CreatePlayerManager() => new(this);

    /// <summary>
    ///     Registers a listener that will be notified whenever datapacks are reloaded.
    ///     Use this to refresh any data cached from registry lookups.
    /// </summary>
    public void RegisterReloadListener(IRegistryReloadListener listener)
        => _reloadListeners.Add(listener);

    /// <summary>
    ///     Sends all reloadable registry data messages followed by <see cref="FinishConfigurationMessage" />
    /// </summary>
    public void SendConfigurationTo(Action<Packet> send)
    {
        // First: it establishes how every later message is identified, so nothing name-keyed can be
        // sent before the client holds it. Dropped for non-OmniBlock clients by sendPacket, since
        // it is an ExtendedProtocolPacket.
        send(MessageRegistrySyncS2CPacket.Get(Messages.NegotiatedOrder, Content.Manifest));

        foreach (var message in RegistryAccess.BuildSyncMessages())
        {
            send(OmniMessagePacket.For(Messages, message)!);
        }

        send(OmniMessagePacket.For(Messages, new SessionDistanceMessage
        {
            RenderDistance = config.GetViewDistance(10),
            SimulationDistance = SimulationDistance
        })!);

        send(OmniMessagePacket.For(Messages, new FinishConfigurationMessage())!);
    }

    /// <summary>
    ///     Reloads all data-driven content from disk. Re-reads base assets, global datapacks, and
    ///     world datapacks, then broadcasts a status message to all connected players.
    /// </summary>
    public void ReloadDatapacks()
    {
        _logger.LogInformation("Reloading datapacks...");
        playerManager.sendToAll(new ChatMessage
        {
            Text = "§eReloading datapacks..."
        });
        try
        {
            var candidateRegistries = RegistryAccess.Rebuild();
            foreach (var listener in _reloadListeners)
                listener.OnRegistriesRebuilt(candidateRegistries);

            RegistryReloadPipeline.SyncToPlayers(candidateRegistries, _reloadListeners, playerManager.players);
            RegistryAccess = candidateRegistries;

            _logger.LogInformation("Datapacks reloaded.");
            playerManager.sendToAll(new ChatMessage
            {
                Text = "§aDatapacks reloaded."
            });
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _pendingContent, null);
            _logger.LogError("Datapack reload failed: {Message}.", ex.Message);

            if (this is InternalServer)
            {
                playerManager.sendToAll(new ChatMessage
                {
                    Text = "§cReload failed! See console for details."
                });
            }
        }
    }
}
