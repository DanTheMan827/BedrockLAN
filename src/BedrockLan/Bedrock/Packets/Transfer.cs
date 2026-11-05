using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>Sends the client on to another server.</summary>
    /// <remarks>
    /// Layout per Mojang's bedrock-protocol-docs: address, port, then (729+) reload world, (2168+) optional
    /// gatherings configuration and (2250+) optional transfer UI settings. Optionals are a presence bool.
    /// </remarks>
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
                w.Bool(false); // optional gatherings configuration: absent
            }

            if (Protocol >= 2250)
            {
                w.Bool(false); // optional server transfer settings (TransferPlayerUIOptions): absent
            }
        }
    }
}
