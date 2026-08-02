using BetaSharp.Blocks.Entities;
using BetaSharp.Entities;
using BetaSharp.Network.Messages;
using BetaSharp.Server.Worlds;
using BetaSharp.Worlds;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core;
using BetaSharp.Worlds.Core.Systems;
using BetaSharp.Worlds.Dimensions;
using BetaSharp.Worlds.Storage;
using BetaSharp.Worlds.Storage.RegionFormat;

namespace BetaSharp.Tests.Worlds;

/// <summary>
///     Whether a light change computed on the server survives the trip to a client.
///     <para>
///         Light is sent in full inside chunk data and never again. Every later change has to reach
///         the client some other way, and a client that misses one keeps the stale value until the
///         chunk is resent, which for a loaded chunk is never.
///     </para>
/// </summary>
public sealed class LightUpdateDeliveryTests
{
    /// <summary>
    ///     The case with no accompanying block change, which is the one the client cannot derive for
    ///     itself. A client recomputes light whenever it applies a block change, so a torch being
    ///     placed repairs itself; light arriving from a neighboring chunk, or from a change the
    ///     client never saw, does not.
    /// </summary>
    [Fact]
    public void A_light_change_with_no_block_change_reaches_the_client()
    {
        TestWorld server = new();
        RecordingListener announced = new();
        server.Broadcaster.AddWorldAccess(announced);
        server.BlockHost.GetChunk(0, 0);

        server.Lighting.SetLight(LightType.Block, 8, 64, 8, 12);

        Assert.Contains((8, 64, 8), announced.BlockUpdates);

        TestWorld client = new();
        client.BlockHost.GetChunk(0, 0);
        Replay(announced, server, client);

        Assert.Equal(
            server.Lighting.GetBrightness(LightType.Block, 8, 64, 8),
            client.Lighting.GetBrightness(LightType.Block, 8, 64, 8));
    }

    /// <summary>
    ///     The same guarantee for the batched form. Which of the three shapes the server picks
    ///     depends only on how many cells in one chunk changed in one tick, so a light change that
    ///     survives one shape and not another is the intermittent failure this whole file exists
    ///     for.
    /// </summary>
    [Fact]
    public void A_chunk_delta_carries_the_light_for_every_position_it_names()
    {
        ChunkDeltaUpdateMessage sent = new()
        {
            X = 3,
            Z = -2,
            Positions = [0x0000, 0x1234],
            BlockRawIds = [1, 2],
            BlockMetadata = [0, 5],
            Light = [0x0F, 0xF0]
        };

        using MemoryStream buffer = new();
        sent.Write(buffer);

        Assert.Equal(buffer.Length, sent.Size());

        buffer.Position = 0;
        ChunkDeltaUpdateMessage received = new();
        received.Read(buffer);

        Assert.Equal(buffer.Length, buffer.Position);
        Assert.Equal(sent.Light, received.Light);
    }

    /// <summary>
    ///     Applies what the server puts on the wire for each announced position, through the wire
    ///     form rather than around it, so the test fails if the message cannot carry what the
    ///     receiver needs.
    /// </summary>
    private static void Replay(RecordingListener announced, TestWorld server, TestWorld client)
    {
        foreach ((int x, int y, int z) in announced.BlockUpdates.Distinct())
        {
            BlockUpdateMessage update = new()
            {
                X = x,
                Y = (sbyte)y,
                Z = z,
                BlockRawId = (byte)server.Reader.GetBlockId(x, y, z),
                BlockMetadata = (byte)server.Reader.GetBlockMeta(x, y, z),
                Light = server.BlockHost.GetChunk(x >> 4, z >> 4).GetPackedLight(x & 15, y, z & 15)
            };

            using MemoryStream buffer = new();
            update.Write(buffer);
            buffer.Position = 0;

            BlockUpdateMessage received = new();
            received.Read(buffer);

            Chunk target = client.BlockHost.GetChunk(received.X >> 4, received.Z >> 4);
            target.SetBlock(received.X & 15, received.Y, received.Z & 15, received.BlockRawId, received.BlockMetadata);
            target.SetPackedLight(received.X & 15, received.Y, received.Z & 15, received.Light);
        }

        while (client.Lighting.DoLightingUpdates())
        {
        }
    }

    private sealed class RecordingListener : IWorldEventListener
    {
        public List<(int X, int Y, int Z)> BlockUpdates { get; } = [];

        public void BlockUpdate(int x, int y, int z) => BlockUpdates.Add((x, y, z));

        public void SetBlocksDirty(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        {
        }

        public void PlaySound(string soundName, double x, double y, double z, float volume, float pitch)
        {
        }

        public void SpawnParticle(string particleName, double x, double y, double z, double velocityX, double velocityY, double velocityZ)
        {
        }

        public void NotifyEntityAdded(Entity entity)
        {
        }

        public void NotifyEntityRemoved(Entity entity)
        {
        }

        public void NotifyAmbientDarknessChanged()
        {
        }

        public void PlayNote(int x, int y, int z, int soundType, int pitch)
        {
        }

        public void PlayStreaming(string trackName, int x, int y, int z)
        {
        }

        public void UpdateBlockEntity(int x, int y, int z, BlockEntity blockEntity)
        {
        }

        public void WorldEvent(EntityPlayer? player, int @event, int x, int y, int z, int data)
        {
        }

        public void BroadcastEntityEvent(Entity entity, byte @event)
        {
        }
    }

    private sealed class TestWorld : World
    {
        public TestWorld() : base(new NoStorage(), "light", new WorldSettings(0L, WorldType.Default))
        {
        }

        protected override IChunkSource CreateChunkCache() => new InMemoryChunkSource(this);
    }

    private sealed class InMemoryChunkSource(World world) : IChunkSource
    {
        private readonly Dictionary<(int X, int Z), Chunk> _chunks = [];

        public bool IsChunkLoaded(int x, int z) => true;

        public Chunk GetChunk(int x, int z)
        {
            if (_chunks.TryGetValue((x, z), out Chunk? chunk))
            {
                return chunk;
            }

            chunk = new Chunk(world, x, z)
            {
                Blocks = new byte[ChuckFormat.ChunkSize],
                Meta = new ChunkNibbleArray(ChuckFormat.ChunkSize),
                SkyLight = new ChunkNibbleArray(ChuckFormat.ChunkSize),
                BlockLight = new ChunkNibbleArray(ChuckFormat.ChunkSize)
            };

            _chunks[(x, z)] = chunk;
            return chunk;
        }

        public Chunk LoadChunk(int x, int z) => GetChunk(x, z);

        public void DecorateTerrain(IChunkSource source, int x, int z)
        {
        }

        public bool Save(bool saveEntities, LoadingDisplay display) => true;
        public bool Tick() => false;
        public bool CanSave() => false;
        public string GetDebugInfo() => "InMemoryChunkSource";
    }

    private sealed class NoStorage : IWorldStorage
    {
        public WorldProperties? LoadProperties() => null;

        public void CheckSessionLock()
        {
        }

        public IChunkStorage? GetChunkStorage(Dimension dimension) => null;

        public void Save(WorldProperties properties, List<EntityPlayer> players)
        {
        }

        public void Save(WorldProperties properties)
        {
        }

        public void ForceSave()
        {
        }

        public IPlayerStorage? GetPlayerStorage() => null;
        public FileInfo? GetWorldPropertiesFile(string name) => null;
    }
}
