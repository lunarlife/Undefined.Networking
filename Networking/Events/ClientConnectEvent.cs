using Utils.Events;

namespace Networking.Events
{
    public class ClientConnectEvent : Event
    {
        public Server Server { get; }
        public Server Client { get; }

        public ClientConnectEvent(Server server, Server client)
        {
            Server = server;
            Client = client;
        }
    }
}