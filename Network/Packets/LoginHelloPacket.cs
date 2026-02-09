using java.io;

namespace betareborn.Network.Packets
{
    public class LoginHelloPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(LoginHelloPacket).TypeHandle);

        public int protocolVersion;
        public string username;
        public long worldSeed;
        public sbyte dimensionId;

        public LoginHelloPacket()
        {
        }

        public LoginHelloPacket(string username, int protocolVersion, long worldSeed, sbyte dimensionId)
        {
            this.username = username;
            this.protocolVersion = protocolVersion;
            this.worldSeed = worldSeed;
            this.dimensionId = dimensionId;
        }

        public LoginHelloPacket(string var1, int var2)
        {
            username = var1;
            protocolVersion = var2;
        }

        public override void read(DataInputStream var1)
        {
            protocolVersion = var1.readInt();
            username = readString(var1, 16);
            worldSeed = var1.readLong();
            dimensionId = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(protocolVersion);
            writeString(username, var1);
            var1.writeLong(worldSeed);
            var1.writeByte(dimensionId);
        }

        public override void apply(NetHandler var1)
        {
            var1.onHello(this);
        }

        public override int size()
        {
            return 4 + username.Length + 4 + 5;
        }
    }

}