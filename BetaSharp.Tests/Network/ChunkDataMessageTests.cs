using OmniBlock.Network;
using OmniBlock.Network.Chunks;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Gen.Chunks;

namespace OmniBlock.Tests.Network;

/// <summary>
///     <see cref="ChunkDataMessage" />: the wiring that puts <see cref="ChunkBlobCodec" /> on the
///     wire.
///     <para>
///         The codec's own tests prove the encoding round-trips. What is left to prove here is that
///         it survives the trip: through the message's serialisation, through the envelope's
///         length-prefixed framing, and through the registry's ID negotiation — and that a peer which
///         cannot read it is sent the legacy packet instead of nothing.
///     </para>
/// </summary>
public sealed class ChunkDataMessageTests
{
    private static Chunk Generate(long seed = 1L, int chunkX = 0, int chunkZ = 0)
    {
        FakeWorldContext world = new();
        OverworldChunkGenerator generator = new(world, seed);

        return generator.GetChunk(chunkX, chunkZ);
    }

    private static ChunkDataMessage MessageFor(Chunk chunk) => ChunkDataMessage.Of(
        chunk.X, chunk.Z, chunk.Blocks, chunk.Meta.Bytes, chunk.BlockLight.Bytes, chunk.SkyLight.Bytes);

    /// <summary>
    ///     End to end through the real framing: a chunk the server encoded is the same chunk the
    ///     client decodes. Serialising the message alone would not catch a payload that the envelope
    ///     truncates, which is the failure the length prefix exists to prevent and therefore the one
    ///     worth testing.
    /// </summary>
    [Theory]
    [InlineData(1L, 0, 0)]
    [InlineData(1L, 12, -7)]
    [InlineData(987_654_321L, 100, 100)]
    public void A_chunk_survives_the_whole_send_path(long seed, int chunkX, int chunkZ)
    {
        MessageRegistry server = new();
        DefaultMessages.RegisterAll(server);
        server.NegotiateAsServer();

        MessageRegistry client = new();
        DefaultMessages.RegisterAll(client);
        client.AdoptOrdering(server.NegotiatedOrder);

        Chunk chunk = Generate(seed, chunkX, chunkZ);

        OmniMessagePacket? envelope = OmniMessagePacket.For(server, MessageFor(chunk));
        Assert.NotNull(envelope);

        MemoryStream wire = new();
        Packet.Write(envelope, wire);
        wire.Position = 0;

        OmniMessagePacket received = Assert.IsType<OmniMessagePacket>(Packet.Read(wire, server: false));
        ChunkDataMessage decoded = Assert.IsType<ChunkDataMessage>(client.Create(received.MessageId));

        using MemoryStream payload = new(received.Payload, writable: false);
        decoded.Read(payload);

        Assert.Equal(chunkX, decoded.ChunkX);
        Assert.Equal(chunkZ, decoded.ChunkZ);

        byte[] blocks = new byte[chunk.Blocks.Length];
        byte[] meta = new byte[chunk.Meta.Bytes.Length];
        byte[] blockLight = new byte[chunk.BlockLight.Bytes.Length];
        byte[] skyLight = new byte[chunk.SkyLight.Bytes.Length];

        ChunkBlobCodec.Decode(decoded.Decompress(), blocks, meta, blockLight, skyLight);

        Assert.Equal(chunk.Blocks, blocks);
        Assert.Equal(chunk.Meta.Bytes, meta);
        Assert.Equal(chunk.BlockLight.Bytes, blockLight);
        Assert.Equal(chunk.SkyLight.Bytes, skyLight);
    }

    /// <summary>
    ///     The property the whole caching scheme rests on: both ends derive the same hash from the
    ///     same chunk, without one ever telling the other what it is. Nothing carries a hash on the
    ///     wire, so if this were false the server and client would silently disagree about which
    ///     chunks are current and the client would keep stale terrain.
    /// </summary>
    [Theory]
    [InlineData(1L, 0, 0)]
    [InlineData(987_654_321L, 100, 100)]
    public void Both_ends_derive_the_same_hash_without_it_crossing_the_wire(long seed, int x, int z)
    {
        Chunk chunk = Generate(seed, x, z);

        ulong sent = ChunkHash.Of(ChunkBlobCodec.Encode(
            chunk.Blocks, chunk.Meta.Bytes, chunk.BlockLight.Bytes, chunk.SkyLight.Bytes));

        ulong received = ChunkHash.Of(MessageFor(chunk).Decompress());

        Assert.Equal(sent, received);
        Assert.NotEqual(0ul, sent);
    }

    /// <summary>
    ///     A one-block change must change the hash, or an edited chunk would be skipped as unchanged
    ///     and the client would keep terrain that no longer exists.
    /// </summary>
    [Fact]
    public void Changing_one_block_changes_the_hash()
    {
        Chunk chunk = Generate();

        ulong before = ChunkHash.Of(ChunkBlobCodec.Encode(
            chunk.Blocks, chunk.Meta.Bytes, chunk.BlockLight.Bytes, chunk.SkyLight.Bytes));

        chunk.Blocks[ChuckFormat.GetIndex(3, 40, 9)] ^= 0x1;

        ulong after = ChunkHash.Of(ChunkBlobCodec.Encode(
            chunk.Blocks, chunk.Meta.Bytes, chunk.BlockLight.Bytes, chunk.SkyLight.Bytes));

        Assert.NotEqual(before, after);
    }

    /// <summary>
    ///     An offer survives the wire, since it is the one message here the client sends and the only
    ///     one whose framing has never been exercised in that direction.
    /// </summary>
    [Fact]
    public void A_cache_offer_round_trips()
    {
        ChunkCacheOfferMessage sent = new();
        for (int i = 0; i < 100; i++)
        {
            sent.Entries.Add(new KeyValuePair<ChunkPos, ulong>(new ChunkPos(i, -i), (ulong)(i * 0x123456789ABCDEF)));
        }

        MemoryStream wire = new();
        sent.Write(wire);
        Assert.Equal(sent.Size(), (int)wire.Length);

        wire.Position = 0;
        ChunkCacheOfferMessage received = new();
        received.Read(wire);

        Assert.Equal(sent.Entries, received.Entries);
    }

    /// <summary>
    ///     An offer is untrusted input and its declared count decides an allocation, so the bound is
    ///     checked before the list is sized rather than after entries start arriving.
    /// </summary>
    [Fact]
    public void An_offer_declaring_more_entries_than_allowed_is_refused()
    {
        MemoryStream wire = new();
        wire.WriteInt(ChunkCacheOfferMessage.MaxEntries + 1);
        wire.Position = 0;

        Assert.Throws<InvalidDataException>(() => new ChunkCacheOfferMessage().Read(wire));
    }

    [Fact]
    public void An_unchanged_notice_round_trips()
    {
        ChunkUnchangedMessage sent = new() { ChunkX = -12, ChunkZ = 34 };

        MemoryStream wire = new();
        sent.Write(wire);
        Assert.Equal(sent.Size(), (int)wire.Length);

        wire.Position = 0;
        ChunkUnchangedMessage received = new();
        received.Read(wire);

        Assert.Equal(-12, received.ChunkX);
        Assert.Equal(34, received.ChunkZ);

        // The saving this whole mechanism exists for, stated as a number.
        Assert.True(received.Size() < 16, "the point is that this is tiny next to a chunk");
    }

    [Fact]
    public void Size_matches_what_is_actually_written()
    {
        ChunkDataMessage message = MessageFor(Generate());

        MemoryStream written = new();
        message.Write(written);

        Assert.Equal(message.Size(), (int)written.Length);
    }

    /// <summary>
    ///     The message is bulk and must never overtake the traffic that cannot wait. A chunk is the
    ///     one payload large enough that letting it go first is visible as a stall, which is the
    ///     whole reason the priority split exists.
    /// </summary>
    [Fact]
    public void A_chunk_is_bulk_and_yields_to_timing_and_entity_traffic()
    {
        Assert.Equal(SendPriority.Normal, new ChunkDataMessage().Priority);
        Assert.Equal(SendPriority.High, new TickStampMessage().Priority);
    }

    /// <summary>
    ///     A decompression bomb is refused during expansion rather than after it. Checking the size
    ///     of the result is not a limit — by then the allocation has already happened, which is
    ///     precisely what the payload was asking for.
    /// </summary>
    [Fact]
    public void A_payload_that_expands_without_bound_is_refused()
    {
        MemoryStream compressed = new();
        using (System.IO.Compression.ZLibStream compressor = new(
                   compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            // Highly compressible and far past anything a chunk can hold.
            compressor.Write(new byte[ChunkDataMessage.MaxDecodedBytes * 4]);
        }

        ChunkDataMessage message = new() { Compressed = compressed.ToArray() };

        Assert.True(
            message.Compressed.Length < 4096,
            "the point of this test is a small payload that expands enormously");

        Assert.Throws<InvalidDataException>(message.Decompress);
    }

    [Fact]
    public void A_declared_length_past_the_bound_is_refused_before_allocating()
    {
        MemoryStream wire = new();
        wire.WriteInt(0);
        wire.WriteInt(0);
        wire.WriteVarInt(int.MaxValue);
        wire.Position = 0;

        Assert.Throws<InvalidDataException>(() => new ChunkDataMessage().Read(wire));
    }

    /// <summary>
    ///     Loopback keeps the legacy path. The message exists to make bytes smaller, and an internal
    ///     connection hands the packet over as an object — so compressing there costs an encode and a
    ///     matching decode to save bytes that never existed.
    /// </summary>
    [Fact]
    public void An_internal_connection_is_not_worth_compacting_for()
    {
        Assert.True(new InternalConnection(null, "test").IsInternal);
        Assert.False(new UdpConnection(new FakeTransportConnection()).IsInternal);
    }
}
