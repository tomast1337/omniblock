using betareborn.Entities;
using betareborn.Network;
using betareborn.Util.Maths;
using java.io;
using java.util;

namespace betareborn.Packets
{
    public class Packet24MobSpawn : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet24MobSpawn).TypeHandle);

        public int entityId;
        public sbyte type;
        public int xPosition;
        public int yPosition;
        public int zPosition;
        public sbyte yaw;
        public sbyte pitch;
        private DataWatcher metaData;
        private List receivedMetadata;

        public Packet24MobSpawn()
        {
        }

        public Packet24MobSpawn(EntityLiving var1)
        {
            entityId = var1.entityId;
            type = (sbyte)EntityRegistry.getRawId(var1);
            xPosition = MathHelper.floor_double(var1.posX * 32.0D);
            yPosition = MathHelper.floor_double(var1.posY * 32.0D);
            zPosition = MathHelper.floor_double(var1.posZ * 32.0D);
            yaw = (sbyte)((int)(var1.rotationYaw * 256.0F / 360.0F));
            pitch = (sbyte)((int)(var1.rotationPitch * 256.0F / 360.0F));
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
            var1.handleMobSpawn(this);
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