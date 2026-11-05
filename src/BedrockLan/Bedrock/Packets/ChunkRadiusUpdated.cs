using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>Confirms the chunk view radius the client asked for.</summary>
    internal sealed class ChunkRadiusUpdated : Packet
    {
        public required int Radius
        {
            get; init;
        }

        internal override int Id => (int)PacketId.ChunkRadiusUpdatedPacket;

        internal override void SerializeBody(ByteWriter w)
        {
            w.VarInt32(Radius);
        }
    }
}
