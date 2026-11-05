using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    public abstract class Packet
    {
        internal abstract int Id
        {
            get;
        }

        internal abstract void SerializeBody(ByteWriter writer);

        protected internal byte[] Build()
        {
            var writer = new ByteWriter(64);
            writer.VarUInt32((uint)Id);
            SerializeBody(writer);
            return writer.ToArray();
        }
    }
}
