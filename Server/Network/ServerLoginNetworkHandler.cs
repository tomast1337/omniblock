using betareborn.Entities;
using betareborn.Network;
using betareborn.Network.Packets;
using betareborn.Network.Packets.Play;
using betareborn.Network.Packets.S2CPlay;
using betareborn.Util.Maths;
using betareborn.Worlds;
using java.lang;
using java.net;
using java.util.logging;

using betareborn.Server.Internal;

namespace betareborn.Server.Network
{
    public class ServerLoginNetworkHandler : NetHandler
    {
        public static Logger LOGGER = Logger.getLogger("Minecraft");
        private static java.util.Random random = new();
        public Connection connection;
        public bool closed = false;
        private MinecraftServer server;
        private int loginTicks = 0;
        private string username = null;
        private LoginHelloPacket loginPacket = null;
        private string serverId = "";

        public ServerLoginNetworkHandler(MinecraftServer server, Socket socket, string name)
        {
            this.server = server;
            connection = new Connection(socket, name, this);
            connection.lag = 0;
        }

        public void tick()
        {
            if (loginPacket != null)
            {
                accept(loginPacket);
                loginPacket = null;
            }

            if (loginTicks++ == 600)
            {
                disconnect("Took too long to log in");
            }
            else
            {
                connection.tick();
            }
        }

        public void disconnect(string reason)
        {
            try
            {
                LOGGER.info("Disconnecting " + getConnectionInfo() + ": " + reason);
                connection.sendPacket(new DisconnectPacket(reason));
                connection.disconnect();
                closed = true;
            }
            catch (java.lang.Exception var3)
            {
                var3.printStackTrace();
            }
        }

        public override void onHandshake(HandshakePacket packet)
        {
            if (server.onlineMode)
            {
                serverId = Long.toHexString(random.nextLong());
                connection.sendPacket(new HandshakePacket(serverId));
            }
            else
            {
                connection.sendPacket(new HandshakePacket("-"));
            }
        }

        public override void onHello(LoginHelloPacket packet)
        {
            if (server is InternalServer)
            {
                packet.username = "player";
            }
            username = packet.username;
            if (packet.protocolVersion != 14)
            {
                if (packet.protocolVersion > 14)
                {
                    disconnect("Outdated server!");
                }
                else
                {
                    disconnect("Outdated client!");
                }
            }
            else
            {
                if (!server.onlineMode)
                {
                    accept(packet);
                }
                else
                {
                    //TODO: ADD SOME KIND OF AUTH
                    //new C_15575233(this, packet).start();
                    throw new IllegalStateException("Auth not supported");
                }
            }
        }

        public void accept(LoginHelloPacket packet)
        {
            ServerPlayerEntity var2 = server.playerManager.connectPlayer(this, packet.username);
            if (var2 != null)
            {
                server.playerManager.loadPlayerData(var2);
                var2.setWorld(server.getWorld(var2.dimensionId));
                LOGGER.info(getConnectionInfo() + " logged in with entity id " + var2.id + " at (" + var2.x + ", " + var2.y + ", " + var2.z + ")");
                ServerWorld var3 = server.getWorld(var2.dimensionId);
                Vec3i var4 = var3.getSpawnPos();
                ServerPlayNetworkHandler var5 = new ServerPlayNetworkHandler(server, connection, var2);
                var5.sendPacket(new LoginHelloPacket("", var2.id, var3.getSeed(), (sbyte)var3.dimension.id));
                var5.sendPacket(new PlayerSpawnPositionS2CPacket(var4.x, var4.y, var4.z));
                server.playerManager.sendWorldInfo(var2, var3);
                server.playerManager.sendToAll(new ChatMessagePacket("§e" + var2.name + " joined the game."));
                server.playerManager.addPlayer(var2);
                var5.teleport(var2.x, var2.y, var2.z, var2.yaw, var2.pitch);
                server.connections.addConnection(var5);
                var5.sendPacket(new WorldTimeUpdateS2CPacket(var3.getTime()));
                var2.initScreenHandler();
            }

            closed = true;
        }

        public override void onDisconnected(string reason, object[] objects)
        {
            LOGGER.info(getConnectionInfo() + " lost connection");
            closed = true;
        }

        public override void handle(Packet packet)
        {
            disconnect("Protocol error");
        }

        public string getConnectionInfo()
        {
            return username != null ? username + " [" + connection.getAddress().toString() + "]" : connection.getAddress().toString();
        }

        public override bool isServerSide()
        {
            return true;
        }
    }

}