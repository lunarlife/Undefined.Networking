using System;

namespace Networking.Exceptions;

public class HandlerException : Exception
{
    public HandlerException(string msg) : base(msg)
    {
        
    }
}