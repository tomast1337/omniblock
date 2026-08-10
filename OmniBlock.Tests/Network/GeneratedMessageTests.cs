using OmniBlock.Items;
using OmniBlock.Network.Messages;

namespace OmniBlock.Tests.Network;

/// <summary>
///     Exercises the serialization each message's <c>*.Wire.cs</c> companion file implements.
///     <para>
///         The tests worth having here are the ones that pin the two failures writing
///         <c>Read</c>, <c>Write</c> and <c>Size</c> as three separate members allows: a field
///         written but not read back, and a <c>Size()</c> that disagrees with the bytes
///         <c>Write()</c> produces. Neither raises an exception in production — the first silently
///         zeroes a field, the second mis-frames every message after it — so a test is the only
///         place they surface.
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
    public void The_registration_list_covers_every_wire_message()
    {
        MessageRegistry registry = Negotiated();

        // Named individually rather than counted, so that a message dropped from
        // MessageRegistrations fails here instead of being absorbed by a total that happens to
        // still match.
        foreach (ResourceLocation key in new[]
        {
            TimeSyncRequestMessage.Id,
            TimeSyncResponseMessage.Id,
            TickStampMessage.Id,
            ChunkDataMessage.Id,
            ChunkUnchangedMessage.Id,
            InteractEntityMessage.Id,
            SnapshotAckMessage.Id,
            PlayerActionMessage.Id,
            InteractBlockMessage.Id,
            SelectedSlotMessage.Id,
            ClientCommandMessage.Id,
            PlayerInputMessage.Id,
            ClickSlotMessage.Id,
            EntityMoveMessage.Id,
            EntityTeleportMessage.Id,
            EntityDestroyMessage.Id,
            EntityStatusMessage.Id,
            EntityVelocityMessage.Id,
            EntityVehicleMessage.Id,
            EntityDataMessage.Id,
            EntityEquipmentMessage.Id,
            EntityAnimationMessage.Id,
            ItemPickupMessage.Id,
            EntitySpawnMessage.Id,
            ItemEntitySpawnMessage.Id,
            LivingEntitySpawnMessage.Id,
            GlobalEntitySpawnMessage.Id,
            PaintingSpawnMessage.Id,
            PlayerSpawnMessage.Id,
            InventoryMessage.Id,
            ScreenHandlerSlotMessage.Id,
            ScreenHandlerPropertyMessage.Id,
            ScreenHandlerAckMessage.Id,
            OpenScreenMessage.Id,
            CloseScreenMessage.Id,
            UpdateSignMessage.Id,
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
    ///     rather than honoured. <c>MaxLength</c> is baked into the message's <c>Read</c> for exactly
    ///     that reason: it cannot be left out of one field's reader without editing that reader.
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

    // ---- inventory slots ----

    [Fact]
    public void A_filled_slot_round_trips_and_costs_five_bytes()
    {
        ClickSlotMessage written = new()
        {
            SyncId = 3,
            Slot = 17,
            Button = 1,
            ActionType = 42,
            HoldingShift = true,
            Stack = new ItemStack(Item.ByName("stick"), 7, 2),
        };

        byte[] bytes = Serialise(written);
        Assert.Equal(bytes.Length, written.Size());

        ClickSlotMessage read = new();
        read.Read(new MemoryStream(bytes));

        Assert.Equal(written.SyncId, read.SyncId);
        Assert.Equal(written.Slot, read.Slot);
        Assert.Equal(written.Button, read.Button);
        Assert.Equal(written.ActionType, read.ActionType);
        Assert.Equal(written.HoldingShift, read.HoldingShift);
        Assert.NotNull(read.Stack);
        Assert.Equal(written.Stack.ItemId, read.Stack.ItemId);
        Assert.Equal(written.Stack.Count, read.Stack.Count);
        Assert.Equal(written.Stack.GetDamage(), read.Stack.GetDamage());
    }

    /// <summary>
    ///     The two slot encodings differ by three bytes, which is what the hand-written packets got
    ///     wrong: <c>ClickSlotC2SPacket</c> declared a constant eleven for a payload that is nine or
    ///     twelve. A generated size is measured, not declared.
    /// </summary>
    [Fact]
    public void An_empty_slot_costs_three_bytes_less_than_a_filled_one()
    {
        ClickSlotMessage empty = new();
        ClickSlotMessage filled = new() { Stack = new ItemStack(Item.ByName("stick"), 1, 0) };

        Assert.Equal(Serialise(empty).Length, empty.Size());
        Assert.Equal(Serialise(filled).Length, filled.Size());
        Assert.Equal(3, filled.Size() - empty.Size());
    }

    // ---- the four position packets, collapsed ----

    [Fact]
    public void A_move_carries_its_mask_and_both_field_groups()
    {
        EntityMoveMessage written = new()
        {
            EntityId = 4242,
            Mask = EntityMoveMessage.Field.Moved | EntityMoveMessage.Field.Rotated,
            DeltaX = -3,
            DeltaY = 1,
            DeltaZ = 127,
            Yaw = -128,
            Pitch = 64,
        };

        byte[] bytes = Serialise(written);
        Assert.Equal(bytes.Length, written.Size());

        EntityMoveMessage read = new();
        read.Read(new MemoryStream(bytes));

        Assert.Equal(written.Mask, read.Mask);
        Assert.Equal(written.DeltaX, read.DeltaX);
        Assert.Equal(written.DeltaY, read.DeltaY);
        Assert.Equal(written.DeltaZ, read.DeltaZ);
        Assert.Equal(written.Yaw, read.Yaw);
        Assert.Equal(written.Pitch, read.Pitch);
    }

    /// <summary>
    ///     A bare "still here" — what <c>EntityS2CPacket</c> was — is the same type with an empty
    ///     mask, and it must survive the round trip as empty rather than as a move of zero.
    /// </summary>
    [Fact]
    public void A_move_with_no_mask_stays_unflagged()
    {
        EntityMoveMessage read = new() { Mask = EntityMoveMessage.Field.Moved };
        read.Read(new MemoryStream(Serialise(new EntityMoveMessage { EntityId = 7 })));

        Assert.Equal(EntityMoveMessage.Field.None, read.Mask);
        Assert.Equal(7, read.EntityId);
    }

    // ---- spawns ----

    /// <summary>
    ///     <c>EntitySpawnS2CPacket</c> wrote its velocity only when <c>EntityData</c> was positive,
    ///     so the payload's length depended on one of its own fields — and its <c>Size()</c> read
    ///     <c>17 + 4 + EntityData &gt; 0 ? 6 : 0</c>, which C# groups as
    ///     <c>(21 + EntityData) &gt; 0 ? 6 : 0</c> and therefore answered 6 for a 21-byte packet.
    ///     The message carries the velocity unconditionally; six bytes on a spawn is cheaper than a
    ///     conditional nobody can size.
    /// </summary>
    [Fact]
    public void An_object_spawn_is_the_same_size_with_and_without_entity_data()
    {
        EntitySpawnMessage plain = new() { EntityId = 1, EntityType = 10 };
        EntitySpawnMessage owned = new() { EntityId = 1, EntityType = 60, EntityData = 77, VelocityX = 400 };

        Assert.Equal(Serialise(plain).Length, plain.Size());
        Assert.Equal(Serialise(owned).Length, owned.Size());
        Assert.Equal(plain.Size(), owned.Size());
    }

    [Fact]
    public void A_player_spawn_round_trips_its_name_and_pose()
    {
        PlayerSpawnMessage written = new()
        {
            EntityId = 12,
            Name = "Nicolas",
            X = -32_768,
            Y = 2_048,
            Z = 96,
            Yaw = -64,
            Pitch = 32,
            CurrentItem = 280,
        };

        byte[] bytes = Serialise(written);
        Assert.Equal(bytes.Length, written.Size());

        PlayerSpawnMessage read = new();
        read.Read(new MemoryStream(bytes));

        Assert.Equal(written.Name, read.Name);
        Assert.Equal(written.X, read.X);
        Assert.Equal(written.Yaw, read.Yaw);
        Assert.Equal(written.CurrentItem, read.CurrentItem);
    }

    /// <summary>
    ///     A name is bounded before the allocation, not after. Sixteen characters is the account
    ///     limit; a peer claiming sixty thousand gets refused rather than obliged.
    /// </summary>
    [Fact]
    public void A_name_longer_than_the_declared_bound_is_refused()
    {
        MemoryStream stream = new();
        stream.WriteInt(0);
        stream.WriteString(new string('x', PlayerSpawnMessage.MaxNameBytes + 1));
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => new PlayerSpawnMessage().Read(stream));
    }

    /// <summary>
    ///     The painting title's bound is a literal because <c>Painting.MaxArtTitleLength</c> is
    ///     computed and cannot be named in an attribute. This is what keeps the two from drifting.
    /// </summary>
    [Fact]
    public void The_painting_title_bound_covers_every_art_title()
    {
        Assert.True(
            PaintingSpawnMessage.MaxTitleBytes >= Painting.MaxArtTitleLength,
            $"the wire bound {PaintingSpawnMessage.MaxTitleBytes} is below the longest art title "
            + $"{Painting.MaxArtTitleLength}, so a valid painting would be refused");
    }

    /// <summary>
    ///     A mob's first sight carries its whole synchronised data set, and the packet that did so
    ///     declared a constant 20 bytes for a payload of 20 plus that data — wrong on every spawn.
    /// </summary>
    [Fact]
    public void A_living_spawn_sizes_its_data_rather_than_assuming_it_is_absent()
    {
        LivingEntitySpawnMessage empty = new();
        LivingEntitySpawnMessage populated = new() { Data = [1, 2, 3, 4, 5] };

        Assert.Equal(Serialise(empty).Length, empty.Size());
        Assert.Equal(Serialise(populated).Length, populated.Size());
        Assert.Equal(5, populated.Size() - empty.Size());
    }

    // ---- screens ----

    /// <summary>
    ///     <c>InventoryS2CPacket</c> charged five bytes for every slot — <c>3 + Contents.Length *
    ///     5</c> — while an empty one writes two. A player's inventory is mostly empty, so the
    ///     declared size was wrong on essentially every send, and wrong by more the emptier it got.
    /// </summary>
    [Fact]
    public void An_inventory_sizes_its_empty_slots_at_two_bytes_not_five()
    {
        InventoryMessage message = new()
        {
            SyncId = 0,
            Contents = [new ItemStack(Item.ByName("stick"), 64, 0), null, null, new ItemStack(Item.ByName("bucket"), 1, 0)],
        };

        byte[] bytes = Serialise(message);

        Assert.Equal(bytes.Length, message.Size());

        // One byte of sync id, one varint of count, then 5 + 2 + 2 + 5.
        Assert.Equal(1 + 1 + 5 + 2 + 2 + 5, bytes.Length);
    }

    [Fact]
    public void An_inventory_round_trips_its_slots_including_the_empty_ones()
    {
        InventoryMessage written = new()
        {
            SyncId = 3,
            Contents = [null, new ItemStack(Item.ByName("stick"), 7, 2), null],
        };

        InventoryMessage read = new();
        read.Read(new MemoryStream(Serialise(written)));

        Assert.Equal(3, read.Contents.Length);
        Assert.Null(read.Contents[0]);
        Assert.Null(read.Contents[2]);
        Assert.Equal(7, read.Contents[1]!.Count);
    }

    /// <summary>
    ///     The slot count decides an array allocation, so it is bounded before the array is made.
    /// </summary>
    [Fact]
    public void An_inventory_larger_than_the_declared_bound_is_refused()
    {
        MemoryStream stream = new();
        stream.WriteByte(0);
        stream.WriteVarInt(InventoryMessage.MaxSlots + 1);
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => new InventoryMessage().Read(stream));
    }

    /// <summary>
    ///     A sign is four lines by definition. <c>UpdateSignPacket</c> wrapped each line in a
    ///     try/catch to survive an array that was not four long; four named fields cannot be the
    ///     wrong length, and a short array is padded rather than swallowed.
    /// </summary>
    [Fact]
    public void A_sign_is_always_four_lines()
    {
        UpdateSignMessage message = new() { Lines = ["only", "two"] };

        Assert.Equal(["only", "two", string.Empty, string.Empty], message.Lines);

        UpdateSignMessage read = new();
        read.Read(new MemoryStream(Serialise(message)));

        Assert.Equal(message.Lines, read.Lines);
    }

    [Fact]
    public void A_sign_line_longer_than_the_declared_bound_is_refused()
    {
        MemoryStream stream = new();
        stream.WriteInt(0);
        stream.WriteShort(64);
        stream.WriteInt(0);
        stream.WriteString(new string('x', UpdateSignMessage.MaxLineBytes + 1));
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => new UpdateSignMessage().Read(stream));
    }

    // ---- dispatch ----

    [Fact]
    public void A_registered_handler_receives_its_message()
    {
        MessageDispatcher dispatcher = new();
        uint seen = 0;
        dispatcher.On<SnapshotAckMessage>(ack => seen = ack.Sequence);

        Assert.True(dispatcher.Dispatch(new SnapshotAckMessage { Sequence = 9 }));
        Assert.Equal(9u, seen);
    }

    [Fact]
    public void An_unregistered_message_is_reported_as_unhandled_rather_than_thrown()
    {
        // A client-bound message arriving at a server is the ordinary case, not a fault.
        Assert.False(new MessageDispatcher().Dispatch(new TickStampMessage()));
    }

    [Fact]
    public void Two_handlers_for_one_message_is_refused()
    {
        MessageDispatcher dispatcher = new();
        dispatcher.On<TickStampMessage>(_ => { });

        // Silently letting the second win would make which one runs depend on load order.
        Assert.Throws<InvalidOperationException>(() => dispatcher.On<TickStampMessage>(_ => { }));
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
