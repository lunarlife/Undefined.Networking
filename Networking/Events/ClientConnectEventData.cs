using Utils.Events;

namespace Networking.Events
{
    public class ClientConnectEventData : EventData
    {
        public Server Server { get; }
        public Server Client { get; }

        public ClientConnectEventData(Server server, Server client)
        {
            Server = server;
            Client = client;
        }
    }
}