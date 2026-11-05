using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>An empty creative inventory.</summary>
    internal sealed class CreativeContent : Packet
    {
        internal override int Id => (int)PacketId.CreativeContentPacket;

        internal override void SerializeBody(ByteWriter w)
        {
            w.VarUInt32(0); // groups
            w.VarUInt32(0); // items
        }
    }
}
