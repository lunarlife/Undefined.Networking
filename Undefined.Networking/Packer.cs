using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using Undefined.Events;
using Undefined.Networking.Events;
using Undefined.Networking.Exceptions;
using Undefined.Networking.Packets;
using Undefined.Serializer;
using Undefined.Verifying;

namespace Undefined.Networking;

public sealed class Packer : IDisposable, IEquatable<Packer>, IComparable<Packer>
{
    private static readonly List<Packer> ActivePackers = [];
    private static readonly object PackersLock = new();
    private static readonly List<Thread> ReceiveThreads = [];
    private static bool _isReceiverWorking;
    private static bool _isSenderWorking;
    private static int _maxClientsCountPerThread = 50;
    private static int _receiveTick = 1;
    private static int _sendTick = 10;

    public static int MaxClientsCountPerThread
    {
        get => _maxClientsCountPerThread;
        set
        {
            Verify.Min(value, 1);
            _maxClientsCountPerThread = value;
        }
    }

    public static int SendPacketTick
    {
        get => _sendTick;
        set
        {
            Verify.Min(value, 0);
            _sendTick = value;
        }
    }

    public static int ReceiveTick
    {
        get => _receiveTick;
        set
        {
            Verify.Min(value, 0);
            _receiveTick = value;
        }
    }

    private readonly PacketDeserializer _deserializer;
    private readonly Priority _priority;
    private readonly Socket _socket;
    private bool _isActivated;

    public IEventAccess<PacketReceiveEventArgs> OnReceive => _deserializer.OnReceive;
    public IEventAccess<RequestEventArgs> OnRequest => _deserializer.OnRequest;

    public DataConverter Converter { get; }
    public Server Server { get; }
    public bool IsActivated => _isActivated;
    public IEventAccess<PackerExceptionEventArgs> OnUnhandledException => _deserializer.OnUnhandledException;

    public int MaxPacketsPerTick
    {
        get => _deserializer.MaxPacketsPerTick;
        set
        {
            Verify.Min(value, 1);
            _deserializer.MaxPacketsPerTick = value;
        }
    }

    public Packer(Server server, Priority priority = Priority.Normal, DataConverter? converter = null)
    {
        if (!server.IsConnectedOrOpened) throw new ReaderException("The connection is closed.");
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


    public void Activate()
    {
        CheckIsPacketsIndexed();
        _isActivated = _isActivated ? throw new PackerException("Packer already activated.") : true;
        lock (PackersLock)
        {
            ActivePackers.Add(this);
        }

        if (!_isReceiverWorking)
        {
            _isReceiverWorking = true;
            CheckReceiveThreads();
        }

        if (_isSenderWorking) return;
        _isSenderWorking = true;
        StartSender();
    }

    public void Deactivate()
    {
        CheckIsActivated();
        _isActivated = false;
        lock (PackersLock)
        {
            ActivePackers.Remove(this);
            if (ActivePackers.Count != 0) return;
        }

        _isSenderWorking = false;
        _isReceiverWorking = false;
    }

    private static void CheckReceiveThreads()
    {
        var threadsCount = MathF.Ceiling((float)ActivePackers.Count / MaxClientsCountPerThread) == 0
            ? 1
            : MathF.Ceiling((float)ActivePackers.Count / MaxClientsCountPerThread);
        for (var i = ReceiveThreads.Count; i < threadsCount; i++) StartReceiveThread(i);
    }

    private static void StartReceiveThread(int id)
    {
        var thread = new Thread(() =>
        {
            var startIndex = id * MaxClientsCountPerThread;
            while (_isReceiverWorking)
            {
                if (startIndex > ActivePackers.Count)
                    break;
                CheckReceiveThreads();
                Thread.Sleep(ReceiveTick);
                for (var i = startIndex;
                     i < startIndex + MaxClientsCountPerThread;
                     i++)
                {
                    if (!Indexer.IsIndexed)
                        break;

                    if (i > ActivePackers.Count - 1) break;
                    var reader = ActivePackers[i];
                    if (!reader._isActivated) continue;
                    reader._deserializer.ReceiveAll();
                }
            }
        })
        {
            Name = $"Receive Packer Thread {id}"
        };
        thread.Start();
        ReceiveThreads.Add(thread);
    }

    private static void StartSender()
    {
        new Thread(() =>
        {
            while (_isSenderWorking)
            {
                Thread.Sleep(SendPacketTick);
                for (var i = 0; i < ActivePackers.Count; i++)
                {
                    var packer = ActivePackers[i];
                    if (!packer._isActivated) continue;
                    if (!Indexer.IsIndexed)
                    {
                        if (_isSenderWorking) _isSenderWorking = false;
                        if (_isReceiverWorking) _isReceiverWorking = false;
                        packer.Deactivate();
                        continue;
                    }

                    packer._deserializer.SendPackets();
                }
            }
        })
        {
            Name = "Send Packer Thread"
        }.Start();
    }

    public void SendBuffer()
    {
        CheckIsPacketsIndexed();
        CheckIsActivated();
        _deserializer.SendPackets();
    }

    public void WaitPacket<T>(Action<T?> callback, int timeout = 0) where T : struct, IPacket
    {
        CheckIsPacketsIndexed();
        CheckIsActivated();
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
        where T : struct, IPacket
    {
        CheckIsPacketsIndexed();
        CheckIsActivated();
        _deserializer.Request(send, compressed, callback, timeoutDisconnectMs);
    }

    public void SendPacket<T>(T packet, bool compressed = true) where T : struct, IPacket
    {
        CheckIsPacketsIndexed();
        CheckIsActivated();
        var packetType = Indexer.GetPacketType(packet.GetType());
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


    private void CheckIsActivated()
    {
        if (!_isActivated) throw new PackerException("Packer is not activated.");
    }

    private static void CheckIsPacketsIndexed()
    {
        if (!Indexer.IsIndexed) throw new PackerException("Packets are not indexed.");
    }
}