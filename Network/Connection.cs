using betareborn.Network.Packets;
using betareborn.Threading;
using java.io;
using java.net;
using java.util;

namespace betareborn.Network
{
    public class Connection
    {
        public static readonly object LOCK = new object();
        public static int READ_THREAD_COUNTER;
        public static int WRITE_THREAD_COUNTER;
        private object lck = new object();
        private Socket socket;
        private readonly SocketAddress address;
        private DataInputStream inputStream;
        private DataOutputStream outputStream;
        private bool open = true;
        private List readQueue = Collections.synchronizedList(new ArrayList());
        private List sendQueue = Collections.synchronizedList(new ArrayList());
        private List delayedSendQueue = Collections.synchronizedList(new ArrayList());
        private NetHandler networkHandler;
        private bool closed = false;
        private java.lang.Thread writer;
        private java.lang.Thread reader;
        private bool disconnected = false;
        private string disconnectedReason = "";
        private object[] disconnectReasonArgs;
        private int timeout = 0;
        private int sendQueueSize = 0;
        public static int[] TOTAL_READ_SIZE = new int[256];
        public static int[] TOTAL_SEND_SIZE = new int[256];
        public int lag = 0;
        private int delay = 50;

        public Connection(Socket var1, string var2, NetHandler var3)
        {
            socket = var1;
            address = var1.getRemoteSocketAddress();
            networkHandler = var3;

            try
            {
                var1.setSoTimeout(30000);
                var1.setTrafficClass(24);
            }
            catch (SocketException var5)
            {
                java.lang.System.err.println(var5.getMessage());
            }

            inputStream = new DataInputStream(var1.getInputStream());
            outputStream = new DataOutputStream(new BufferedOutputStream(var1.getOutputStream(), 5120));
            reader = new NetworkReaderThread(this, var2 + " read thread");
            writer = new NetworkWriterThread(this, var2 + " write thread");
            reader.start();
            writer.start();
        }

        public void setNetworkHandler(NetHandler netHandler)
        {
            networkHandler = netHandler;
        }

        public void sendPacket(Packet packet)
        {
            if (!closed)
            {
                object var2 = lck;
                lock (var2)
                {
                    sendQueueSize += packet.size() + 1;
                    if (packet.worldPacket)
                    {
                        delayedSendQueue.add(packet);
                    }
                    else
                    {
                        sendQueue.add(packet);
                    }

                }
            }
        }

        private bool write()
        {
            bool var1 = false;

            try
            {
                int[] var10000;
                int var10001;
                Packet var2;
                object var3;
                if (!sendQueue.isEmpty() && (lag == 0 || java.lang.System.currentTimeMillis() - ((Packet)sendQueue.get(0)).creationTime >= lag))
                {
                    var3 = lck;
                    lock (var3)
                    {
                        var2 = (Packet)sendQueue.remove(0);
                        sendQueueSize -= var2.size() + 1;
                    }

                    Packet.write(var2, outputStream);
                    var10000 = TOTAL_SEND_SIZE;
                    var10001 = var2.getRawId();
                    var10000[var10001] += var2.size() + 1;
                    var1 = true;
                }

                if (delay-- <= 0 && !delayedSendQueue.isEmpty() && (lag == 0 || java.lang.System.currentTimeMillis() - ((Packet)delayedSendQueue.get(0)).creationTime >= lag))
                {
                    var3 = lck;
                    lock (var3)
                    {
                        var2 = (Packet)delayedSendQueue.remove(0);
                        sendQueueSize -= var2.size() + 1;
                    }

                    Packet.write(var2, outputStream);
                    var10000 = TOTAL_SEND_SIZE;
                    var10001 = var2.getRawId();
                    var10000[var10001] += var2.size() + 1;
                    delay = 0;
                    var1 = true;
                }

                return var1;
            }
            catch (java.lang.Exception var8)
            {
                if (!disconnected)
                {
                    disconnect(var8);
                }

                return false;
            }
        }

        public void interrupt()
        {
            reader.interrupt();
            writer.interrupt();
        }

        private bool read()
        {
            bool var1 = false;

            try
            {
                Packet var2 = Packet.read(inputStream, networkHandler.isServerHandler());
                if (var2 != null)
                {
                    int[] var10000 = TOTAL_READ_SIZE;
                    int var10001 = var2.getRawId();
                    var10000[var10001] += var2.size() + 1;
                    readQueue.add(var2);
                    var1 = true;
                }
                else
                {
                    disconnect("disconnect.endOfStream", new object[0]);
                }

                return var1;
            }
            catch (java.lang.Exception var3)
            {
                if (!disconnected)
                {
                    disconnect(var3);
                }

                return false;
            }
        }

        private void disconnect(java.lang.Exception var1)
        {
            var1.printStackTrace();
            disconnect("disconnect.genericReason", new object[] { "Internal exception: " + var1.toString() });
        }

        public void disconnect(string var1, params object[] var2)
        {
            if (open)
            {
                disconnected = true;
                disconnectedReason = var1;
                disconnectReasonArgs = var2;
                new NetworkMasterThread(this).start();
                open = false;

                try
                {
                    inputStream.close();
                    inputStream = null;
                }
                catch (java.lang.Throwable var6)
                {
                }

                try
                {
                    outputStream.close();
                    outputStream = null;
                }
                catch (java.lang.Throwable var5)
                {
                }

                try
                {
                    socket.close();
                    socket = null;
                }
                catch (java.lang.Throwable var4)
                {
                }

            }
        }

        public void tick()
        {
            if (sendQueueSize > 1048576)
            {
                disconnect("disconnect.overflow", new object[0]);
            }

            if (readQueue.isEmpty())
            {
                if (timeout++ == 1200)
                {
                    disconnect("disconnect.timeout", new object[0]);
                }
            }
            else
            {
                timeout = 0;
            }

            int var1 = 100;

            while (!readQueue.isEmpty() && var1-- >= 0)
            {
                Packet var2 = (Packet)readQueue.remove(0);
                var2.apply(networkHandler);
            }

            interrupt();
            if (disconnected && readQueue.isEmpty())
            {
                networkHandler.handleErrorMessage(disconnectedReason, disconnectReasonArgs);
            }

        }

        public SocketAddress getAddress()
        {
            return address;
        }

        public void disconnect()
        {
            interrupt();
            closed = true;
            reader.interrupt();
            new ThreadCloseConnection(this).start();
        }

        public int getDelayedSendQueueSize()
        {
            return delayedSendQueue.size();
        }

        public static bool isOpen(Connection var0)
        {
            return var0.open;
        }

        public static bool isClosed(Connection var0)
        {
            return var0.closed;
        }

        public static bool readPacket(Connection var0)
        {
            return var0.read();
        }

        public static bool writePacket(Connection var0)
        {
            return var0.write();
        }

        public static DataOutputStream getOutputStream(Connection var0)
        {
            return var0.outputStream;
        }

        public static bool isDisconnected(Connection var0)
        {
            return var0.disconnected;
        }

        public static void disconnect(Connection var0, java.lang.Exception var1)
        {
            var0.disconnect(var1);
        }

        public static java.lang.Thread getReader(Connection var0)
        {
            return var0.reader;
        }

        public static java.lang.Thread getWriter(Connection var0)
        {
            return var0.writer;
        }
    }

}