using Undefined.Events;

namespace Undefined.Networking.Events;

public class ClientConnectEventArgs : IEventArgs
{
    public Server Server { get; }
    public Server NewClient { get; }

    public ClientConnectEventArgs(Server server, Server newClient)
    {
        Server = server;
        NewClient = newClient;
    }
}