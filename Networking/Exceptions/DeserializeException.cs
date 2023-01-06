using System;

namespace Networking.Exceptions;

public class DeserializeException : Exception
{
    public DeserializeException(string msg) : base(msg)
    {
        
    }
}