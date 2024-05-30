using System;

namespace Undefined.Networking.Exceptions;

public class ReaderException : Exception
{
    public ReaderException(string msg) : base(msg)
    {
    }
}