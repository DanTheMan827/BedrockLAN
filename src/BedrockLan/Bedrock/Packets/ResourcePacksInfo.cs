using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>An empty ResourcePacksInfo (no behaviour or resource packs). Layouts from protocol 729 on.</summary>
    internal sealed class ResourcePacksInfo : Packet
    {
        public required int Protocol
        {
            get; init;
        }

        internal override int Id => (int)PacketId.ResourcePacksInfoPacket;

        internal override void SerializeBody(ByteWriter w)
        {
            w.Bool(false); // forced to accept
            w.Bool(false); // has addon packs
            w.Bool(false); // scripting enabled
            if (Protocol >= 818)
            {
                w.Bool(false); // vibrant visuals force disabled
            }

            if (Protocol >= 766)
            {
                w.Bytes(new byte[16]); // world template UUID
                w.String("");          // world template version
            }

            if (Protocol >= 2168)
            {
                w.VarUInt32(0); // resource pack entries
            }
            else
            {
                w.UInt16LE(0); // resource pack entries
                if (Protocol < 748)
                {
                    w.VarUInt32(0); // CDN entries (729-747 only)
                }
            }
        }
    }
}
