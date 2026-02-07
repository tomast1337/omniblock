using java.io;

namespace betareborn.Packets
{
    public class UpdateSignPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(UpdateSignPacket).TypeHandle);

        public int x;
        public int y;
        public int z;
        public string[] text;

        public UpdateSignPacket()
        {
            worldPacket = true;
        }

        public UpdateSignPacket(int x, int y, int z, string[] text)
        {
            worldPacket = true;
            this.x = x;
            this.y = y;
            this.z = z;
            this.text = text;
        }

        public override void read(DataInputStream stream)
        {
            x = stream.readInt();
            y = stream.readShort();
            z = stream.readInt();
            text = new string[4];

            for (int var2 = 0; var2 < 4; ++var2)
            {

                text[var2] = readString(stream, 15);
            }

        }

        public override void write(DataOutputStream stream)
        {
            stream.writeInt(x);
            stream.writeShort(y);
            stream.writeInt(z);

            for (int var2 = 0; var2 < 4; ++var2)
            {
                writeString(text[var2], stream);
            }

        }

        public override void apply(NetHandler networkHandler)
        {
            networkHandler.handleSignUpdate(this);
        }

        public override int size()
        {
            int var1 = 0;

            for (int var2 = 0; var2 < 4; ++var2)
            {
                var1 += text[var2].Length;
            }

            return var1;
        }
    }

}