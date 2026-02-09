using betareborn.Network.Packets;
using java.io;
using java.util;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityTrackerUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityTrackerUpdateS2CPacket).TypeHandle);

        public int entityId;
        private List field_21048_b;

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            field_21048_b = DataWatcher.readWatchableObjects(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            DataWatcher.writeObjectsInListToStream(field_21048_b, var1);
        }

        public override void apply(NetHandler var1)
        {
            var1.onEntityTrackerUpdate(this);
        }

        public override int size()
        {
            return 5;
        }

        public List func_21047_b()
        {
            return field_21048_b;
        }
    }

}