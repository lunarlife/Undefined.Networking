using System;
using System.Net;
using System.Net.Sockets;
using Networking.Events;
using Utils.Events;

namespace Networking
{
    public sealed class Server
    {
        private Socket? _socket;
        private ConnectionType _connectionType = ConnectionType.None;
        
        public ConnectionType ConnectionType => _connectionType;
        public IPAddress? Address => IsConnectedOrOpened ? (_socket!.RemoteEndPoint as IPEndPoint)!.Address.MapToIPv4() : throw new ServerException("server is closed");
        public int? Port => IsConnectedOrOpened ? (_socket!.RemoteEndPoint as IPEndPoint)!.Port : throw new ServerException("server is closed");
        
        public bool IsConnectedOrOpened => _connectionType != ConnectionType.None;

        public Socket? Socket => _socket;
        public Event<ClientConnectEventData> OnClientConnected { get; } = new();
        public void Connect(IPAddress address, int port)
        {
            if (IsConnectedOrOpened) throw new ServerException("server is connected or opened");
            _connectionType = ConnectionType.Client;
            _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            _socket.Connect(address, port);
        }

        public void Close()
        {
            if (!IsConnectedOrOpened) throw new ServerException("server is not connected or opened");
            _connectionType = ConnectionType.None;
            if(_socket!.Connected)
                _socket!.Shutdown(SocketShutdown.Both);
        }

        public void OpenServer(IPAddress address, int port)
        {
            if (IsConnectedOrOpened) throw new ServerException("server is connected or opened");
            _connectionType = ConnectionType.OpenServer;
            _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            _socket.Bind(new IPEndPoint(address, port));
            _socket.Listen(0);
            _socket.BeginAccept(Accept, null);
        }
        private void Accept(IAsyncResult result)
        {
            if (!IsConnectedOrOpened) throw new ServerException("server is connected or opened");
            if (_connectionType != ConnectionType.OpenServer)
                throw new ServerException("connection type is not client");
            var socket = _socket!.EndAccept(result);
            var client = new Server
            {
                _socket = socket,
                _connectionType = ConnectionType.Client,
            };
            var connectEvent = new ClientConnectEventData(this, client);
            OnClientConnected.Invoke(connectEvent);
            _socket.BeginAccept(Accept, null);
        }
    }

    public class ServerException : Exception
    {
        public ServerException(string msg) : base(msg)
        {

        }
    }
}