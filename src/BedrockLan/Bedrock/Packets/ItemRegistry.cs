using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>An empty item registry.</summary>
    internal sealed class ItemRegistry : Packet
    {
        internal override int Id => (int)PacketId.ItemRegistryPacket;

        internal override void SerializeBody(ByteWriter w)
        {
            w.VarUInt32(0); // items
        }
    }
}
