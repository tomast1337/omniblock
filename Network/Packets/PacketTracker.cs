namespace betareborn.Network.Packets
{
    public class PacketTracker
    {
        private int count;
        private long size;

        private PacketTracker()
        {
        }

        public void update(int size)
        {
            ++count;
            this.size += size;
        }

        public PacketTracker(Empty1 var1) : this()
        {
        }
    }

}