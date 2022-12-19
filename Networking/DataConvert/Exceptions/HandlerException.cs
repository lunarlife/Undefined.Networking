using System;

namespace Networking.DataConvert.Exceptions;

public class HandlerException : Exception
{
    public HandlerException(string msg) : base(msg)
    {
        
    }
}