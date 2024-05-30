using System;
using Undefined.Networking.Exceptions;

namespace Undefined.Networking.Packets;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class CustomSerializerAttribute : Attribute
{
    public ICustomSerializer Serializer { get; }

    public CustomSerializerAttribute(Type serializerType)
    {
        if (!serializerType.IsClass || serializerType.IsAbstract)
            throw new PacketSendException($"{serializerType.Name} must be not abstract class.");
        if (!typeof(ICustomSerializer).IsAssignableFrom(serializerType))
            throw new PacketSendException($"{serializerType.Name} must be {nameof(ICustomSerializer)}.");
        var constructors = serializerType.GetConstructors();
        foreach (var info in constructors)
        {
            if (info.GetParameters().Length != 0) continue;
            Serializer = (ICustomSerializer)info.Invoke([]);
        }

        if (Serializer is null)
            throw new PacketSerializeException($"No empty constructors found in class {serializerType.Name}.");
    }
}