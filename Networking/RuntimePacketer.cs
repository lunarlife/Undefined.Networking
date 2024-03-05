using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Networking.Events;
using Networking.Exceptions;
using Networking.Packets;
using Undefined.Events;
using Undefined.Serializer;

namespace Networking;

public sealed class RuntimePacketer : IEquatable<RuntimePacketer>, IComparable<RuntimePacketer>
{
    private static readonly List<RuntimePacketer> Packeters = new(100);
    private static readonly object PacketersLock = new();
    private static readonly List<Thread> Threads = new(capacity: 10);
    private static readonly List<RequestInfo> Requests = new(20);
    private static bool _isThreadPoolWorking;
    private static bool _isSenderWorking;
    private static bool _isPacketsIndexed;
    private static readonly Dictionary<int, PacketId> PacketIds = new();
    private static readonly Dictionary<Type, PacketId> PacketTypes = new();
    public DataConverter Converter { get; }


    public delegate void UnhandledExceptionHandler(Exception exception);

    public event UnhandledExceptionHandler UnhandledException;
    private readonly Socket _socket;
    private readonly Priority _priority;
    private readonly Queue<PacketInfo> _packetsToSend = new();
    private readonly object _sendLock = new();
    private bool _isWorking;

    private MemoryStream _stream = new();
    private readonly object _streamLock = new();
    private readonly Event<PacketReceiveEventArgs> _onReceive = new();
    public bool HasPacketsToSend => _packetsToSend.Count != 0;
    public static int Tick { get; set; } = 1;
    public static int MaxClientsCountForThread { get; set; } = 50;

    public IEventAccess<PacketReceiveEventArgs> OnReceive => _onReceive.Access;
    public Server Server { get; }

    public static int SendPacketTick { get; set; } = 10;
    public static int MaxPacketsPerTick { get; set; } = 20;

    public RuntimePacketer(Server server, Priority priority, DataConverter? converter = null)
    {
        if (!server.IsConnectedOrOpened) throw new ReaderException("client is closed");
        if (!_isPacketsIndexed) IndexPackets();
        _priority = priority;
        Converter = converter ?? DataConverter.GetDefault();
        Server = server;
        _socket = server.Socket!;
    }


    private static void StartSender()
    {
        new Thread(() =>
        {
            while (_isSenderWorking)
            {
                Thread.Sleep(SendPacketTick);
                for (var i = 0; i < Packeters.Count; i++)
                {
                    var packeter = Packeters[i];
                    PacketInfo[] packetsToSend;
                    lock (packeter._sendLock)
                    {
                        var count = packeter._packetsToSend.Count < MaxPacketsPerTick
                            ? packeter._packetsToSend.Count
                            : MaxPacketsPerTick;
                        packetsToSend = new PacketInfo[count];

                        for (var j = 0; j < count; j++)
                            packetsToSend[j] = packeter._packetsToSend.Dequeue();
                        if (packetsToSend.Length == 0) continue;
                    }

                    try
                    {
                        packeter.SendPacketsNowLocal(packetsToSend);
                    }
                    catch (Exception e)
                    {
                        packeter.UnhandledException?.Invoke(e);
                    }
                }
            }
        }).Start();
    }
    public void Run()
    {
        _isWorking = _isWorking ? throw new PacketerException("Packeter already working") : true;
        lock (PacketersLock)
            Packeters.Add(this);
        if (!_isThreadPoolWorking)
        {
            _isThreadPoolWorking = true;
            CheckReceiveThreads();
        }

        if (!_isSenderWorking)
        {
            _isSenderWorking = true;
            StartSender();
        }
    }

    public void Stop()
    {
        _isWorking = _isWorking ? false : throw new PacketerException("Packeter is not working");
        lock (PacketersLock)
        {
            Packeters.Remove(this);
            if (Packeters.Count != 0) return;
        }

        _isSenderWorking = false;
        _isThreadPoolWorking = false;
    }
    private static void CheckReceiveThreads()
    {
        var threadsCount = MathF.Ceiling((float)Packeters.Count / MaxClientsCountForThread) == 0
            ? 1
            : MathF.Ceiling((float)Packeters.Count / MaxClientsCountForThread);
        for (var i = Threads.Count; i < threadsCount; i++) StartReceiveThread(i);
    }

    private static void StartReceiveThread(int id)
    {
        var thread = new Thread(() =>
        {
            var startIndex = id * MaxClientsCountForThread;
            while (_isThreadPoolWorking)
            {
                if (startIndex > Packeters.Count)
                    break;
                CheckReceiveThreads();
                Thread.Sleep(Tick);
                for (var i = startIndex;
                     i < startIndex + MaxClientsCountForThread;
                     i++)
                {
                    if (i > Packeters.Count - 1) break;
                    var reader = Packeters[i];
                    if (!reader._isWorking) continue;
                    try
                    {
                        lock (reader._streamLock)
                        {
                            reader.ReceiveBuffer();
                            reader.UnpackPackets();
                            reader.CheckBuffer();
                        }
                    }
                    catch (Exception e)
                    {
                        reader.UnhandledException.Invoke(e);
                    }
                }
            }
        })
        {
            Name = $"RuntimePacketerThread {id}"
        };
        thread.Start();
        Threads.Add(thread);
    }

    private void CheckBuffer()
    {
        if (_stream.Length == 0) return;
        var length = (int)(_stream.Length - _stream.Position);
        if (length > 0)
        {
            var buffer = (stackalloc byte[length]);
            _ = _stream.Read(buffer);
            _stream = new MemoryStream();
            _stream.Write(buffer);
            _stream.Position = 0;
        }
        else
            _stream = new MemoryStream();
    }

    private void UnpackPackets()
    {
        while (true)
        {
            var left = (int)(_stream.Length - _stream.Position);
            if (left < 2) return;
            var buffer = (stackalloc byte[2]);
            _ = _stream.Read(buffer);
            left -= 2;
            var packetLength = Converter.Deserialize<ushort>(buffer);
            var totalLength = packetLength + 2; //2 bytes for a packet id
            if (left < totalLength)
            {
                _stream.Position -= 2;
                return;
            }

            buffer = (stackalloc byte[3]);
            _ = _stream.Read(buffer);
            left -= 2;
            var packetId = Converter.Deserialize<ushort>(buffer);
            buffer = (stackalloc byte[packetLength]);
            _ = _stream.Read(buffer);
            left -= packetLength;
            var packet = Converter.Deserialize(GetPacketType(packetId), buffer)!;
            ApplyPacket(packet);
            if (left > 2) continue;
            break;
        }
    }

    private byte[] GetBytes(PacketInfo info)
    {
        var bytes = Converter.Serialize(info.Packet, info.Compressed);
        if (bytes.Length > ushort.MaxValue) throw new PacketLengthException();
        var packetIdBytes = Converter.Serialize(GetPacketId(info.Packet.GetType()), false);
        var lengthBytes = Converter.Serialize((ushort)bytes.Length, false);
        var buffer = new byte[bytes.Length + 4];
        lengthBytes.CopyTo(buffer, 0);
        packetIdBytes.CopyTo(buffer, 2);
        bytes.CopyTo(buffer, 4);
        return buffer;
    }

    private void ReceiveBuffer()
    {
        var length = _socket.Available;
        if (length == 0) return;
        var buffer = (stackalloc byte[length]);
        _socket.Receive(buffer);
        _stream.Position = _stream.Length;
        _stream.Write(buffer);
        _stream.Position = 0;
    }

    private void ApplyPacket(object packet)
    {
        var type = packet.GetType();
        _onReceive.Raise(new PacketReceiveEventArgs(packet));
        foreach (var request in Requests)
        {
            if (request.ReceiveType != type) continue;
            Requests.Remove(request);
            request.Callback?.DynamicInvoke(packet);
        }
    }

    private void SendPacketsNowLocal(IEnumerable<PacketInfo> packetsToSend)
    {
        if (!_isWorking || !Server.IsConnectedOrOpened) return;
        var bytes = new List<byte[]>();
        foreach (var o in packetsToSend)
        {
            var b = GetBytes(o);
            bytes.Add(b);
        }

        _socket.Send(ConvertUtils.Combine(bytes), SocketFlags.None);
    }

    public void Request<T, T1>(T send, Action<T1>? callback = null)
        where T : struct
        where T1 : struct
    {
        if (typeof(T).GetCustomAttribute<RequestPacketAttribute>() is not { } att || att.RequestType != typeof(T1))
            throw new InvalidPacketException();
        SendPacketsNow(send);
        Requests.Add(new RequestInfo
        {
            Callback = callback,
            ReceiveType = typeof(T)
        });
    }

    public void SendPacket<T>(T packet, bool compressed = true) where T : struct
    {
        lock (_sendLock)
            _packetsToSend.Enqueue(new PacketInfo
            {
                Compressed = compressed
            });
    }

    public void SendPacketsNow<T>(params T[] packets) where T : struct => SendPacketsNow(true, packets);

    public void SendPacketsNow<T>(bool compressed, params T[] packets) where T : struct
    {
        var pInfos = new PacketInfo[packets.Length];
        for (var i = 0; i < packets.Length; i++)
        {
            pInfos[i] = new PacketInfo
            {
                Compressed = compressed,
                Packet = packets[i]
            };
        }

        SendPacketsNowLocal(pInfos);
    }

    public bool Equals(RuntimePacketer? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Server == other.Server;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((RuntimePacketer)obj);
    }

    public override int GetHashCode() => (int)_priority;

    public int CompareTo(RuntimePacketer? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (ReferenceEquals(null, other)) return 1;
        return _priority.CompareTo(other._priority);
    }

    public static void IndexPackets()
    {
        if (_isPacketsIndexed) throw new Exception("Packets already indexed.");
        _isPacketsIndexed = true;
        var i = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.FullName))
        foreach (var t in assembly.GetTypes().Where(type => type.IsValueType &&
                                                            (type.GetCustomAttribute<PacketAttribute>(
                                                                 true) is not null ||
                                                             type.GetCustomAttribute<RequestPacketAttribute>(true) is
                                                                 not null)).OrderBy(t => t.Name))
        {
            PacketIds.Add(i, new PacketId(t, i));
            PacketTypes.Add(t, new PacketId(t, i));
            i++;
        }
    }

    private static Type GetPacketType(ushort id) =>
        PacketIds.Count <= id ? throw new Exception("unknown id " + id) : PacketIds[id].Type;

    private static ushort GetPacketId(Type type) => (ushort)PacketTypes[type].Id;

    private bool CheckIsValidPacket(object p) => p.GetType().GetCustomAttribute<PacketAttribute>(true) is null
        ? throw new InvalidPacketException()
        : true;

    private struct RequestInfo
    {
        public Type ReceiveType;
        public Delegate? Callback;
    }

    private struct PacketInfo
    {
        public object Packet;
        public bool Compressed;
    }
}