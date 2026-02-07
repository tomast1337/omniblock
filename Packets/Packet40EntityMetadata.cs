using java.io;
using java.util;

namespace betareborn.Packets
{
    public class Packet40EntityMetadata : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet40EntityMetadata).TypeHandle);

        public int entityId;
        private List field_21048_b;

        public override void read(DataInputStream var1)
        {
            this.entityId = var1.readInt();
            this.field_21048_b = DataWatcher.readWatchableObjects(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.entityId);
            DataWatcher.writeObjectsInListToStream(this.field_21048_b, var1);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_21148_a(this);
        }

        public override int size()
        {
            return 5;
        }

        public List func_21047_b()
        {
            return this.field_21048_b;
        }
    }

}