using java.io;

namespace betareborn.Packets
{
    public class Packet1Login : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet1Login).TypeHandle);

        public int protocolVersion;
        public String username;
        public long mapSeed;
        public sbyte dimension;

        public Packet1Login()
        {
        }

        public Packet1Login(String var1, int var2)
        {
            this.username = var1;
            this.protocolVersion = var2;
        }

        public override void read(DataInputStream var1)
        {
            this.protocolVersion = var1.readInt();
            this.username = readString(var1, 16);
            this.mapSeed = var1.readLong();
            this.dimension = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.protocolVersion);
            writeString(this.username, var1);
            var1.writeLong(this.mapSeed);
            var1.writeByte(this.dimension);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleLogin(this);
        }

        public override int size()
        {
            return 4 + this.username.Length + 4 + 5;
        }
    }

}