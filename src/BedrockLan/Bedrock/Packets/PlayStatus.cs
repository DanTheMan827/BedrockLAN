using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>Login / spawn progress for the client.</summary>
    internal sealed class PlayStatus : Packet
    {
        public const int LoginSuccess = 0;
        public const int PlayerSpawn = 3;

        /// <summary>One of <see cref="LoginSuccess"/>, <see cref="PlayerSpawn"/>.</summary>
        public required int Status
        {
            get; init;
        }

        internal override int Id => (int)PacketId.PlayStatusPacket;

        internal override void SerializeBody(ByteWriter w)
        {
            w.Int32BE(Status);
        }
    }
}
