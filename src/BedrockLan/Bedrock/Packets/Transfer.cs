using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>Sends the client on to another server.</summary>
    internal sealed class Transfer : Packet
    {
        public required int Protocol
        {
            get; init;
        }

        public required string Address
        {
            get; init;
        }

        public required ushort Port
        {
            get; init;
        }

        internal override int Id => (int)PacketId.TransferPacket;

        internal override void SerializeBody(ByteWriter w)
        {
            w.String(Address);
            w.UInt16LE(Port);
            if (Protocol >= 729)
            {
                w.Bool(false); // reload world
            }

            if (Protocol >= 2168)
            {
                w.Bool(false); // optional gathering join info: absent
            }
        }
    }
}
