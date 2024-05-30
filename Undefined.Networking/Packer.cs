using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Undefined.Events;
using Undefined.Networking.Events;
using Undefined.Networking.Exceptions;
using Undefined.Networking.Packets;
using Undefined.Serializer;
using Undefined.Verify;

namespace Undefined.Networking;

public sealed class Packer : IDisposable, IEquatable<Packer>, IComparable<Packer>
{
    private static readonly List<Packer> Packers = new(100);
    private static readonly object PackersLock = new();
    private static readonly List<Thread> Threads = new(10);
    private static bool _isThreadPoolWorking;
    private static bool _isSenderWorking;
    private static readonly List<PacketType> PacketIds = [];
    private static readonly Dictionary<Type, PacketType> PacketTypes = [];
    private static bool _isPacketsIndexed;
    private readonly PacketDeserializer _deserializer;

    private readonly Priority _priority;
    private readonly Socket _socket;

    private bool _isWorking;

    public DataConverter Converter { get; }
    public static int Tick { get; set; } = 1;
    public static int MaxClientsCountPerThread { get; set; } = 50;

    public IEventAccess<PacketReceiveEventArgs> OnReceive => _deserializer.OnReceive;
    public IEventAccess<RequestEventArgs> OnRequest => _deserializer.OnRequest;
    public Server Server { get; }

    public static int SendPacketTick { get; set; } = 10;

    public int MaxPacketsPerTick
    {
        get => _deserializer.MaxPacketsPerTick;
        set => _deserializer.MaxPacketsPerTick = value;
    }

    public IEventAccess<PackerExceptionEventArgs> OnUnhandledException => _deserializer.OnUnhandledException;


    public Packer(Server server, Priority priority = Priority.Normal, DataConverter? converter = null)
    {
        if (!server.IsConnectedOrOpened) throw new ReaderException("The connection is closed.");
        if (!_isPacketsIndexed) ReindexPackets();
        _priority = priority;
        Converter = converter ?? DataConverter.GetDefault();
        Server = server;
        _socket = server.Socket!;
        _deserializer = new PacketDeserializer(Converter, this);
    }

    public int CompareTo(Packer? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (ReferenceEquals(null, other)) return 1;
        return _priority.CompareTo(other._priority);
    }


    public void Dispose()
    {
        _socket.Dispose();
        _deserializer.Dispose();
    }

    public bool Equals(Packer? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Server == other.Server;
    }


    private static void StartSender()
    {
        new Thread(() =>
        {
            while (_isSenderWorking)
            {
                Thread.Sleep(SendPacketTick);
                for (var i = 0; i < Packers.Count; i++)
                {
                    var packers = Packers[i];
                    packers._deserializer.SendPackets();
                }
            }
        })
        {
            Name = "Send Packer Thread"
        }.Start();
    }

    public void Open()
    {
        _isWorking = _isWorking ? throw new PackerException("Packer already working") : true;
        lock (PackersLock)
        {
            Packers.Add(this);
        }

        if (!_isThreadPoolWorking)
        {
            _isThreadPoolWorking = true;
            CheckReceiveThreads();
        }

        if (_isSenderWorking) return;
        _isSenderWorking = true;
        StartSender();
    }

    public void Close()
    {
        _isWorking = _isWorking ? false : throw new PackerException("Packer is not working.");
        lock (PackersLock)
        {
            Packers.Remove(this);
            if (Packers.Count != 0) return;
        }

        _isSenderWorking = false;
        _isThreadPoolWorking = false;
    }

    private static void CheckReceiveThreads()
    {
        var threadsCount = MathF.Ceiling((float)Packers.Count / MaxClientsCountPerThread) == 0
            ? 1
            : MathF.Ceiling((float)Packers.Count / MaxClientsCountPerThread);
        for (var i = Threads.Count; i < threadsCount; i++) StartReceiveThread(i);
    }

    private static void StartReceiveThread(int id)
    {
        var thread = new Thread(() =>
        {
            var startIndex = id * MaxClientsCountPerThread;
            while (_isThreadPoolWorking)
            {
                if (startIndex > Packers.Count)
                    break;
                CheckReceiveThreads();
                Thread.Sleep(Tick);
                for (var i = startIndex;
                     i < startIndex + MaxClientsCountPerThread;
                     i++)
                {
                    if (i > Packers.Count - 1) break;
                    var reader = Packers[i];
                    if (!reader._isWorking) continue;
                    reader._deserializer.ReceiveAll();
                }
            }
        })
        {
            Name = $"Receive Packer Thread {id}"
        };
        thread.Start();
        Threads.Add(thread);
    }

    public void SendBuffer() => _deserializer.SendPackets();

    public void WaitPacket<T>(Action<T?> callback, int timeout = 0) where T : struct, IPacket
    {
        _deserializer.WaitPacket(typeof(T), callback, timeout);
    }

    public void Request<T>(IRequest<T> send, bool compressed, int timeoutDisconnectMs) where T : struct, IPacket =>
        Request(send, compressed, timeoutDisconnectMs, null);

    public void Request<T>(IRequest<T> send, bool compressed) where T : struct, IPacket =>
        Request(send, compressed, 10000, null);

    public void Request<T>(IRequest<T> send, bool compressed, Action<T>? callback) where T : struct, IPacket =>
        Request(send, compressed, 10000, callback);

    public void Request<T>(IRequest<T> send, bool compressed, int timeoutDisconnectMs,
        Action<T>? callback)
        where T : struct, IPacket =>
        _deserializer.Request(send, compressed, callback, timeoutDisconnectMs);

    public void SendPacket<T>(T packet, bool compressed = true) where T : struct, IPacket
    {
        var packetType = GetPacketType(packet.GetType());
        if (packetType is ResponsePacketType)
            throw new PacketSendException($"Response packet {packet.GetType().Name} cant be send.");
        if (packetType is RequestPacketType)
            throw new PacketSendException(
                $"Request packet {packet.GetType().Name} cant be send. Use {nameof(Request)} method to do it.");

        _deserializer.AddPacketToSendQueue(packet, compressed);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        return obj.GetType() == GetType() && Equals((Packer)obj);
    }

    public override int GetHashCode() => (int)_priority;

    public static void ReindexPackets()
    {
        _isPacketsIndexed = true;
        PacketIds.Clear();
        PacketTypes.Clear();
        ushort id = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.FullName))
            IndexPackets(assembly.GetTypes(), ref id);
    }

    private static void IndexPackets(IEnumerable<Type> assemblyTypes, ref ushort id)
    {
        var packetInterfaceType = typeof(IPacket);
        var requestInterfaceType = typeof(IRequest);
        var requestInterfaceTypeGeneric = typeof(IRequest<>);
        foreach (var type in assemblyTypes.OrderBy(type => requestInterfaceType.IsAssignableFrom(type) ? 0 : 1))
        {
            Verify.Verify.Range(id, 0, ushort.MaxValue, $"Maximum packet types is {ushort.MaxValue}.");
            if (type == requestInterfaceTypeGeneric || type == requestInterfaceType ||
                type == packetInterfaceType) continue;
            if (requestInterfaceType.IsAssignableFrom(type))
            {
                if (!type.IsValueType)
                    throw new PacketException($"Request {type.Name} must be a struct.");
                if (type.GetInterfaces().FirstOrDefault(i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == requestInterfaceTypeGeneric) is not
                    { } iType)
                    throw new PackerException(
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

    public static PacketType GetPacketType(ushort id) =>
        PacketIds.Count <= id ? throw new PackerException($"Packet id {id} not found.") : PacketIds[id];

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
            throw new PackerException($"Type {type.FullName} is not a packet.");
        return packetType;
    }

    private static bool CheckIsValidPacket(object p) => CheckIsValidPacket(p.GetType());

    private static bool CheckIsValidPacket(Type type) => TryGetPacketType(type, out _);
}