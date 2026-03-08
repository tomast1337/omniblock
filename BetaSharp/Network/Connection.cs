using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BetaSharp.Network.Packets;
using BetaSharp.Threading;
using java.util;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Network;

public class Connection
{
    public bool betaSharpClient = false;

    private readonly ILogger<Connection> _logger = Log.Instance.For<Connection>();
    private readonly IPEndPoint? _address;
    private readonly java.lang.Thread _writer;
    private readonly java.lang.Thread _reader;
    private readonly ManualResetEventSlim wakeSignal = new(false);

    protected bool open = true;
    protected ConcurrentQueue<Packet> readQueue = [];
    protected NetHandler? networkHandler;
    protected bool closed;
    protected bool disconnected;
    protected string disconnectedReason = "";
    protected object[]? disconnectReasonArgs;

    private int timeout;
    private int sendQueueSize;
    private ConcurrentQueue<Packet> sendQueue = [];
    private ConcurrentQueue<Packet> delayedSendQueue = [];
    private object lck = new();
    private int _delay;
    private Socket? _socket;
    private NetworkStream? _networkStream;

    public Connection(Socket socket, string address, NetHandler networkHandler)
    {
        _socket = socket;
        _address = (IPEndPoint?) socket.RemoteEndPoint;
        this.networkHandler = networkHandler;

        socket.ReceiveTimeout = 30000;
        // setTrafficClass doesn't have a direct .NET equivalent and can be omitted

        _networkStream = new NetworkStream(socket);

        _reader = new NetworkReaderThread(this, address + " read thread");
        _writer = new NetworkWriterThread(this, address + " write thread");
        _reader.start();
        _writer.start();
    }

    protected Connection()
    {
        _address = null;
    }

    public void setNetworkHandler(NetHandler netHandler)
    {
        networkHandler = netHandler;
    }

    public virtual void sendPacket(Packet packet)
    {
        if (packet is ExtendedProtocolPacket && !betaSharpClient) return;

        if (!closed)
        {
            packet.UseCount++;
            object lockObj = lck;
            lock (lockObj)
            {
                sendQueueSize += packet.Size() + 1;
                if (Packet.Registry[packet.Id]!.WorldPacket)
                {
                    delayedSendQueue.Enqueue(packet);
                }
                else
                {
                    sendQueue.Enqueue(packet);
                }

            }
        }
    }

    protected virtual bool write()
    {
        if (_networkStream == null)
        {
            throw new Exception("Connection not initialized");
        }

        bool wrotePacket = false;

        try
        {
            Packet? packet;
            object lockObj;
            if (!sendQueue.IsEmpty)
            {
                lockObj = lck;
                lock (lockObj)
                {
                    if (!sendQueue.TryDequeue(out packet))
                    {
                        return false;
                    }

                    sendQueueSize -= packet.Size() + 1;
                }

                Packet.Write(packet, _networkStream);
                wrotePacket = true;
            }

            if (_delay-- <= 0 && !delayedSendQueue.IsEmpty)
            {
                lockObj = lck;
                lock (lockObj)
                {
                    if (!delayedSendQueue.TryDequeue(out packet))
                    {
                        return false;
                    }

                    sendQueueSize -= packet.Size() + 1;
                }

                Packet.Write(packet, _networkStream);
                _delay = 0;
                wrotePacket = true;
            }

            return wrotePacket;
        }
        catch (Exception ex)
        {
            if (!disconnected)
            {
                disconnect(ex);
            }

            return false;
        }
    }

    public virtual void interrupt()
    {
        wakeSignal.Set();
    }

    public void waitForSignal(int timeoutMs)
    {
        wakeSignal.Wait(timeoutMs);
        wakeSignal.Reset();
    }

    protected virtual bool read()
    {
        if (networkHandler == null || _networkStream == null)
        {
            throw new Exception("Connection not initialized");
        }

        bool receivedPacket = false;

        try
        {
            Packet? packet = Packet.Read(_networkStream, networkHandler.isServerSide());
            if (packet != null)
            {
                readQueue.Enqueue(packet);
                receivedPacket = true;
            }
            else
            {
                disconnect("disconnect.endOfStream");
            }

            return receivedPacket;
        }
        catch (Exception ex)
        {
            if (!disconnected)
            {
                disconnect(ex);
            }

            return false;
        }
    }

    private void disconnect(Exception e)
    {
        _logger.LogError(e, e.Message);
        disconnect("disconnect.genericReason", "Internal exception: " + e);
    }

    public virtual void disconnect(string disconnectedReason, params object[] disconnectReasonArgs)
    {
        if (open)
        {
            disconnected = true;
            this.disconnectedReason = disconnectedReason;
            this.disconnectReasonArgs = disconnectReasonArgs;
            new NetworkMasterThread(this).start();
            open = false;

            try
            {
                _networkStream?.Close();
                _networkStream = null;

                _socket?.Close();
                _socket = null;
            }
            catch (Exception)
            {
                // Ignore.
            }
        }
    }

    public virtual void tick()
    {
        if (sendQueueSize > 1048576)
        {
            disconnect("disconnect.overflow");
        }

        if (readQueue.IsEmpty)
        {
            if (timeout++ == 1200)
            {
                disconnect("disconnect.timeout");
            }
        }
        else
        {
            timeout = 0;
        }

        processPackets();

        interrupt();
        if (disconnected && readQueue.IsEmpty)
        {
            networkHandler?.onDisconnected(disconnectedReason, disconnectReasonArgs);
        }

    }

    protected virtual void processPackets()
    {
        if (networkHandler == null)
        {
            throw new Exception("networkHandler is null");
        }

        int maxPacketsPerTick = 100;

        while (readQueue.TryDequeue(out var packet) && maxPacketsPerTick-- >= 0)
        {
            packet.Apply(networkHandler);
            packet.Return();
        }
    }

    public virtual IPEndPoint? getAddress()
    {
        return _address;
    }

    public virtual void disconnect()
    {
        interrupt();
        closed = true;
        new ThreadCloseConnection(this).Start();
    }

    public int getDelayedSendQueueSize()
    {
        return delayedSendQueue.Count;
    }

    public static bool isOpen(Connection conn)
    {
        return conn.open;
    }

    public static bool isClosed(Connection conn)
    {
        return conn.closed;
    }

    public static bool readPacket(Connection conn)
    {
        return conn.read();
    }

    public static bool writePacket(Connection conn)
    {
        return conn.write();
    }

    public static NetworkStream? getOutputStream(Connection conn)
    {
        return conn._networkStream;
    }

    public static bool isDisconnected(Connection conn)
    {
        return conn.disconnected;
    }

    public static void disconnect(Connection conn, Exception ex)
    {
        conn.disconnect(ex);
    }

    public static java.lang.Thread getReader(Connection conn)
    {
        return conn._reader;
    }

    public static java.lang.Thread getWriter(Connection conn)
    {
        return conn._writer;
    }
}
