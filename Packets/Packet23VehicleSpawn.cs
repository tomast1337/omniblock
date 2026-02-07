using java.io;

namespace betareborn.Packets
{
    public class Packet23VehicleSpawn : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet23VehicleSpawn).TypeHandle);

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
            this.entityId = var1.readInt();
            this.type = (sbyte)var1.readByte();
            this.xPosition = var1.readInt();
            this.yPosition = var1.readInt();
            this.zPosition = var1.readInt();
            this.field_28044_i = var1.readInt();
            if (this.field_28044_i > 0)
            {
                this.field_28047_e = var1.readShort();
                this.field_28046_f = var1.readShort();
                this.field_28045_g = var1.readShort();
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.entityId);
            var1.writeByte(this.type);
            var1.writeInt(this.xPosition);
            var1.writeInt(this.yPosition);
            var1.writeInt(this.zPosition);
            var1.writeInt(this.field_28044_i);
            if (this.field_28044_i > 0)
            {
                var1.writeShort(this.field_28047_e);
                var1.writeShort(this.field_28046_f);
                var1.writeShort(this.field_28045_g);
            }

        }

        public override void apply(NetHandler var1)
        {
            var1.handleVehicleSpawn(this);
        }

        public override int size()
        {
            return 21 + this.field_28044_i > 0 ? 6 : 0;
        }
    }

}