using System;

namespace Networking.DataConvert.Exceptions;

public class DeserializeException : Exception
{
    public DeserializeException(string msg) : base(msg)
    {
        
    }
}