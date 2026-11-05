using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>JigsawStructureData with the four empty lists vanilla sends.</summary>
    internal sealed class JigsawStructureData : Packet
    {
        private static readonly string[] ListNames = { "processors", "template_pools", "jigsaws", "structure_sets" };

        internal override int Id => (int)PacketId.JigsawStructureDataPacket;

        internal override void SerializeBody(ByteWriter w)
        {
            // Network little-endian NBT: compound "" { processors, template_pools, jigsaws, structure_sets: [] of compound }
            w.Byte(0x0A);
            w.VarUInt32(0);
            foreach (var name in ListNames)
            {
                w.Byte(0x09);
                w.String(name);
                w.Byte(0x0A);   // element type: compound
                w.VarInt32(0);  // length
            }

            w.Byte(0x00);
        }
    }
}
