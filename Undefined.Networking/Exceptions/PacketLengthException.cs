using System;

namespace Undefined.Networking.Exceptions;

public class PacketLengthException : Exception
{
    public PacketLengthException() : base($"length cant be more then {ushort.MaxValue}")
    {
    }
}