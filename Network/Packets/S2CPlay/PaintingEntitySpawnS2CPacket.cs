using betareborn.Entities;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class PaintingEntitySpawnS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PaintingEntitySpawnS2CPacket).TypeHandle);

        public int entityId;
        public int xPosition;
        public int yPosition;
        public int zPosition;
        public int direction;
        public string title;

        public PaintingEntitySpawnS2CPacket()
        {
        }

        public PaintingEntitySpawnS2CPacket(EntityPainting var1)
        {
            entityId = var1.id;
            xPosition = var1.xPosition;
            yPosition = var1.yPosition;
            zPosition = var1.zPosition;
            direction = var1.direction;
            title = var1.art.title;
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            title = readString(var1, EnumArt.maxArtTitleLength);
            xPosition = var1.readInt();
            yPosition = var1.readInt();
            zPosition = var1.readInt();
            direction = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            writeString(title, var1);
            var1.writeInt(xPosition);
            var1.writeInt(yPosition);
            var1.writeInt(zPosition);
            var1.writeInt(direction);
        }

        public override void apply(NetHandler var1)
        {
            var1.onPaintingEntitySpawn(this);
        }

        public override int size()
        {
            return 24;
        }
    }

}