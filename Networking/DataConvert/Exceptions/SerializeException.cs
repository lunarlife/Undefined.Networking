using System;

namespace Networking.DataConvert.Exceptions;

public class SerializeException : Exception
{
    public SerializeException(string msg) : base(msg)
    {
        
    }
}