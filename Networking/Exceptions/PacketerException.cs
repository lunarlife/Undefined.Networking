using System;

namespace Networking.Exceptions;

public sealed class PacketerException : Exception
{
    public PacketerException(string? message = null) : base(message)
    {
    }
}