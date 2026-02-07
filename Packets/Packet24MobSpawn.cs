using betareborn.Entities;
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
            this.entityId = var1.entityId;
            this.type = (sbyte)EntityRegistry.getRawId(var1);
            this.xPosition = MathHelper.floor_double(var1.posX * 32.0D);
            this.yPosition = MathHelper.floor_double(var1.posY * 32.0D);
            this.zPosition = MathHelper.floor_double(var1.posZ * 32.0D);
            this.yaw = (sbyte)((int)(var1.rotationYaw * 256.0F / 360.0F));
            this.pitch = (sbyte)((int)(var1.rotationPitch * 256.0F / 360.0F));
            this.metaData = var1.getDataWatcher();
        }

        public override void read(DataInputStream var1)
        {
            this.entityId = var1.readInt();
            this.type = (sbyte)var1.readByte();
            this.xPosition = var1.readInt();
            this.yPosition = var1.readInt();
            this.zPosition = var1.readInt();
            this.yaw = (sbyte)var1.readByte();
            this.pitch = (sbyte)var1.readByte();
            this.receivedMetadata = DataWatcher.readWatchableObjects(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.entityId);
            var1.writeByte(this.type);
            var1.writeInt(this.xPosition);
            var1.writeInt(this.yPosition);
            var1.writeInt(this.zPosition);
            var1.writeByte(this.yaw);
            var1.writeByte(this.pitch);
            this.metaData.writeWatchableObjects(var1);
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
            return this.receivedMetadata;
        }
    }

}