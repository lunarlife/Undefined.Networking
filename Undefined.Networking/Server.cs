using System;
using System.Net;
using System.Net.Sockets;
using Undefined.Events;
using Undefined.Networking.Events;

namespace Undefined.Networking;

public sealed class Server : IDisposable
{
    private readonly Event<ClientConnectEventArgs> _onClientConnected = new();

    public ConnectionType ConnectionType { get; private set; } = ConnectionType.None;
    public ProtocolType ProtocolType { get; private set; } = ProtocolType.Unknown;
    
    public IPAddress? Address => IsConnectedOrOpened
        ? (Socket!.RemoteEndPoint as IPEndPoint)!.Address.MapToIPv4()
        : throw new ServerException("Server is closed.");

    public int? Port => IsConnectedOrOpened
        ? (Socket!.RemoteEndPoint as IPEndPoint)!.Port
        : throw new ServerException("Server is closed.");

    public bool IsConnectedOrOpened => ConnectionType != ConnectionType.None;

    public Socket? Socket { get; private set; }

    public IEventAccess<ClientConnectEventArgs> OnClientConnected => _onClientConnected.Access;

    public void Dispose()
    {
        if (ConnectionType != ConnectionType.None) Close();

        Socket?.Dispose();
    }

    public void Connect(IPAddress address, int port, ProtocolType protocolType = ProtocolType.Tcp)
    {
        if (IsConnectedOrOpened) throw new ServerException("Server is connected or opened.");
        ConnectionType = ConnectionType.Client;
        ProtocolType = protocolType;
        Socket = new Socket(SocketType.Stream, protocolType);
        Socket.Connect(address, port);
    }

    public void Close()
    {
        if (!IsConnectedOrOpened) throw new ServerException("Server is not connected or opened.");
        ConnectionType = ConnectionType.None;
        ProtocolType = ProtocolType.Unknown;
        if (Socket!.Connected)
            Socket!.Shutdown(SocketShutdown.Both);
    }

    public void OpenServer(IPAddress address, int port, ProtocolType protocolType = ProtocolType.Tcp)
    {
        if (IsConnectedOrOpened) throw new ServerException("Server is connected or opened.");
        ConnectionType = ConnectionType.OpenServer;
        ProtocolType = protocolType;
        Socket = new Socket(SocketType.Stream, protocolType);
        Socket.Bind(new IPEndPoint(address, port));
        Socket.Listen(0);
        Socket.BeginAccept(Accept, null);
    }

    private void Accept(IAsyncResult result)
    {
        if (!IsConnectedOrOpened) throw new ServerException("Server is connected or opened.");
        if (ConnectionType != ConnectionType.OpenServer)
            throw new ServerException("Connection type is not client.");
        var socket = Socket!.EndAccept(result);
        var client = new Server
        {
            Socket = socket,
            ConnectionType = ConnectionType.Client
        };
        var connectEvent = new ClientConnectEventArgs(this, client);
        _onClientConnected.Raise(connectEvent);
        Socket.BeginAccept(Accept, null);
    }
}

public class ServerException : Exception
{
    public ServerException(string msg) : base(msg)
    {
    }
}