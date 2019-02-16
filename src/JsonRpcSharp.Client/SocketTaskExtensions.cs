using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

#if !NETSTANDARD2_0
namespace JsonRpcSharp.Client
{
    /// <summary>
    /// Missing from net46/net461
    /// https://github.com/dotnet/corefx/blob/master/src/System.Net.Sockets/src/System/Net/Sockets/SocketTaskExtensions.cs
    /// </summary>
    public static class SocketTaskExtensions
    {
        public static Task ConnectAsync(this Socket socket, EndPoint remoteEP) =>
            socket.ConnectAsync(remoteEP);
        public static Task ConnectAsync(this Socket socket, IPAddress address, int port) =>
            socket.ConnectAsync(address, port);
        public static Task<int> SendAsync(this Socket socket, ArraySegment<byte> buffer, SocketFlags socketFlags) =>
           socket.SendAsync(buffer, socketFlags);
        public static Task<int> ReceiveAsync(this Socket socket, ArraySegment<byte> buffer, SocketFlags socketFlags) =>
           socket.ReceiveAsync(buffer, socketFlags);
    }
}
#endif
