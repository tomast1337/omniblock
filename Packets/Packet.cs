using java.io;
using java.lang;
using java.util;

namespace betareborn.Packets
{
    public abstract class Packet : java.lang.Object
    {
        private static Map packetIdToClassMap = new HashMap();
        private static Map packetClassToIdMap = new HashMap();
        private static Set clientPacketIdList = new HashSet();
        private static Set serverPacketIdList = new HashSet();
        public readonly long creationTimeMillis = java.lang.System.currentTimeMillis();
        public bool worldPacket = false;
        private static HashMap packetStats;
        private static int totalPacketsCount;

        static void addIdClassMapping(int var0, bool var1, bool var2, Class var3)
        {
            if (packetIdToClassMap.containsKey(Integer.valueOf(var0)))
            {
                throw new IllegalArgumentException("Duplicate packet id:" + var0);
            }
            else if (packetClassToIdMap.containsKey(var3))
            {
                throw new IllegalArgumentException("Duplicate packet class:" + var3);
            }
            else
            {
                packetIdToClassMap.put(Integer.valueOf(var0), var3);
                packetClassToIdMap.put(var3, Integer.valueOf(var0));
                if (var1)
                {
                    clientPacketIdList.add(Integer.valueOf(var0));
                }

                if (var2)
                {
                    serverPacketIdList.add(Integer.valueOf(var0));
                }

            }
        }

        public static Packet getNewPacket(int var0)
        {
            try
            {
                Class var1 = (Class)packetIdToClassMap.get(Integer.valueOf(var0));
                return var1 == null ? null : (Packet)var1.newInstance();
            }
            catch (java.lang.Exception var2)
            {
                var2.printStackTrace();
                java.lang.System.@out.println("Skipping packet with id " + var0);
                return null;
            }
        }

        public int getPacketId()
        {
            return ((Integer)packetClassToIdMap.get(this.getClass())).intValue();
        }

        public static Packet readPacket(DataInputStream var0, bool var1)
        {
            bool var2 = false;
            Packet var3 = null;


            int var6;
            try
            {
                var6 = var0.read();
                if (var6 == -1)
                {
                    return null;
                }

                if (var1 && !serverPacketIdList.contains(Integer.valueOf(var6)) || !var1 && !clientPacketIdList.contains(Integer.valueOf(var6)))
                {
                    throw new java.io.IOException("Bad packet id " + var6);
                }

                var3 = getNewPacket(var6);
                if (var3 == null)
                {
                    throw new java.io.IOException("Bad packet id " + var6);
                }

                var3.read(var0);
            }
            catch (EOFException var5)
            {
                java.lang.System.@out.println("Reached end of stream");
                return null;
            }

            PacketCounter var4 = (PacketCounter)packetStats.get(Integer.valueOf(var6));
            if (var4 == null)
            {
                var4 = new PacketCounter((Empty1)null);
                packetStats.put(Integer.valueOf(var6), var4);
            }

            var4.addPacket(var3.size());
            ++totalPacketsCount;
            if (totalPacketsCount % 1000 == 0)
            {
            }

            return var3;
        }

        public static void writePacket(Packet var0, DataOutputStream var1)
        {
            var1.write(var0.getPacketId());
            var0.write(var1);
        }

        public static void writeString(string var0, DataOutputStream var1)
        {
            if (var0.Length > Short.MAX_VALUE)
            {
                throw new java.io.IOException("String too big");
            }
            else
            {
                var1.writeShort(var0.Length);
                var1.writeChars(var0);
            }
        }

        public static string readString(DataInputStream var0, int var1)
        {

            short var2 = var0.readShort();
            if (var2 > var1)
            {
                throw new java.io.IOException("Received string length longer than maximum allowed (" + var2 + " > " + var1 + ")");
            }
            else if (var2 < 0)
            {
                throw new java.io.IOException("Received string length is less than zero! Weird string!");
            }
            else
            {
                StringBuilder var3 = new StringBuilder();

                for (int var4 = 0; var4 < var2; ++var4)
                {
                    var3.append(var0.readChar());
                }

                return var3.toString();
            }
        }

        public abstract void read(DataInputStream var1);

        public abstract void write(DataOutputStream var1);

        public abstract void apply(NetHandler var1);

        public abstract int size();

        static Packet()
        {
            addIdClassMapping(0, true, true, Packet0KeepAlive.Class);
            addIdClassMapping(1, true, true, Packet1Login.Class);
            addIdClassMapping(2, true, true, Packet2Handshake.Class);
            addIdClassMapping(3, true, true, Packet3Chat.Class);
            addIdClassMapping(4, true, false, Packet4UpdateTime.Class);
            addIdClassMapping(5, true, false, Packet5PlayerInventory.Class);
            addIdClassMapping(6, true, false, Packet6SpawnPosition.Class);
            addIdClassMapping(7, false, true, Packet7UseEntity.Class);
            addIdClassMapping(8, true, false, Packet8UpdateHealth.Class);
            addIdClassMapping(9, true, true, Packet9Respawn.Class);
            addIdClassMapping(10, true, true, Packet10Flying.Class);
            addIdClassMapping(11, true, true, Packet11PlayerPosition.Class);
            addIdClassMapping(12, true, true, Packet12PlayerLook.Class);
            addIdClassMapping(13, true, true, Packet13PlayerLookMove.Class);
            addIdClassMapping(14, false, true, Packet14BlockDig.Class);
            addIdClassMapping(15, false, true, Packet15Place.Class);
            addIdClassMapping(16, false, true, Packet16BlockItemSwitch.Class);
            addIdClassMapping(17, true, false, Packet17Sleep.Class);
            addIdClassMapping(18, true, true, Packet18Animation.Class);
            addIdClassMapping(19, false, true, Packet19EntityAction.Class);
            addIdClassMapping(20, true, false, Packet20NamedEntitySpawn.Class);
            addIdClassMapping(21, true, false, Packet21PickupSpawn.Class);
            addIdClassMapping(22, true, false, Packet22Collect.Class);
            addIdClassMapping(23, true, false, Packet23VehicleSpawn.Class);
            addIdClassMapping(24, true, false, Packet24MobSpawn.Class);
            addIdClassMapping(25, true, false, Packet25EntityPainting.Class);
            addIdClassMapping(27, false, true, Packet27Position.Class);
            addIdClassMapping(28, true, false, Packet28EntityVelocity.Class);
            addIdClassMapping(29, true, false, Packet29DestroyEntity.Class);
            addIdClassMapping(30, true, false, Packet30Entity.Class);
            addIdClassMapping(31, true, false, Packet31RelEntityMove.Class);
            addIdClassMapping(32, true, false, Packet32EntityLook.Class);
            addIdClassMapping(33, true, false, Packet33RelEntityMoveLook.Class);
            addIdClassMapping(34, true, false, Packet34EntityTeleport.Class);
            addIdClassMapping(38, true, false, Packet38EntityStatus.Class);
            addIdClassMapping(39, true, false, Packet39AttachEntity.Class);
            addIdClassMapping(40, true, false, Packet40EntityMetadata.Class);
            addIdClassMapping(50, true, false, Packet50PreChunk.Class);
            addIdClassMapping(51, true, false, Packet51MapChunk.Class);
            addIdClassMapping(52, true, false, Packet52MultiBlockChange.Class);
            addIdClassMapping(53, true, false, Packet53BlockChange.Class);
            addIdClassMapping(54, true, false, Packet54PlayNoteBlock.Class);
            addIdClassMapping(60, true, false, Packet60Explosion.Class);
            addIdClassMapping(61, true, false, Packet61DoorChange.Class);
            addIdClassMapping(70, true, false, Packet70Bed.Class);
            addIdClassMapping(71, true, false, Packet71Weather.Class);
            addIdClassMapping(100, true, false, Packet100OpenWindow.Class);
            addIdClassMapping(101, true, true, Packet101CloseWindow.Class);
            addIdClassMapping(102, false, true, Packet102WindowClick.Class);
            addIdClassMapping(103, true, false, Packet103SetSlot.Class);
            addIdClassMapping(104, true, false, Packet104WindowItems.Class);
            addIdClassMapping(105, true, false, Packet105UpdateProgressbar.Class);
            addIdClassMapping(106, true, true, Packet106Transaction.Class);
            addIdClassMapping(130, true, true, UpdateSignPacket.Class);
            addIdClassMapping(131, true, false, Packet131MapData.Class);
            addIdClassMapping(200, true, false, Packet200Statistic.Class);
            addIdClassMapping(255, true, true, Packet255KickDisconnect.Class);
            packetStats = new HashMap();
            totalPacketsCount = 0;
        }
    }
}