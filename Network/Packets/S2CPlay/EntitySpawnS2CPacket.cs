using betareborn.Entities;
using betareborn.Util.Maths;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntitySpawnS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntitySpawnS2CPacket).TypeHandle);

        public int id;
        public int x;
        public int y;
        public int z;
        public int velocityX;
        public int velocityY;
        public int velocityZ;
        public int entityType;
        public int entityData;

        public EntitySpawnS2CPacket()
        {
        }

        public EntitySpawnS2CPacket(Entity entity, int entityType) : this(entity, entityType, 0)
        {
        }

        public EntitySpawnS2CPacket(Entity entity, int entityType, int entityData)
        {
            id = entity.id;
            x = MathHelper.floor(entity.x * 32.0);
            y = MathHelper.floor(entity.y * 32.0);
            z = MathHelper.floor(entity.z * 32.0);
            this.entityType = entityType;
            this.entityData = entityData;
            if (entityData > 0)
            {
                double var4 = entity.velocityX;
                double var6 = entity.velocityY;
                double var8 = entity.velocityZ;
                double var10 = 3.9;
                if (var4 < -var10)
                {
                    var4 = -var10;
                }

                if (var6 < -var10)
                {
                    var6 = -var10;
                }

                if (var8 < -var10)
                {
                    var8 = -var10;
                }

                if (var4 > var10)
                {
                    var4 = var10;
                }

                if (var6 > var10)
                {
                    var6 = var10;
                }

                if (var8 > var10)
                {
                    var8 = var10;
                }

                velocityX = (int)(var4 * 8000.0);
                velocityY = (int)(var6 * 8000.0);
                velocityZ = (int)(var8 * 8000.0);
            }
        }

        public override void read(DataInputStream var1)
        {
            id = var1.readInt();
            entityType = (sbyte)var1.readByte();
            x = var1.readInt();
            y = var1.readInt();
            z = var1.readInt();
            entityData = var1.readInt();
            if (entityData > 0)
            {
                velocityX = var1.readShort();
                velocityY = var1.readShort();
                velocityZ = var1.readShort();
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(id);
            var1.writeByte(entityType);
            var1.writeInt(x);
            var1.writeInt(y);
            var1.writeInt(z);
            var1.writeInt(entityData);
            if (entityData > 0)
            {
                var1.writeShort(velocityX);
                var1.writeShort(velocityY);
                var1.writeShort(velocityZ);
            }

        }

        public override void apply(NetHandler var1)
        {
            var1.onEntitySpawn(this);
        }

        public override int size()
        {
            return 21 + entityData > 0 ? 6 : 0;
        }
    }

}