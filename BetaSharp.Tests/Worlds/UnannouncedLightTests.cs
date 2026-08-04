using BetaSharp.Blocks;
using BetaSharp.Blocks.Entities;
using BetaSharp.Entities;
using BetaSharp.Network.Messages;
using BetaSharp.Tests.TestSupport;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Tests.Worlds;

/// <summary>
///     Whether a client ends a block placement holding the light the server holds, for the cells
///     the server never mentions.
///     <para>
///         The server announces a position when its block changes or when its own light changes.
///         A cell whose light is the same before and after satisfies neither, so nothing about it
///         goes on the wire. That leaves two writers into the client's light with nothing
///         arbitrating them: the wire for announced cells, and the client's own
///         <see cref="LightingEngine" /> — run over the block change it just applied — for the
///         rest.
///     </para>
///     <para>
///         Both of these pass. The arrangement is unsound in principle and sound for this case:
///         the client's propagation reaches the same answer the server did, so nothing is left for
///         the wire to correct. That is a fact about sky light above a placed block, not a
///         guarantee, and it is worth pinning because it narrows where a real divergence can come
///         from — not from the announce rule, on this path.
///     </para>
/// </summary>
public sealed class UnannouncedLightTests
{
    private const int PlacedY = 64;

    /// <summary>
    ///     Everything within a chunk of the placement. A light update refuses to touch a cell whose
    ///     chunk lacks a full neighbor ring, so a world of one chunk lights nothing and would make
    ///     any assertion here pass for the wrong reason.
    /// </summary>
    private static readonly (int X, int Z)[] s_neighborhood =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0), (0, 0), (1, 0),
        (-1, 1), (0, 1), (1, 1)
    ];

    /// <summary>
    ///     The cell directly above a block placed under open sky. Its sky light is 15 before the
    ///     placement and 15 after — the new block is below it and shadows nothing — so the server
    ///     has no reason to announce it, and a client that darkens it is never corrected.
    /// </summary>
    [Fact]
    public void The_cell_above_a_placed_block_keeps_the_sky_light_the_server_holds()
    {
        LightTestWorld server = Lit();
        LightTestWorld client = Lit();

        Assert.Equal(15, server.SkyLightAt(8, PlacedY + 1, 8));
        Assert.Equal(15, client.SkyLightAt(8, PlacedY + 1, 8));

        // Attached after the setup, so what it holds is the placement's announcements alone.
        RecordingListener announced = new();
        server.Broadcaster.AddWorldAccess(announced);

        server.Writer.SetBlock(8, PlacedY, 8, BlockRegistry.Get("stone").Id, 0);
        server.DrainLighting();

        Replay(announced, server, client);

        Assert.Equal(server.SkyLightAt(8, PlacedY + 1, 8), client.SkyLightAt(8, PlacedY + 1, 8));
    }

    /// <summary>
    ///     The cell below, which the placement does shadow. Its light changes on the server, so it
    ///     is announced and the wire carries it. Here to prove the replay works at all, so a
    ///     failure above is about what was never sent rather than about the harness.
    /// </summary>
    [Fact]
    public void The_cell_below_a_placed_block_is_announced_and_arrives()
    {
        LightTestWorld server = Lit();
        LightTestWorld client = Lit();

        RecordingListener announced = new();
        server.Broadcaster.AddWorldAccess(announced);

        server.Writer.SetBlock(8, PlacedY, 8, BlockRegistry.Get("stone").Id, 0);
        server.DrainLighting();

        Assert.Contains((8, PlacedY - 1, 8), announced.BlockUpdates);

        Replay(announced, server, client);

        Assert.Equal(server.SkyLightAt(8, PlacedY - 1, 8), client.SkyLightAt(8, PlacedY - 1, 8));
    }

    /// <summary>An all-air neighborhood under open sky, lit and settled.</summary>
    private static LightTestWorld Lit()
    {
        LightTestWorld world = new();

        foreach ((int x, int z) in s_neighborhood)
        {
            world.Chunks.Add(x, z);
        }

        world.DrainLighting();
        return world;
    }

    /// <summary>
    ///     Applies what the server puts on the wire for each announced position, through the wire
    ///     form rather than around it, and in the order <c>ClientWorld.SetBlockWithMetaFromPacket</c>
    ///     uses: the block first, then the light over the top of whatever that queued.
    /// </summary>
    /// <remarks>
    ///     Light is read from the server after its queue has drained, which is the best content the
    ///     wire could ever carry — the real server reads it at send time and can read it
    ///     mid-propagation. A failure here is therefore about cells the wire never names, not about
    ///     a stale byte for one it does.
    /// </remarks>
    private static void Replay(RecordingListener announced, LightTestWorld server, LightTestWorld client)
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

            client.Writer.SetBlockWithoutNotifyingNeighbors(
                received.X, received.Y, received.Z, received.BlockRawId, received.BlockMetadata);

            client.BlockHost.GetChunk(received.X >> 4, received.Z >> 4)
                .SetPackedLight(received.X & 15, received.Y, received.Z & 15, received.Light);
        }

        client.DrainLighting();
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
}
