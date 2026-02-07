using betareborn.Entities;
using java.io;

namespace betareborn.Packets
{
    public class Packet25EntityPainting : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet25EntityPainting).TypeHandle);

        public int entityId;
        public int xPosition;
        public int yPosition;
        public int zPosition;
        public int direction;
        public String title;

        public Packet25EntityPainting()
        {
        }

        public Packet25EntityPainting(EntityPainting var1)
        {
            this.entityId = var1.entityId;
            this.xPosition = var1.xPosition;
            this.yPosition = var1.yPosition;
            this.zPosition = var1.zPosition;
            this.direction = var1.direction;
            this.title = var1.art.title;
        }

        public override void read(DataInputStream var1)
        {
            this.entityId = var1.readInt();
            this.title = readString(var1, EnumArt.maxArtTitleLength);
            this.xPosition = var1.readInt();
            this.yPosition = var1.readInt();
            this.zPosition = var1.readInt();
            this.direction = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.entityId);
            writeString(this.title, var1);
            var1.writeInt(this.xPosition);
            var1.writeInt(this.yPosition);
            var1.writeInt(this.zPosition);
            var1.writeInt(this.direction);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_21146_a(this);
        }

        public override int size()
        {
            return 24;
        }
    }

}