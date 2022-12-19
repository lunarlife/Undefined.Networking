using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Networking.DataConvert;
using Networking.Packets;
using Utils;

namespace Networking
{

    public sealed class RuntimePacketer : IEquatable<RuntimePacketer>, IComparable<RuntimePacketer>
    {
        private static readonly List<RuntimePacketer> Packeters = new();
        private static readonly List<Thread> Threads = new();
        private static readonly List<RequestInfo> Requests = new();
        private static bool _isThreadPoolWorking;
        private static bool _isSenderWorking;
    
        public delegate void ReceiveHanlder(Packet packet);
        public event ReceiveHanlder Receive;
        public delegate void UnhandledExceptionHandler(Exception exception);
        public event UnhandledExceptionHandler UnhandledException;

        private readonly Socket _socket;
        private readonly Priority _priority;
        private readonly Queue<Packet> _packetsToSend = new();

        private bool _isReading;

        public bool HasPacketsToSend => _packetsToSend.Count != 0;
        public static int Tick { get; set; } = 10;
        public static int MaxClientsCountForThread { get; set; } = 100;

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

        public static int SendPacketTick { get; set; } = 20;
        public static int MaxPacketsPerTick { get; set; } = 5;

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
                        var count = packeter._packetsToSend.Count < MaxPacketsPerTick
                            ? packeter._packetsToSend.Count
                            : MaxPacketsPerTick;
                        var packetsToSend = new List<Packet>();
                        for (var j = 0; j < count; j++)
                        {
                            var packet = packeter._packetsToSend.Dequeue();
                            packetsToSend.Add(packet);
                        }
                        if(packetsToSend.Count != 0)
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
                        var available = reader._socket.Available;
                        if (available == 0) continue;
                        var buffer = new byte[available];
                        ushort readedBytes = 0;
                        reader._socket.Receive(buffer);
                        var readedPackets = 0;
                        while (readedBytes < available && readedPackets <= MaxPacketsPerTick * 2)
                        {
                            try
                            {
                                var packet = DataConverter.Deserialize<Packet>(buffer, ref readedBytes);
                                readedPackets++;
                                if (packet == null) continue;
                                reader.Receive?.Invoke(packet);
                                if (Requests.FirstOrDefault(r => r.ReceiveType == packet.GetType()) is not
                                    { } requestInfo) continue;
                                Requests.Remove(requestInfo);
                                requestInfo.Callback?.Invoke(packet);
                            }
                            catch (Exception e)
                            {
                                reader.UnhandledException?.Invoke(e);
                                throw;
                            }
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
        private static void SendPacketsNow(RuntimePacketer packeter, params Packet[] packetsToSend)
        {
            if(!packeter.IsSending || !packeter.Server.IsConnectedOrOpened) return;
            var bytesEnumerable = packetsToSend.Select(packet => DataConverter.Serialize(packet)).ToArray(); 
            packeter._socket.Send(DataConverter.Combine(bytesEnumerable));
        }

        public void Request<T>(RequestPacket<T> send, Action<Packet>? callback = null) where T : Packet
        {
            SendPacketNow(send);
            Requests.Add(new RequestInfo
            {
                Callback = callback,
                ReceiveType = typeof(T)
            });
        }

        public void SendPacket(params Packet[] packets)
        {
            foreach (var p in packets) _packetsToSend.Enqueue(p);
        }
        public void SendPacketNow(params Packet[] packets)
        {
            SendPacketsNow(this, packets);
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
        private class RequestInfo
        {
            public Type ReceiveType;
            public Action<Packet>? Callback;
        }

    }

    public class ReaderException : Exception
    {
        public ReaderException(string msg) : base(msg)
        {

        }
    }
}