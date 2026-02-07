using betareborn.Packets;
using betareborn.Threading;
using java.io;
using java.net;
using java.util;

namespace betareborn
{
    public class NetworkManager
    {
        public static readonly Object threadSyncObject = new Object();
        public static int numReadThreads;
        public static int numWriteThreads;
        private Object sendQueueLock = new Object();
        private Socket networkSocket;
        private readonly SocketAddress remoteSocketAddress;
        private DataInputStream socketInputStream;
        private DataOutputStream socketOutputStream;
        private bool _isRunning = true;
        private List readPackets = Collections.synchronizedList(new ArrayList());
        private List dataPackets = Collections.synchronizedList(new ArrayList());
        private List chunkDataPackets = Collections.synchronizedList(new ArrayList());
        private NetHandler netHandler;
        private bool _isServerTerminating = false;
        private java.lang.Thread writeThread;
        private java.lang.Thread readThread;
        private bool isTerminating = false;
        private String terminationReason = "";
        private Object[] field_20101_t;
        private int timeSinceLastRead = 0;
        private int sendQueueByteLength = 0;
        public static int[] field_28145_d = new int[256];
        public static int[] field_28144_e = new int[256];
        public int chunkDataSendCounter = 0;
        private int field_20100_w = 50;

        public NetworkManager(Socket var1, String var2, NetHandler var3)
        {
            networkSocket = var1;
            remoteSocketAddress = var1.getRemoteSocketAddress();
            netHandler = var3;

            try
            {
                var1.setSoTimeout(30000);
                var1.setTrafficClass(24);
            }
            catch (SocketException var5)
            {
                java.lang.System.err.println(var5.getMessage());
            }

            socketInputStream = new DataInputStream(var1.getInputStream());
            socketOutputStream = new DataOutputStream(new BufferedOutputStream(var1.getOutputStream(), 5120));
            readThread = new NetworkReaderThread(this, var2 + " read thread");
            writeThread = new NetworkWriterThread(this, var2 + " write thread");
            readThread.start();
            writeThread.start();
        }

        public void addToSendQueue(Packet var1)
        {
            if (!_isServerTerminating)
            {
                Object var2 = sendQueueLock;
                lock (var2)
                {
                    sendQueueByteLength += var1.size() + 1;
                    if (var1.worldPacket)
                    {
                        chunkDataPackets.add(var1);
                    }
                    else
                    {
                        dataPackets.add(var1);
                    }

                }
            }
        }

        private bool sendPacket()
        {
            bool var1 = false;

            try
            {
                int[] var10000;
                int var10001;
                Packet var2;
                Object var3;
                if (!dataPackets.isEmpty() && (chunkDataSendCounter == 0 || java.lang.System.currentTimeMillis() - ((Packet)dataPackets.get(0)).creationTimeMillis >= (long)chunkDataSendCounter))
                {
                    var3 = sendQueueLock;
                    lock (var3)
                    {
                        var2 = (Packet)dataPackets.remove(0);
                        sendQueueByteLength -= var2.size() + 1;
                    }

                    Packet.writePacket(var2, socketOutputStream);
                    var10000 = field_28144_e;
                    var10001 = var2.getPacketId();
                    var10000[var10001] += var2.size() + 1;
                    var1 = true;
                }

                if (field_20100_w-- <= 0 && !chunkDataPackets.isEmpty() && (chunkDataSendCounter == 0 || java.lang.System.currentTimeMillis() - ((Packet)chunkDataPackets.get(0)).creationTimeMillis >= (long)chunkDataSendCounter))
                {
                    var3 = sendQueueLock;
                    lock (var3)
                    {
                        var2 = (Packet)chunkDataPackets.remove(0);
                        sendQueueByteLength -= var2.size() + 1;
                    }

                    Packet.writePacket(var2, socketOutputStream);
                    var10000 = field_28144_e;
                    var10001 = var2.getPacketId();
                    var10000[var10001] += var2.size() + 1;
                    field_20100_w = 0;
                    var1 = true;
                }

                return var1;
            }
            catch (java.lang.Exception var8)
            {
                if (!isTerminating)
                {
                    onNetworkError(var8);
                }

                return false;
            }
        }

        public void wakeThreads()
        {
            readThread.interrupt();
            writeThread.interrupt();
        }

        private bool readPacket()
        {
            bool var1 = false;

            try
            {
                Packet var2 = Packet.readPacket(socketInputStream, netHandler.isServerHandler());
                if (var2 != null)
                {
                    int[] var10000 = field_28145_d;
                    int var10001 = var2.getPacketId();
                    var10000[var10001] += var2.size() + 1;
                    readPackets.add(var2);
                    var1 = true;
                }
                else
                {
                    networkShutdown("disconnect.endOfStream", new Object[0]);
                }

                return var1;
            }
            catch (java.lang.Exception var3)
            {
                if (!isTerminating)
                {
                    onNetworkError(var3);
                }

                return false;
            }
        }

        private void onNetworkError(java.lang.Exception var1)
        {
            var1.printStackTrace();
            networkShutdown("disconnect.genericReason", new Object[] { "Internal exception: " + var1.toString() });
        }

        public void networkShutdown(String var1, params object[] var2)
        {
            if (_isRunning)
            {
                isTerminating = true;
                terminationReason = var1;
                field_20101_t = var2;
                (new NetworkMasterThread(this)).start();
                _isRunning = false;

                try
                {
                    socketInputStream.close();
                    socketInputStream = null;
                }
                catch (java.lang.Throwable var6)
                {
                }

                try
                {
                    socketOutputStream.close();
                    socketOutputStream = null;
                }
                catch (java.lang.Throwable var5)
                {
                }

                try
                {
                    networkSocket.close();
                    networkSocket = null;
                }
                catch (java.lang.Throwable var4)
                {
                }

            }
        }

        public void processReadPackets()
        {
            if (sendQueueByteLength > 1048576)
            {
                networkShutdown("disconnect.overflow", new Object[0]);
            }

            if (readPackets.isEmpty())
            {
                if (timeSinceLastRead++ == 1200)
                {
                    networkShutdown("disconnect.timeout", new Object[0]);
                }
            }
            else
            {
                timeSinceLastRead = 0;
            }

            int var1 = 100;

            while (!readPackets.isEmpty() && var1-- >= 0)
            {
                Packet var2 = (Packet)readPackets.remove(0);
                var2.apply(netHandler);
            }

            wakeThreads();
            if (isTerminating && readPackets.isEmpty())
            {
                netHandler.handleErrorMessage(terminationReason, field_20101_t);
            }

        }

        public void func_28142_c()
        {
            wakeThreads();
            _isServerTerminating = true;
            readThread.interrupt();
            (new ThreadCloseConnection(this)).start();
        }

        public static bool isRunning(NetworkManager var0)
        {
            return var0._isRunning;
        }

        public static bool isServerTerminating(NetworkManager var0)
        {
            return var0._isServerTerminating;
        }

        public static bool readNetworkPacket(NetworkManager var0)
        {
            return var0.readPacket();
        }

        public static bool sendNetworkPacket(NetworkManager var0)
        {
            return var0.sendPacket();
        }

        public static DataOutputStream func_28140_f(NetworkManager var0)
        {
            return var0.socketOutputStream;
        }

        public static bool func_28138_e(NetworkManager var0)
        {
            return var0.isTerminating;
        }

        public static void func_30005_a(NetworkManager var0, java.lang.Exception var1)
        {
            var0.onNetworkError(var1);
        }

        public static java.lang.Thread getReadThread(NetworkManager var0)
        {
            return var0.readThread;
        }

        public static java.lang.Thread getWriteThread(NetworkManager var0)
        {
            return var0.writeThread;
        }
    }

}