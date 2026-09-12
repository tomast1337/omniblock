namespace OmniBlock.Network.Messages;

/// <summary>
///     Server-authoritative terrain streaming and simulation radii for this session. Rendering may
///     use all transmitted terrain; gameplay ticking is bounded independently by the simulation
///     radius and never depends on camera direction.
/// </summary>
public sealed class SessionDistanceMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "session_distance");

    public int RenderDistance { get; set; }
    public int SimulationDistance { get; set; }
    public override ResourceLocation Key => Id;

    public override void Read(Stream stream)
    {
        RenderDistance = stream.ReadVarInt();
        SimulationDistance = stream.ReadVarInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteVarInt(RenderDistance);
        stream.WriteVarInt(SimulationDistance);
    }

    public override int Size() =>
        StreamExtensions.VarIntSize(RenderDistance) +
        StreamExtensions.VarIntSize(SimulationDistance);
}
