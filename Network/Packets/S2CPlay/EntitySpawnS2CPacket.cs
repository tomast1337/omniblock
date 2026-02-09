using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntitySpawnS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntitySpawnS2CPacket).TypeHandle);

        public int entityId;
        public int xPosition;
        public int yPosition;
        public int zPosition;
        public int field_28047_e;
        public int field_28046_f;
        public int field_28045_g;
        public int type;
        public int field_28044_i;

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            type = (sbyte)var1.readByte();
            xPosition = var1.readInt();
            yPosition = var1.readInt();
            zPosition = var1.readInt();
            field_28044_i = var1.readInt();
            if (field_28044_i > 0)
            {
                field_28047_e = var1.readShort();
                field_28046_f = var1.readShort();
                field_28045_g = var1.readShort();
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeByte(type);
            var1.writeInt(xPosition);
            var1.writeInt(yPosition);
            var1.writeInt(zPosition);
            var1.writeInt(field_28044_i);
            if (field_28044_i > 0)
            {
                var1.writeShort(field_28047_e);
                var1.writeShort(field_28046_f);
                var1.writeShort(field_28045_g);
            }

        }

        public override void apply(NetHandler var1)
        {
            var1.handleVehicleSpawn(this);
        }

        public override int size()
        {
            return 21 + field_28044_i > 0 ? 6 : 0;
        }
    }

}