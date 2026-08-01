using BetaSharp.Network.Messages;

namespace BetaSharp.Tests.Network;

/// <summary>
///     Exercises the serialization the source generator emits.
///     <para>
///         The tests worth having here are the ones that pin the two failures hand-writing
///         <c>Read</c>, <c>Write</c> and <c>Size</c> separately used to allow: a field written but
///         not read back, and a <c>Size()</c> that disagrees with the bytes <c>Write()</c> produces.
///         Neither raises an exception in production — the first silently zeroes a field, the second
///         mis-frames every message after it — so a test is the only place they surface.
///     </para>
/// </summary>
public sealed class GeneratedMessageTests
{
    private static MessageRegistry Negotiated()
    {
        MessageRegistry registry = new();
        DefaultMessages.RegisterAll(registry);
        registry.NegotiateAsServer();
        return registry;
    }

    private static byte[] Serialise(Message message)
    {
        MemoryStream buffer = new();
        message.Write(buffer);
        return buffer.ToArray();
    }

    // ---- the whole registered set ----

    [Fact]
    public void Every_registered_message_reports_the_size_it_writes()
    {
        MessageRegistry registry = Negotiated();

        for (int id = 0; id < registry.Count; id++)
        {
            Message message = registry.Create(id)!;

            Assert.Equal(Serialise(message).Length, message.Size());
        }
    }

    [Fact]
    public void Every_registered_message_round_trips()
    {
        MessageRegistry registry = Negotiated();

        for (int id = 0; id < registry.Count; id++)
        {
            Message written = registry.Create(id)!;
            byte[] bytes = Serialise(written);

            Message read = registry.Create(id)!;
            read.Read(new MemoryStream(bytes, writable: false));

            // Re-serialising is the check rather than comparing properties: it catches a field that
            // Read skipped as surely as one it decoded wrongly, without the test needing to know
            // which properties a given message has.
            Assert.Equal(bytes, Serialise(read));
        }
    }

    [Fact]
    public void Reading_a_message_consumes_exactly_what_writing_produced()
    {
        MessageRegistry registry = Negotiated();

        for (int id = 0; id < registry.Count; id++)
        {
            byte[] bytes = Serialise(registry.Create(id)!);
            MemoryStream stream = new(bytes, writable: false);

            registry.Create(id)!.Read(stream);

            // A reader that stops short leaves the envelope's remaining bytes to be interpreted as
            // the next message. The length prefix contains the damage to one frame, but the frame
            // is still lost, and silently.
            Assert.Equal(bytes.Length, stream.Position);
        }
    }

    [Fact]
    public void The_generated_registration_list_covers_every_generated_message()
    {
        MessageRegistry registry = Negotiated();

        // Named individually rather than counted, so that a message dropped from the generated list
        // fails here instead of being absorbed by a total that happens to still match.
        foreach (ResourceLocation key in new[]
        {
            TimeSyncRequestMessage.Id,
            TimeSyncResponseMessage.Id,
            TickStampMessage.Id,
            ChunkDataMessage.Id,
            ChunkUnchangedMessage.Id,
            InteractEntityMessage.Id,
            SnapshotAckMessage.Id,
        })
        {
            Assert.True(registry.GetId(key) >= 0, $"{key} is not registered.");
        }
    }

    // ---- values survive a round trip ----

    [Fact]
    public void A_populated_time_sync_response_round_trips_every_field()
    {
        TimeSyncResponseMessage written = new()
        {
            Sequence = 0xDEADBEEF,
            ClientSendTime = 1_234_567_890_123L,
            ServerRecvTime = -42L,
        };

        TimeSyncResponseMessage read = new();
        read.Read(new MemoryStream(Serialise(written)));

        Assert.Equal(written.Sequence, read.Sequence);
        Assert.Equal(written.ClientSendTime, read.ClientSendTime);
        Assert.Equal(written.ServerRecvTime, read.ServerRecvTime);
    }

    [Fact]
    public void A_populated_interact_round_trips_every_field()
    {
        InteractEntityMessage written = new() { EntityId = -77, Action = 1, RenderTimeMs = 987_654_321L };

        InteractEntityMessage read = new();
        read.Read(new MemoryStream(Serialise(written)));

        Assert.Equal(written.EntityId, read.EntityId);
        Assert.Equal(written.Action, read.Action);
        Assert.Equal(written.RenderTimeMs, read.RenderTimeMs);
    }

    [Fact]
    public void A_populated_chunk_message_round_trips_its_blob()
    {
        byte[] blob = [.. Enumerable.Range(0, 5000).Select(i => (byte)(i * 31))];
        ChunkDataMessage written = new() { ChunkX = -1024, ChunkZ = 2048, Compressed = blob };

        byte[] bytes = Serialise(written);
        Assert.Equal(bytes.Length, written.Size());

        ChunkDataMessage read = new();
        read.Read(new MemoryStream(bytes));

        Assert.Equal(written.ChunkX, read.ChunkX);
        Assert.Equal(written.ChunkZ, read.ChunkZ);
        Assert.Equal(blob, read.Compressed);
    }

    /// <summary>
    ///     The declared bound is applied before the allocation, so a hostile length is refused
    ///     rather than honoured. <c>[WireField(MaxLength = ...)]</c> exists to make that check
    ///     impossible to leave out.
    /// </summary>
    [Fact]
    public void A_blob_longer_than_the_declared_bound_is_refused()
    {
        MemoryStream stream = new();
        stream.WriteInt(0);
        stream.WriteInt(0);
        stream.WriteVarInt(ChunkDataMessage.MaxDecodedBytes + 1);
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => new ChunkDataMessage().Read(stream));
    }

    // ---- the varint fields are actually varint ----

    [Fact]
    public void A_small_sequence_costs_one_byte()
    {
        SnapshotAckMessage message = new() { Sequence = 7 };

        Assert.Single(Serialise(message));
        Assert.Equal(1, message.Size());
    }

    [Fact]
    public void A_wrapped_sequence_survives_the_varint_round_trip()
    {
        // uint is the sequence type precisely because it wraps, so the encoding has to carry the
        // top bit rather than widening through a signed intermediate.
        SnapshotAckMessage written = new() { Sequence = uint.MaxValue };

        SnapshotAckMessage read = new();
        read.Read(new MemoryStream(Serialise(written)));

        Assert.Equal(uint.MaxValue, read.Sequence);
    }
}
