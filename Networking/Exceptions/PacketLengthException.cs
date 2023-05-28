using System;

namespace Networking;

public class PacketLengthException : Exception
{
    public PacketLengthException() : base($"length cant be more then {ushort.MaxValue}")
    {
        
    }
}