namespace OmniBlock.Network.Messages;

/// <summary>
///     The player neither moved nor turned, but its footing changed. See <see cref="IPlayerMove" />
///     for why this is worth its own message.
///     <para>
///         Normal priority, like the rest of the family, and deliberately not high. Movement is
///         latency-sensitive, but a server-sent position that overtakes the chunk batch it belongs
///         after puts the player in terrain the client has not loaded — so it has to stay in the
///         same ordering domain as world data.
///     </para>
/// </summary>
public sealed class PlayerMoveMessage : Message, IPlayerMove
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "player_move");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;
    public bool OnGround { get; set; }

    public override void Read(Stream stream) => OnGround = stream.ReadBoolean();

    public override void Write(Stream stream) => stream.WriteBoolean(OnGround);

    public override int Size() => 1;
}
