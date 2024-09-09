using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Undefined.Networking.Exceptions;
using Undefined.Networking.Packets;
using Undefined.Verifying;

namespace Undefined.Networking;

public static class Indexer
{
    private static readonly List<PacketType> PacketIds = [];
    private static readonly Dictionary<Type, PacketType> PacketTypes = [];

    public static bool IsIndexed { get; private set; }


    public static void IndexSpecificTypes(bool clearOld, params Type[] types)
    {
        if (clearOld)
            RemoveIndexedPackets();
        var id = (ushort)PacketIds.Count;
        IndexPackets(types, ref id);
        IsIndexed = true;
    }

    public static void ReindexAllPackets(bool clearOld) =>
        IndexSpecificAssemblies(clearOld, AppDomain.CurrentDomain.GetAssemblies());

    public static void IndexSpecificAssemblies(bool clearOld, params string[] assemblies) =>
        IndexSpecificAssemblies(clearOld, LoadAssemblies(assemblies));

    public static void IndexSpecificAssemblies(bool clearOld, params Assembly[] assemblies) =>
        IndexSpecificAssemblies(clearOld, assemblies.AsEnumerable());

    public static void IndexSpecificAssemblies(bool clearOld, IEnumerable<Assembly> assemblies)
    {
        if (clearOld)
            RemoveIndexedPackets();
        var id = (ushort)PacketIds.Count;
        foreach (var assembly in assemblies.OrderBy(a => a.FullName))
            IndexPackets(assembly.GetTypes(), ref id);
        IsIndexed = true;
    }

    public static void RemoveIndexedPackets(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies) RemoveIndexedPackets(assembly.GetTypes());
    }

    public static void RemoveIndexedType<T>() where T : struct, IPacket => RemoveIndexedPackets(typeof(T));
    public static void IndexType<T>(bool clearOld) where T : struct, IPacket => IndexSpecificTypes(clearOld, typeof(T));

    public static void RemoveIndexedPackets(params Type[] types)
    {
        foreach (var type in types)
        {
            if (!PacketTypes.Remove(type, out var packetType)) continue;
            var index = PacketIds.IndexOf(packetType);
            PacketIds.RemoveAt(index);
            for (var i = index; i < PacketIds.Count; i++) PacketIds[i].Id = (ushort)i;
        }
    }
    public static void RemoveIndexedPackets()
    {
        IsIndexed = false;
        PacketIds.Clear();
        PacketTypes.Clear();
    }

    private static IEnumerable<Assembly> LoadAssemblies(IEnumerable<string> assemblies) =>
        assemblies.Select(assemblyName => AppDomain.CurrentDomain.Load(assemblyName));

    private static void IndexPackets(IEnumerable<Type> assemblyTypes, ref ushort id)
    {
        var packetInterfaceType = typeof(IPacket);
        var requestInterfaceType = typeof(IRequest);
        var requestInterfaceTypeGeneric = typeof(IRequest<>);
        foreach (var type in assemblyTypes.OrderBy(type => type.Name)
                     .ThenBy(type => requestInterfaceType.IsAssignableFrom(type) ? 0 : 1))
        {
            Verify.Range(id, 0, ushort.MaxValue, $"Maximum packet types is {ushort.MaxValue}.");
            if (type == requestInterfaceTypeGeneric || type == requestInterfaceType ||
                type == packetInterfaceType) continue;
            if (PacketTypes.ContainsKey(type)) continue;
            if (requestInterfaceType.IsAssignableFrom(type))
            {
                if (!type.IsValueType)
                    throw new PacketException($"Request {type.Name} must be a struct.");
                if (type.GetInterfaces().FirstOrDefault(i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == requestInterfaceTypeGeneric) is not
                    { } iType)
                    throw new PacketsIndexException(
                        $"Request packet packet must be inherited from {requestInterfaceTypeGeneric}.");
                var serializer = type.GetCustomAttribute<CustomSerializerAttribute>()?.Serializer;
                var responseTypeValue = iType.GetGenericArguments()[0];
                var requestType = new RequestPacketType(type, responseTypeValue, id, serializer);
                id++;
                var responseType = new ResponsePacketType(responseTypeValue, type, id, serializer);
                if (!PacketTypes.TryAdd(responseTypeValue, responseType))
                {
                    var value = PacketTypes[responseTypeValue];
                    throw new PacketException(
                        $"Request packet {type.Name} has the same response type with {value.Type.Name}.");
                }

                PacketTypes.Add(type, requestType);
                PacketIds.Add(requestType);
                PacketIds.Add(responseType);
            }
            else if (packetInterfaceType.IsAssignableFrom(type))
            {
                if (!type.IsValueType)
                    throw new PacketException($"Packet {type.Name} must be a struct.");
                var serializer = type.GetCustomAttribute<CustomSerializerAttribute>()?.Serializer;
                var packetType = new DefaultPacketType(type, id, serializer);
                if (PacketTypes.TryAdd(type, packetType))
                    PacketIds.Add(packetType);
                else continue;
            }
            else continue;

            id++;
        }
    }

    public static bool IsValidPacket(Type type) => TryGetPacketType(type, out _);

    public static PacketType GetPacketType(ushort id) =>
        PacketIds.Count <= id ? throw new PacketsIndexException($"Packet id {id} not found.") : PacketIds[id];

    public static bool TryGetPacketType(Type type, out PacketType? packetType) =>
        PacketTypes.TryGetValue(type, out packetType);

    public static bool TryGetPacketType(ushort id, out PacketType? packetType)
    {
        if (PacketIds.Count <= id)
        {
            packetType = null;
            return false;
        }

        packetType = PacketIds[id];
        return true;
    }

    public static PacketType GetPacketType(Type type)
    {
        if (!PacketTypes.TryGetValue(type, out var packetType))
            throw new PacketsIndexException($"Type {type.FullName} is not a packet.");
        return packetType;
    }
}