using BedrockLan.Internal;

namespace BedrockLan.Bedrock.Packets
{
    /// <summary>A minimal StartGame: empty world, player floating at y=100 in adventure mode.</summary>
    /// <remarks>
    /// Layout per Mojang's bedrock-protocol-docs, checked for protocols 2168-2250; newer protocols are assumed
    /// unchanged from 2250. The only wire change in that
    /// range is the editor level migration version byte added in 2216; later changes (2235: managed chat) sit
    /// inside the server join info, which is sent absent. See <see cref="BedrockPackets.SupportsSpawnStage"/>.
    /// </remarks>
    internal sealed class StartGame : Packet
    {
        /// <summary>Entity runtime/unique id given to the (never really spawned) player.</summary>
        public const long PlayerEntityId = 1;
        /// <summary>First protocol with the editor level migration version in the level settings.</summary>
        internal const int ProtocolEditorLevelMigration = 2216;

        public required string WorldName
        {
            get; init;
        }

        public required int Protocol
        {
            get; init;
        }
        internal override int Id => (int)PacketId.StartGamePacket;

        internal override void SerializeBody(ByteWriter w)
        {
            w.VarInt64(PlayerEntityId);   // entity unique id
            w.VarUInt64(PlayerEntityId);  // entity runtime id
            w.VarInt32(2);                // player game mode: adventure
            w.FloatLE(0);
            w.FloatLE(100);
            w.FloatLE(0); // position
            w.FloatLE(0);                 // pitch
            w.FloatLE(0);                 // yaw

            // Level settings
            w.Int64LE(0);                 // seed
            w.Int16LE(0);                 // spawn biome type
            w.String("");                 // user defined biome name
            w.VarInt32(0);                // dimension: overworld
            w.VarInt32(2);                // generator: flat
            w.VarInt32(2);                // world game mode: adventure
            w.Bool(false);                // hardcore
            w.VarInt32(1);                // difficulty
            w.VarInt32(0);
            w.VarInt32(100);
            w.VarInt32(0); // world spawn
            w.Bool(true);                 // achievements disabled
            w.VarInt32(0);                // editor world type
            w.Bool(false);                // created in editor
            w.Bool(false);                // exported from editor
            if (Protocol >= ProtocolEditorLevelMigration)
            {
                w.Byte(0);                // editor level migration version: legacy
            }

            w.VarInt32(0);                // day cycle lock time
            w.VarUInt32(0);               // education edition offer
            w.Bool(false);                // education features
            w.String("");                 // education product id
            w.FloatLE(0);                 // rain level
            w.FloatLE(0);                 // lightning level
            w.Bool(false);                // confirmed platform locked content
            w.Bool(true);                 // multiplayer game
            w.Bool(true);                 // LAN broadcast
            w.VarInt32(0);                // XBL broadcast mode
            w.VarInt32(0);                // platform broadcast mode
            w.Bool(false);                // commands enabled
            w.Bool(false);                // texture packs required
            w.VarUInt32(0);               // game rules
            w.UInt32LE(0);                // experiments
            w.Bool(false);                // experiments previously toggled
            w.Bool(false);                // bonus chest
            w.Bool(false);                // start with map
            w.Byte(1);                    // default player permissions: member
            w.Int32LE(4);                 // server chunk tick radius
            w.Bool(false);                // locked behaviour pack
            w.Bool(false);                // locked texture pack
            w.Bool(false);                // from locked world template
            w.Bool(false);                // MSA gamertags only
            w.Bool(false);                // from world template
            w.Bool(false);                // world template settings locked
            w.Bool(false);                // only spawn v1 villagers
            w.Bool(false);                // persona disabled
            w.Bool(false);                // custom skins disabled
            w.Bool(false);                // emote chat muted
            w.String("*");                // base game version
            w.Int32LE(0);                 // limited world width
            w.Int32LE(0);                 // limited world depth
            w.Bool(true);                 // new nether
            w.String("");                 // education shared resource: button name
            w.String("");                 // education shared resource: link
            w.Bool(false);                // force experimental gameplay: absent
            w.Byte(0);                    // chat restriction level
            w.Bool(false);                // disable player interactions
            w.VarInt32(0);                // server editor connection policy
            w.Bool(false);                // allow anonymous block drops in editor worlds

            w.String("");                 // level id
            w.String(WorldName);          // world name
            w.String("");                 // template content identity
            w.Bool(false);                // trial
            w.VarInt32(0);                // movement: rewind history size
            w.Bool(false);                // movement: server authoritative block breaking
            w.Int64LE(0);                 // time
            w.VarInt32(0);                // enchantment seed
            w.VarUInt32(0);               // custom blocks
            w.String(Guid.NewGuid().ToString()); // multiplayer correlation id
            w.Bool(true);                 // server authoritative inventory
            w.String("*");                // game version
            w.Byte(0x0A);
            w.VarUInt32(0);
            w.Byte(0x00); // property data: empty NBT compound
            w.UInt64LE(0);                // server block state checksum
            w.Bytes(new byte[16]);        // world template id
            w.Bool(false);                // client side generation
            w.Bool(false);                // use block network id hashes
            w.Bool(false);                // server authoritative sound
            w.Bool(false);                // server join information: absent
            w.String("");                 // server id
            w.String("");                 // scenario id
            w.String("");                 // world id
            w.String("");                 // owner id
        }
    }
}
