using java.io;

namespace betareborn.Packets
{
    public class Packet7UseEntity : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet7UseEntity).TypeHandle);

        public int playerEntityId;
        public int targetEntity;
        public int isLeftClick;

        public Packet7UseEntity()
        {
        }

        public Packet7UseEntity(int var1, int var2, int var3)
        {
            this.playerEntityId = var1;
            this.targetEntity = var2;
            this.isLeftClick = var3;
        }

        public override void read(DataInputStream var1)
        {
            this.playerEntityId = var1.readInt();
            this.targetEntity = var1.readInt();
            this.isLeftClick = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.playerEntityId);
            var1.writeInt(this.targetEntity);
            var1.writeByte(this.isLeftClick);
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