using System.Net.Sockets;
using java.io;

namespace BetaSharp.Network.Packets.C2SPlay;

public class UpdateSelectedSlotC2SPacket : Packet
{
    public int selectedSlot;

    public UpdateSelectedSlotC2SPacket()
    {
    }

    public UpdateSelectedSlotC2SPacket(int selectedSlot)
    {
        this.selectedSlot = selectedSlot;
    }

    public override void Read(NetworkStream stream)
    {
        selectedSlot = stream.readShort();
    }

    public override void Write(NetworkStream stream)
    {
        stream.writeShort(selectedSlot);
    }

    public override void Apply(NetHandler handler)
    {
        handler.onUpdateSelectedSlot(this);
    }

    public override int Size()
    {
        return 2;
    }
}
