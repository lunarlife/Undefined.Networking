using System;

namespace Networking.Exceptions;

public class SerializeException : Exception
{
    public SerializeException(string msg) : base(msg)
    {
        
    }
}