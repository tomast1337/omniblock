using betareborn.Entities;
using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class ClientCommandC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ClientCommandC2SPacket).TypeHandle);

        public int entityId;
        public int state;

        public ClientCommandC2SPacket()
        {
        }

        public ClientCommandC2SPacket(Entity var1, int var2)
        {
            entityId = var1.entityId;
            state = var2;
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            state = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeByte(state);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_21147_a(this);
        }

        public override int size()
        {
            return 5;
        }
    }

}