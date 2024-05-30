using System;

namespace Undefined.Networking.Exceptions;

public class ResponseException : PacketException
{
    public ResponseException(string? message) : base(message)

    {
    }
}

public class PacketException : Exception
{
    public PacketException(string? message) : base(message)
    {
    }
}