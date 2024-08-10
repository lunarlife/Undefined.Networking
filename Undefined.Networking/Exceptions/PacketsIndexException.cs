using System;

namespace Undefined.Networking.Exceptions;

public class PacketsIndexException : Exception
{
    public PacketsIndexException(string? message) : base(message)
    {
    }
}