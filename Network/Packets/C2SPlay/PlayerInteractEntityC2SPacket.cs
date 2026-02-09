using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class PlayerInteractEntityC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerInteractEntityC2SPacket).TypeHandle);

        public int playerEntityId;
        public int targetEntity;
        public int isLeftClick;

        public PlayerInteractEntityC2SPacket()
        {
        }

        public PlayerInteractEntityC2SPacket(int var1, int var2, int var3)
        {
            playerEntityId = var1;
            targetEntity = var2;
            isLeftClick = var3;
        }

        public override void read(DataInputStream var1)
        {
            playerEntityId = var1.readInt();
            targetEntity = var1.readInt();
            isLeftClick = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(playerEntityId);
            var1.writeInt(targetEntity);
            var1.writeByte(isLeftClick);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleUseEntity(this);
        }

        public override int size()
        {
            return 9;
        }
    }

}