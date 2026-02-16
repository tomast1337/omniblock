using System;
using System.Net;
using System.Net.Sockets;

namespace BetaSharp.Launcher.Features.New.Authentication.Extensions;

internal static class SocketExtensions
{
    extension(Socket)
    {
        public static int GetAvailablePort()
        {
            var endPoint = new IPEndPoint(IPAddress.Loopback, 0);

            using var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            socket.Bind(endPoint);
            socket.Listen();

            var local = (IPEndPoint?)socket.LocalEndPoint;

            ArgumentNullException.ThrowIfNull(local);

            return local.Port;
        }
    }
}
