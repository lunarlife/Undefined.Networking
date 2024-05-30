using Undefined.Events;
using Undefined.Networking.Exceptions;

namespace Undefined.Networking.Events;

public class PackerExceptionEventArgs : IEventArgs
{
    public PackerException Exception { get; }

    public PackerExceptionEventArgs(PackerException exception)
    {
        Exception = exception;
    }
}