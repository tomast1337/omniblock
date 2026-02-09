using betareborn.Entities;
using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class ClientCommandC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ClientCommandC2SPacket).TypeHandle);

        public int entityId;
        public int mode;

        public ClientCommandC2SPacket()
        {
        }

        public ClientCommandC2SPacket(Entity var1, int var2)
        {
            entityId = var1.id;
            mode = var2;
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            mode = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeByte(mode);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleClientCommand(this);
        }

        public override int size()
        {
            return 5;
        }
    }

}