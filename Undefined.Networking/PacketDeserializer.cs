using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Undefined.Events;
using Undefined.Networking.Events;
using Undefined.Networking.Exceptions;
using Undefined.Networking.Packets;
using Undefined.Serializer;
using Undefined.Serializer.Buffers;
using Buffer = Undefined.Serializer.Buffers.Buffer;

namespace Undefined.Networking;

internal sealed class PacketDeserializer : IDisposable
{
    private readonly Event<PacketReceiveEventArgs> _onReceive = new();
    private readonly Event<RequestEventArgs> _onRequest = new();
    private readonly Event<ResponseEventArgs> _onResponse = new();
    private readonly Event<PackerExceptionEventArgs> _onUnhandledException = new();
    private readonly Server _server;
    private readonly Packer _packer;
    private readonly Buffer _packetTempBuffer;
    private readonly Buffer _receiveBuffer;
    private readonly Buffer _sendBuffer;
    private readonly BufferWriter _packetTempWriter;
    private readonly BufferReader _receiveReader;
    private readonly BufferWriter _receiveWriter;
    private readonly BufferWriter _sendWriter;
    private readonly Queue<IPacketData> _packetsToSend = [];
    private readonly Dictionary<int, RequestData> _requests = [];
    private readonly List<PacketWaitInfo> _waitingPackets = [];
    private readonly object _sendBufferLock = new();
    private readonly object _packetsSendQueueWaitingLock = new();
    private readonly object _packetsWaitingLock = new(); 
    
    private int _availableData;
    private ushort _lastRequestId;

    public IEventAccess<PacketReceiveEventArgs> OnReceive => _onReceive.Access;
    public IEventAccess<ResponseEventArgs> OnResponse => _onResponse.Access;
    public IEventAccess<RequestEventArgs> OnRequest => _onRequest.Access;
    public IEventAccess<PackerExceptionEventArgs> OnUnhandledException => _onUnhandledException.Access;
    
    public int MaxPacketsPerTick { get; set; } = 20;


    public PacketDeserializer(DataConverter converter, Packer packer)
    {
        _packer = packer;
        _server = packer.Server;
        _packetTempBuffer = new Buffer(128, true, converter);
        _receiveBuffer = new Buffer(128, true, converter);
        _sendBuffer = new Buffer(128, true, converter);
        _packetTempWriter = new BufferWriter(_packetTempBuffer);
        _receiveReader = new BufferReader(_receiveBuffer);
        _receiveWriter = new BufferWriter(_receiveBuffer);
        _sendWriter = new BufferWriter(_sendBuffer);
    }

    public void Dispose()
    {
        _server.Dispose();
        _receiveBuffer.Dispose();
        _sendBuffer.Dispose();
    }

    public void ReceiveAll()
    {
        _availableData += _server.Socket!.Receive(_receiveWriter);
        while (TryUnpackPacket(out var read)) _availableData -= read;
        _receiveWriter.Position = _receiveReader.Position = _availableData;
    }

    private bool TryUnpackPacket(out int read)
    {
        read = 0;
        if (_availableData < 2) return false;
        var packetInfo = (PacketInfoFlags)_receiveReader.Read();
        read++;

        var isResponse = (packetInfo & PacketInfoFlags.IsResponse) != 0;

        if (!ReadPacketLength(packetInfo, out var packetLength, ref read))
            return false;


        //Packet type and request id serializing
        ushort requestId = 0;
        RequestData? request = null;
        PacketType packetType;
        if (isResponse)
        {
            requestId = ReadRequestId(packetInfo, ref read);
            if (!_requests.TryGetValue(requestId, out request))
            {
                _server.Close();
                throw new ResponseException($"Received response for not exist request {requestId}.");
            }

            packetType = Indexer.GetPacketType(((RequestPacketType)request.Type).ResponseType);
            if (packetType.Purpose != PacketPurpose.Response)
            {
                _server.Close();
                throw new ResponseException(
                    "Received default packet but sender note it as a response packet. Make sure that all packets indexed correct.");
            }
        }
        else
        {
            if (!GetPacketType(packetInfo, out packetType!, ref read)) return false;
        }

        if (packetType.Purpose == PacketPurpose.Request)
            requestId = ReadRequestId(packetInfo, ref read);

        var packet = packetType.Serializer is { } serializer
            ? serializer.Deserialize(_receiveReader)
            : (IPacket)_receiveReader.Read(packetType.Type)!;
        read += packetLength;
        if (packetType.Purpose == PacketPurpose.Request)
        {
            var type = (ResponsePacketType)Indexer.GetPacketType(((RequestPacketType)packetType).ResponseType);
            var args = new RequestEventArgs((IRequest)packet,
                type,
                requestId);
            _onRequest.Raise(args);
            if (args.Response.ResponsePacket is not { } response)
            {
                _server.Close();
                throw new ResponseException($"No response for request type {packetType.Type.Name}(id: {requestId}).");
            }

            var responseData = new ResponseData(requestId, response, type, args.Response.Compressed);
            AddPacketDataToSendQueue(responseData);
        }
        else if (packetType.Purpose == PacketPurpose.Response)
        {
            if (!_requests.Remove(requestId))
            {
                _server.Close();
                throw new PacketException($"Received invalid response id: {requestId}.");
            }

            request!.Callback?.DynamicInvoke(packet);
            _onResponse.Raise(new ResponseEventArgs(request.Packet, packet, requestId));
        }
        else ApplyPacket(packet);

        return true;
    }

    private bool GetPacketType(PacketInfoFlags packetInfo, out PacketType? packetType, ref int read)
    {
        var packetIdLength = (ushort)(packetInfo & PacketInfoFlags.IsUShortPacketId) == 0
            ? sizeof(byte)
            : sizeof(ushort);
        if (_receiveReader.Left < packetIdLength)
        {
            packetType = null;
            return false;
        }

        packetType = Indexer.GetPacketType(packetIdLength == sizeof(byte)
            ? _receiveReader.Read()
            : _receiveReader.Read<ushort>());
        if (packetType is ResponsePacketType)
            throw new PacketException(
                $"Received response packet ({packetType.Type.Name}) as a default packet. Make sure that all packets indexed correct.");
        read += packetIdLength;
        return true;
    }

    private bool ReadPacketLength(PacketInfoFlags packetInfo, out int packetLength,
        ref int read)
    {
        Type packetLengthType;
        int lengthOfPacketLength;
        if ((packetInfo & PacketInfoFlags.IsIntLength) != 0)
        {
            lengthOfPacketLength = sizeof(int);
            packetLengthType = typeof(int);
        }
        else if ((packetInfo & PacketInfoFlags.IsUShortLength) != 0)
        {
            lengthOfPacketLength = sizeof(ushort);
            packetLengthType = typeof(ushort);
        }
        else
        {
            lengthOfPacketLength = sizeof(byte);
            packetLengthType = typeof(byte);
        }

        //Trying to deserialize a packet length
        if (lengthOfPacketLength == sizeof(byte))
            packetLength = _receiveReader.Read();
        else if (_receiveReader.Left < lengthOfPacketLength)
        {
            packetLength = 0;
            return false;
        }
        else packetLength = (int)_receiveReader.Read(packetLengthType)!;

        read += lengthOfPacketLength;
        return true;
    }

    private ushort ReadRequestId(PacketInfoFlags packetInfo, ref int read)
    {
        if ((packetInfo & PacketInfoFlags.IsUShortRequestId) != 0)
        {
            read += sizeof(ushort);
            return _receiveReader.Read<ushort>();
        }

        read += sizeof(byte);
        return _receiveReader.Read();
    }

    private void ApplyPacket(IPacket packet)
    {
        var type = packet.GetType();
        lock (_packetsWaitingLock)
        {
            for (var i = 0; i < _waitingPackets.Count; i++)
            {
                var request = _waitingPackets[i];
                if (request.ReceiveType != type) continue;
                _waitingPackets.Remove(request);
                request.Callback?.DynamicInvoke(packet);
            }
        }

        _onReceive.Raise(new PacketReceiveEventArgs(packet, _packer));
    }

    public void AddPacketToSendQueue(IPacket packet, bool compressed)
    {
        var type = Indexer.GetPacketType(packet.GetType());
        var data = new PacketData(packet, type, compressed);
        AddPacketDataToSendQueue(data);
    }

    private void AddPacketDataToSendQueue(IPacketData data)
    {
        lock (_packetsSendQueueWaitingLock)
        {
            _packetsToSend.Enqueue(data);
        }
    }


    public void Request(IRequest packet, bool compressed, Delegate? callback, int timeoutMs)
    {
        var objectType = packet.GetType();
        if (Indexer.GetPacketType(objectType) is not RequestPacketType type)
            throw new PackerException($"Type {objectType.FullName} is not request packet.");

        lock (_packetsSendQueueWaitingLock)
        {
            var id = _lastRequestId;
            var data = new RequestData(id, packet, type, compressed, callback);
            _requests.Add(id, data);
            _packetsToSend.Enqueue(new RequestData(id, packet, type, compressed, callback));
            _lastRequestId++;
        }

        if (timeoutMs == 0) return;
        Task.Delay(timeoutMs).ContinueWith(_ => { });
    }

    public void SendPackets()
    {
        lock (_packetsSendQueueWaitingLock)
        lock (_sendBufferLock)
        {
            var count = _packetsToSend.Count < MaxPacketsPerTick
                ? _packetsToSend.Count
                : MaxPacketsPerTick;
            for (var j = 0; j < count; j++)
                WritePacketToBuffer(_packetsToSend.Dequeue());
            if (count == 0) return;
            try
            {
                _server.Socket!.Send(new ReadOnlySpan<byte>(_sendBuffer.GetBuffer(), 0, _sendWriter.Position));
            }
            catch (SocketException)
            {
                _packer.Deactivate();
                _onUnhandledException.Raise(
                    new PackerExceptionEventArgs(new PackerException("Client lost connection to the server.")));
            }

            _sendWriter.Position = 0;
        }
    }


    private void WritePacketToBuffer(IPacketData info)
    {
        _packetTempWriter.Position = 0;
        if (info.Type.Serializer is { } serializer) serializer.Serialize(_packetTempWriter);
        else
            _packetTempWriter.Write(info.Packet, info.Compressed);
        var packetLength = _packetTempWriter.Position;
        PacketInfoFlags flags = 0;
        var type = info.Type;

        flags |= type.Flags;
        switch (packetLength)
        {
            case > ushort.MaxValue:
                flags |= PacketInfoFlags.IsIntLength;
                break;
            case > byte.MaxValue:
                flags |= PacketInfoFlags.IsUShortLength;
                break;
        }

        var requestId = 0u;
        if (info is IIdentifiablePacketData i)
        {
            requestId = i.Id;
            if (requestId > byte.MaxValue) flags |= PacketInfoFlags.IsUShortRequestId;
        }

        lock (_sendBufferLock)
        {
            //Packet info
            _sendWriter.Write((byte)flags);

            //Packet length
            if ((flags & PacketInfoFlags.IsIntLength) != 0)
                _sendWriter.Write(packetLength, false);
            else if ((flags & PacketInfoFlags.IsUShortLength) != 0)
                _sendWriter.Write((ushort)packetLength, false);
            else
                _sendWriter.Write((byte)packetLength);

            //Packet id
            if (type is not ResponsePacketType)
            {
                if (type.IsUShortPacketId)
                    _sendWriter.Write(type, false);
                else
                    _sendWriter.Write((byte)type.Id);
            }

            if (info is IIdentifiablePacketData)
            {
                if ((flags & PacketInfoFlags.IsUShortRequestId) != 0)
                    _sendWriter.Write((ushort)requestId, false);
                else
                    _sendWriter.Write((byte)requestId);
            }

            _sendWriter.Write(_packetTempBuffer.GetBuffer(), 0, packetLength);
        }
    }

    public void WaitPacket(Type packet, Delegate? callback, int timeout)
    {
        var info = new PacketWaitInfo(packet, callback);
        lock (_packetsWaitingLock)
        {
            _waitingPackets.Add(info);
        }

        if (timeout > 0)
            Task.Delay(timeout).ContinueWith(_ =>
            {
                lock (_packetsWaitingLock)
                {
                    for (var i = 0; i < _waitingPackets.Count; i++)
                    {
                        if (!_waitingPackets[i].Equals(info)) continue;
                        _waitingPackets.RemoveAt(i);
                        callback?.DynamicInvoke(null);
                        break;
                    }
                }
            });
    }

    private readonly struct PacketWaitInfo
    {
        public Type ReceiveType { get; }
        public Delegate? Callback { get; }

        public PacketWaitInfo(Type receiveType, Delegate? callback)
        {
            ReceiveType = receiveType;
            Callback = callback;
        }

        public bool Equals(PacketWaitInfo other) =>
            ReceiveType == other.ReceiveType && Equals(Callback, other.Callback);

        public override bool Equals(object? obj) => obj is PacketWaitInfo other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ReceiveType, Callback);
    }
}