using betareborn.Entities;
using betareborn.Util.Maths;
using java.io;
using java.util;

namespace betareborn.Network.Packets.S2CPlay
{
    public class LivingEntitySpawnS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(LivingEntitySpawnS2CPacket).TypeHandle);

        public int entityId;
        public sbyte type;
        public int xPosition;
        public int yPosition;
        public int zPosition;
        public sbyte yaw;
        public sbyte pitch;
        private DataWatcher metaData;
        private List receivedMetadata;

        public LivingEntitySpawnS2CPacket()
        {
        }

        public LivingEntitySpawnS2CPacket(EntityLiving var1)
        {
            entityId = var1.id;
            type = (sbyte)EntityRegistry.getRawId(var1);
            xPosition = MathHelper.floor_double(var1.x * 32.0D);
            yPosition = MathHelper.floor_double(var1.y * 32.0D);
            zPosition = MathHelper.floor_double(var1.z * 32.0D);
            yaw = (sbyte)(int)(var1.yaw * 256.0F / 360.0F);
            pitch = (sbyte)(int)(var1.pitch * 256.0F / 360.0F);
            metaData = var1.getDataWatcher();
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            type = (sbyte)var1.readByte();
            xPosition = var1.readInt();
            yPosition = var1.readInt();
            zPosition = var1.readInt();
            yaw = (sbyte)var1.readByte();
            pitch = (sbyte)var1.readByte();
            receivedMetadata = DataWatcher.readWatchableObjects(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeByte(type);
            var1.writeInt(xPosition);
            var1.writeInt(yPosition);
            var1.writeInt(zPosition);
            var1.writeByte(yaw);
            var1.writeByte(pitch);
            metaData.writeWatchableObjects(var1);
        }

        public override void apply(NetHandler var1)
        {
            var1.onLivingEntitySpawn(this);
        }

        public override int size()
        {
            return 20;
        }

        public List getMetadata()
        {
            return receivedMetadata;
        }
    }

}