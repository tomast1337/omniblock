using betareborn.Server.Threading;
using java.lang;
using java.net;
using java.util.logging;

namespace betareborn.Server.Network
{
    public class ConnectionListener
    {
        public static Logger LOGGER = Logger.getLogger("Minecraft");
        public ServerSocket socket;
        private java.lang.Thread thread;
        public volatile bool open = false;
        public int connectionCounter = 0;
        private List<ServerLoginNetworkHandler> pendingConnections = [];
        private List<ServerPlayNetworkHandler> connections = [];
        public MinecraftServer server;
        public int port;

        public ConnectionListener(MinecraftServer server, InetAddress address, int port)
        {
            this.server = server;
            socket = new ServerSocket(port, 0, address);
            socket.setPerformancePreferences(0, 2, 1);
            this.port = socket.getLocalPort();
            open = true;
            thread = new AcceptConnectionThread(this, "Listen Thread");
            thread.start();
        }

        public void addConnection(ServerPlayNetworkHandler connection)
        {
            connections.Add(connection);
        }

        public void addPendingConnection(ServerLoginNetworkHandler connection)
        {
            if (connection == null)
            {
                throw new IllegalArgumentException("Got null pendingconnection!");
            }
            else
            {
                pendingConnections.Add(connection);
            }
        }

        public void tick()
        {
            for (int var1 = 0; var1 < pendingConnections.Count; var1++)
            {
                ServerLoginNetworkHandler var2 = pendingConnections[var1];

                try
                {
                    var2.tick();
                }
                catch (java.lang.Exception var5)
                {
                    var2.disconnect("Internal server error");
                    LOGGER.log(Level.WARNING, "Failed to handle packet: " + var5, var5);
                }

                if (var2.closed)
                {
                    pendingConnections.RemoveAt(var1--);
                }

                var2.connection.interrupt();
            }

            for (int var6 = 0; var6 < connections.Count; var6++)
            {
                ServerPlayNetworkHandler var7 = connections[var6];

                try
                {
                    var7.tick();
                }
                catch (java.lang.Exception var4)
                {
                    LOGGER.log(Level.WARNING, "Failed to handle packet: " + var4, (Throwable)var4);
                    var7.disconnect("Internal server error");
                }

                if (var7.disconnected)
                {
                    connections.RemoveAt(var6--);
                }

                var7.connection.interrupt();
            }
        }
    }

}
