using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>Reply to RequestNetworkSettings: compression settings for the rest of the session.</summary>
    internal sealed class NetworkSettings : Packet
    {
        private const ushort CompressionAlgorithmFlate = 0;

        /// <summary>Batches at least this large are compressed.</summary>
        public required ushort CompressionThreshold
        {
            get; init;
        }

        internal override int Id => (int)PacketId.NetworkSettingsPacket;

        internal override void SerializeBody(ByteWriter w)
        {
            w.UInt16LE(CompressionThreshold);
            w.UInt16LE(CompressionAlgorithmFlate);
            w.Bool(false); // client throttle enabled
            w.Byte(0);     // client throttle threshold
            w.FloatLE(0);  // client throttle scalar
        }
    }
}
