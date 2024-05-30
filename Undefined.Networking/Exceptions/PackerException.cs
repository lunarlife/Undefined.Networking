using System;

namespace Undefined.Networking.Exceptions;

public sealed class PackerException : Exception
{
    public PackerException(string? message = null) : base(message)
    {
    }
}