using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Networking.Exceptions;
using Networking.Packets;
using Utils;
using Utils.DataConvert;
using Utils.Enums;
using Utils.Events;
using Utils.Exceptions;

namespace Networking;

public sealed class RuntimePacketer : IEquatable<RuntimePacketer>, IComparable<RuntimePacketer>
{
    private static readonly List<RuntimePacketer> Packeters = new();
    private static readonly List<Thread> Threads = new();
    private static List<RequestInfo> _requests = new();
    private static bool _isThreadPoolWorking;
    private static bool _isSenderWorking;
    private static bool _isPacketsIndexed;
    private static Enum<PacketId> _packetIds = new();

    public Event<PacketReceiveEventData> OnReceive { get; } = new();
    public delegate void UnhandledExceptionHandler(Exception exception);
    public event UnhandledExceptionHandler UnhandledException;
    private readonly Socket _socket;
    private readonly Priority _priority;
    private readonly Queue<object> _packetsToSend = new();
    private readonly object _sendLock = new();
    private bool _isReading;

    private MemoryStream _stream = new();
    private readonly object _streamLock = new();
    public bool HasPacketsToSend => _packetsToSend.Count != 0;
    public static int Tick { get; set; } = 1;
    public static int MaxClientsCountForThread { get; set; } = 50;

    public static bool IsThreadPoolWorking
    {
        get => _isThreadPoolWorking;
        set
        {
            _isThreadPoolWorking = value;
            if (_isThreadPoolWorking)
                StartPool();
            else
            {
                Threads.Clear();
            }
        }
    }
    public static bool IsSenderWorking
    {
        get => _isSenderWorking;
        set
        {
            _isSenderWorking = value;
            if (_isSenderWorking)
                StartSender();
            else
                _isSenderWorking = value;
        }
    }

    public bool IsReading
    {
        get => _isReading;
        set
        {
            _isReading = value;
            if (_isReading)
            {
                if(Packeters.Contains(this)) return;
                Packeters.Add(this);
                Packeters.Sort();
            }
            else
            {
                _isReading = value;
                if(!Packeters.Contains(this)) return;
                Packeters.Remove(this);
            }
        }
    }

    public bool IsSending { get; set; }

    public Server Server { get; }

    public static int SendPacketTick { get; set; } = 10;
    public static int MaxPacketsPerTick { get; set; } = 20;

    public RuntimePacketer(Server server, Priority priority)
    {
        if (!server.IsConnectedOrOpened) throw new ReaderException("client is closed");
        _priority = priority;
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
                    object[] packetsToSend;
                    lock (packeter._sendLock)
                    {
                        var count = packeter._packetsToSend.Count < MaxPacketsPerTick
                            ? packeter._packetsToSend.Count
                            : MaxPacketsPerTick;
                        packetsToSend = new Object[count];

                        for (var j = 0; j < count; j++)
                            packetsToSend[j] = packeter._packetsToSend.Dequeue();
                        if (packetsToSend.Length == 0) continue;
                    }
                    try
                    {
                        SendPacketsNow(packeter, packetsToSend.ToArray());
                    }
                    catch (Exception e)
                    {
                        packeter.UnhandledException?.Invoke(e);
                    }
                }
            }
        }).Start();

    }
    private static void StartPool()
    {
        var threadsCount = MathF.Ceiling((float)Packeters.Count / MaxClientsCountForThread) == 0 ? 1 : MathF.Ceiling((float)Packeters.Count / MaxClientsCountForThread);
        for (var i = 0; i < threadsCount; i++) StartThread(i);
    }

    private static void CheckThreads()
    {
        var threadsCount = MathF.Ceiling((float)Packeters.Count / MaxClientsCountForThread) == 0 ? 1 : MathF.Ceiling((float)Packeters.Count / MaxClientsCountForThread);
        for (var i = Threads.Count; i < threadsCount; i++) StartThread(i);
    }
    private static void StartThread(int id)
    {
        var thread = new Thread(() =>
        {
            var startIndex = id * MaxClientsCountForThread;
            while (_isThreadPoolWorking)
            {
                if (startIndex > Packeters.Count)
                    break;
                CheckThreads();
                Thread.Sleep(Tick);
                for (var i = startIndex;
                     i < startIndex + MaxClientsCountForThread;
                     i++)
                {
                    if(i > Packeters.Count - 1) break;
                    var reader = Packeters[i];
                    if (!reader._isReading) continue;
                    try
                    {
                        reader.ReceiveBuffer();
                        reader.TryUnpackPacket();
                        reader.CheckBuffer();
                    }
                    catch (Exception e)
                    {
                        reader.UnhandledException?.Invoke(e);
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
        if(_stream.Length == 0) return;
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
    private void TryUnpackPacket()
    {
        var left = (int)(_stream.Length - _stream.Position);
        if (left < 2)
            return;
        var buffer = (stackalloc byte[2]);
        _ = _stream.Read(buffer);
        left -= 2;
        var packetLength = BitConverter.ToUInt16(buffer);
        var totalLength = packetLength + 2; //2 bytes for a packet id
        if (left < totalLength)
        {
            _stream.Position -= 2;
            return;
        }
        buffer = (stackalloc byte[2]);
        _ = _stream.Read(buffer);
        left -= 2;
        var packetId = BitConverter.ToUInt16(buffer);
        buffer = (stackalloc byte[packetLength]);
        _ = _stream.Read(buffer);
        left -= packetLength;
        var packet = DataConverter.Deserialize(buffer, GetPacketType(packetId))!;
        ApplyPacket(packet);
        if (left > 2) TryUnpackPacket();
    }

    private static byte[] GetBytes(object packet)
    {
        var bytes = DataConverter.Serialize(packet);
        if (bytes.Length > ushort.MaxValue) throw new PacketLengthException();
        var packetIdBytes = BitConverter.GetBytes(GetPacketId(packet.GetType()));
        var lengthBytes = BitConverter.GetBytes((ushort)bytes.Length);
        var buffer = new byte[bytes.Length + 4];
        lengthBytes.CopyTo(buffer, 0);
        packetIdBytes.CopyTo(buffer, 2);
        bytes.CopyTo(buffer,4);
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
        OnReceive.Invoke(new PacketReceiveEventData(packet));
        if (_requests.FirstOrDefault(r => r.ReceiveType == type) is not
            { } requestInfo) return;
        _requests.Remove(requestInfo);
        requestInfo.Callback?.DynamicInvoke(packet);
    }
    private static void SendPacketsNow(RuntimePacketer packeter, IEnumerable<object> packetsToSend)
    {
        if(!packeter.IsSending || !packeter.Server.IsConnectedOrOpened) return;
        var totalLength = 0;
        var bytes = new List<byte[]>();
        foreach (var o in packetsToSend)
        {
            var b = GetBytes(o);
            totalLength += b.Length;
            bytes.Add(b);
        }
        var buffer = new byte[totalLength];
        var index = 0;
        for (var i = 0; i < bytes.Count; i++)
        {
            bytes[i].CopyTo(buffer, index);
            index += bytes[i].Length;
        }
        packeter._socket.Send(buffer, SocketFlags.None);
    }

    public void Request<T, T1>(T send, Action<T1>? callback = null) 
        where T : struct 
        where T1 : struct
    {
        if (typeof(T).GetCustomAttribute<RequestPacketAttribute>() is not { } att || att.RequestType != typeof(T1))
            throw new InvalidPacketException();
        SendPacketNow(send);
        _requests.Add(new RequestInfo
        {
            Callback = callback,
            ReceiveType = typeof(T)
        });
    }

    public void SendPacket<T>(T packet) where T : struct
    {
        lock(_sendLock)
            _packetsToSend.Enqueue(packet);
    }
    public void SendPacketNow<T>(params T[] packets) where T : struct
    {
        SendPacketsNow(this, packets.Cast<object>());
    }
        
    public bool Equals(RuntimePacketer other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Server == other.Server;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((RuntimePacketer)obj);
    }

    public override int GetHashCode()
    {
        return (int)_priority;
    }

    public int CompareTo(RuntimePacketer other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (ReferenceEquals(null, other)) return 1;
        return _priority.CompareTo(other._priority);
    }

    public static void LoadPackets()
    {
        if (_isPacketsIndexed) throw new Exception("");
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.FullName))
        foreach (var t in assembly.GetTypes().Where(type => type.IsValueType &&
                                                            (type.GetCustomAttribute<PacketAttribute>(
                                                                 true) is not null ||
                                                             type.GetCustomAttribute<RequestPacketAttribute>(true) is
                                                                 not null)).OrderBy(t => t.Name))
            _packetIds.AddMember(t.Name, new PacketId(t));
        _isPacketsIndexed = true;
    }

    private static Type GetPacketType(ushort id) => _packetIds.Count <= id ? throw new Exception("unknown id " + id) : _packetIds[id].Type;
    private static ushort GetPacketId(Type type) => (ushort)_packetIds[type.Name].ID;
    private bool CheckIsValidPacket(object p) => p.GetType().GetCustomAttribute<PacketAttribute>(true) is null ? throw new InvalidPacketException() : true;
    private class RequestInfo
    {
        public Type ReceiveType;
        public Delegate? Callback;
    }

}