using betareborn.Entities;
using betareborn.Network.Packets.C2SPlay;
using betareborn.Network.Packets.Play;
using betareborn.Network.Packets.S2CPlay;
using betareborn.Stats;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Client.Network
{
    public class EntityClientPlayerMP : ClientPlayerEntity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityClientPlayerMP).TypeHandle);

        public ClientNetworkHandler sendQueue;
        private int field_9380_bx = 0;
        private bool field_21093_bH = false;
        private double oldPosX;
        private double field_9378_bz;
        private double oldPosY;
        private double oldPosZ;
        private float oldRotationYaw;
        private float oldRotationPitch;
        private bool field_9382_bF = false;
        private bool wasSneaking = false;
        private int field_12242_bI = 0;

        public EntityClientPlayerMP(Minecraft var1, World var2, Session var3, ClientNetworkHandler var4) : base(var1, var2, var3, 0)
        {
            sendQueue = var4;
        }

        public override bool damage(Entity var1, int var2)
        {
            return false;
        }

        public override void heal(int var1)
        {
        }

        public override void onUpdate()
        {
            if (world.isPosLoaded(MathHelper.floor_double(x), 64, MathHelper.floor_double(z)))
            {
                base.onUpdate();
                func_4056_N();
            }
        }

        public void func_4056_N()
        {
            if (field_9380_bx++ == 20)
            {
                sendInventoryChanged();
                field_9380_bx = 0;
            }

            bool var1 = isSneaking();
            if (var1 != wasSneaking)
            {
                if (var1)
                {
                    sendQueue.addToSendQueue(new ClientCommandC2SPacket(this, 1));
                }
                else
                {
                    sendQueue.addToSendQueue(new ClientCommandC2SPacket(this, 2));
                }

                wasSneaking = var1;
            }

            double var2 = x - oldPosX;
            double var4 = boundingBox.minY - field_9378_bz;
            double var6 = y - oldPosY;
            double var8 = z - oldPosZ;
            double var10 = (double)(yaw - oldRotationYaw);
            double var12 = (double)(pitch - oldRotationPitch);
            bool var14 = var4 != 0.0D || var6 != 0.0D || var2 != 0.0D || var8 != 0.0D;
            bool var15 = var10 != 0.0D || var12 != 0.0D;
            if (vehicle != null)
            {
                if (var15)
                {
                    sendQueue.addToSendQueue(new PlayerMovePositionAndOnGroundPacket(velocityX, -999.0D, -999.0D, velocityZ, onGround));
                }
                else
                {
                    sendQueue.addToSendQueue(new PlayerMoveFullPacket(velocityX, -999.0D, -999.0D, velocityZ, yaw, pitch, onGround));
                }

                var14 = false;
            }
            else if (var14 && var15)
            {
                sendQueue.addToSendQueue(new PlayerMoveFullPacket(x, boundingBox.minY, y, z, yaw, pitch, onGround));
                field_12242_bI = 0;
            }
            else if (var14)
            {
                sendQueue.addToSendQueue(new PlayerMovePositionAndOnGroundPacket(x, boundingBox.minY, y, z, onGround));
                field_12242_bI = 0;
            }
            else if (var15)
            {
                sendQueue.addToSendQueue(new PlayerMoveLookAndOnGroundPacket(yaw, pitch, onGround));
                field_12242_bI = 0;
            }
            else
            {
                sendQueue.addToSendQueue(new PlayerMovePacket(onGround));
                if (field_9382_bF == onGround && field_12242_bI <= 200)
                {
                    ++field_12242_bI;
                }
                else
                {
                    field_12242_bI = 0;
                }
            }

            field_9382_bF = onGround;
            if (var14)
            {
                oldPosX = x;
                field_9378_bz = boundingBox.minY;
                oldPosY = y;
                oldPosZ = z;
            }

            if (var15)
            {
                oldRotationYaw = yaw;
                oldRotationPitch = pitch;
            }

        }

        public override void dropSelectedItem()
        {
            sendQueue.addToSendQueue(new PlayerActionC2SPacket(4, 0, 0, 0, 0));
        }

        private void sendInventoryChanged()
        {
        }

        protected override void spawnItem(EntityItem var1)
        {
        }

        public override void sendChatMessage(string var1)
        {
            sendQueue.addToSendQueue(new ChatMessagePacket(var1));
        }

        public override void swingHand()
        {
            base.swingHand();
            sendQueue.addToSendQueue(new EntityAnimationPacket(this, 1));
        }

        public override void respawn()
        {
            sendInventoryChanged();
            sendQueue.addToSendQueue(new PlayerRespawnPacket((sbyte)dimension));
        }

        protected override void applyDamage(int var1)
        {
            health -= var1;
        }

        public override void closeScreen()
        {
            sendQueue.addToSendQueue(new CloseScreenS2CPacket(craftingInventory.syncId));
            inventory.setItemStack(null);
            base.closeScreen();
        }

        public override void setHealth(int var1)
        {
            if (field_21093_bH)
            {
                base.setHealth(var1);
            }
            else
            {
                health = var1;
                field_21093_bH = true;
            }

        }

        public override void increaseStat(StatBase var1, int var2)
        {
            if (var1 != null)
            {
                if (var1.localOnly)
                {
                    base.increaseStat(var1, var2);
                }

            }
        }

        public void func_27027_b(StatBase var1, int var2)
        {
            if (var1 != null)
            {
                if (!var1.localOnly)
                {
                    base.increaseStat(var1, var2);
                }

            }
        }
    }

}