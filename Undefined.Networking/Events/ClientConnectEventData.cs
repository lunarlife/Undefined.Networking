using Undefined.Events;

namespace Undefined.Networking.Events;

public class ClientConnectEventData : IEventArgs
{
    public Server Server { get; }
    public Server NewClient { get; }

    public ClientConnectEventData(Server server, Server newClient)
    {
        Server = server;
        NewClient = newClient;
    }
}