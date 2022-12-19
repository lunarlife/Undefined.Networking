using System;

namespace Networking.DataConvert.Exceptions;

public class ConverterException : Exception
{
    public ConverterException(string msg) : base(msg)
    {
        
    }
}