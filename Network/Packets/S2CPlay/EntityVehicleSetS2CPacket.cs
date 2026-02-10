using betareborn.Entities;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityVehicleSetS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityVehicleSetS2CPacket).TypeHandle);

        public int entityId;
        public int vehicleEntityId;

        public EntityVehicleSetS2CPacket()
        {
        }

        public EntityVehicleSetS2CPacket(Entity entity, Entity vehicle)
        {
            entityId = entity.id;
            vehicleEntityId = vehicle != null ? vehicle.id : -1;
        }

        public override int size()
        {
            return 8;
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            vehicleEntityId = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeInt(vehicleEntityId);
        }

        public override void apply(NetHandler var1)
        {
            var1.onEntityVehicleSet(this);
        }
    }

}