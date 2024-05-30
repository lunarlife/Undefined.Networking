using System;

namespace Undefined.Networking.Exceptions;

public class PacketSendException : Exception
{
    public PacketSendException(string? message) : base(message)
    {
    }
}