using Undefined.Networking.Packets;
using Undefined.Serializer.Buffers;

namespace Undefined.Networking;

[CustomSerializer(typeof(TestPacketSerializer))]
public struct TestPacket : IPacket
{
}

public struct TestRequest : IRequest<TestResponse>
{
}

public struct TestResponse : IPacket
{
}

public class TestPacketSerializer : ICustomSerializer<TestPacket>
{
    public TestPacket Deserialize(BufferReader reader)
    {
        return new TestPacket();
    }

    public void Serialize(BufferWriter writer)
    {
    }
}