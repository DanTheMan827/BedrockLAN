using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>An empty ResourcePackStack.</summary>
    internal sealed class ResourcePackStack : Packet
    {
        public required int Protocol
        {
            get; init;
        }

        internal override int Id => (int)PacketId.ResourcePackStackPacket;

        internal override void SerializeBody(ByteWriter w)
        {
            w.Bool(false); // forced to accept
            if (Protocol < 898)
            {
                w.VarUInt32(0); // behaviour packs
            }

            w.VarUInt32(0); // resource packs
            w.String("*");  // base game version
            w.Int32LE(0);   // experiments
            w.Bool(false);  // experiments previously toggled
            w.Bool(false);  // has editor packs (671+)
        }
    }
}
