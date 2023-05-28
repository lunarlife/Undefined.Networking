using System;
using System.Reflection;
using Networking.Exceptions;

namespace Networking.Packets;

[AttributeUsage(AttributeTargets.Struct)]
public sealed class RequestPacketAttribute<T> : RequestPacketAttribute where T : struct
{
    public RequestPacketAttribute() : base(typeof(T))
    {
    }
}
public class RequestPacketAttribute : Attribute
{
    public Type RequestType { get; }

    internal RequestPacketAttribute(Type requestType)
    {
        if (requestType.GetCustomAttribute<PacketAttribute>() is null)
            throw new InvalidPacketException();
        RequestType = requestType;
    }
}