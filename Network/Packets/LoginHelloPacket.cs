using betareborn.Network;
using java.io;

namespace betareborn.Network.Packets
{
    public class LoginHelloPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(LoginHelloPacket).TypeHandle);

        public int protocolVersion;
        public string username;
        public long mapSeed;
        public sbyte dimension;

        public LoginHelloPacket()
        {
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
            mapSeed = var1.readLong();
            dimension = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(protocolVersion);
            writeString(username, var1);
            var1.writeLong(mapSeed);
            var1.writeByte(dimension);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleLogin(this);
        }

        public override int size()
        {
            return 4 + username.Length + 4 + 5;
        }
    }

}