using System;

namespace Undefined.Networking.Exceptions;

public class PacketSerializeException : Exception
{
    public PacketSerializeException(string? message) : base(message)
    {
    }
}