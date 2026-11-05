using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>No custom voxel shapes.</summary>
    internal sealed class VoxelShapes : Packet
    {
        internal override int Id => (int)PacketId.VoxelShapesPacket;

        internal override void SerializeBody(ByteWriter w)
        {
            w.VarUInt32(0); // shapes
            w.VarUInt32(0); // name map
            w.UInt16LE(0);  // custom shape count
        }
    }
}
