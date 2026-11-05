using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>Disconnects the client with a message.</summary>
    internal sealed class Disconnect : Packet
    {
        public required string Message
        {
            get; init;
        }
        public required int Protocol
        {
            get; init;
        }

        internal override int Id => (int)PacketId.DisconnectPacket;

        internal override void SerializeBody(ByteWriter w)
        {
            if (Protocol >= 622)
            {
                w.VarInt32(0); // reason: unknown
            }

            w.Bool(false); // skip message (712+ / 975+ encode "present" the same way)
            w.String(Message);
            if (Protocol >= 712)
            {
                w.String(Message); // filtered message
            }
        }
    }
}
