namespace BedrockLan.Bedrock.Packets
{
    // Generated from the supplied Mojang Bedrock protocol packet listing.
    /// <summary>
    /// Network packet identifiers for the Minecraft Bedrock protocol.
    /// </summary>
    public enum PacketId : int
    {
        /// <summary>
        /// Sent once from client to server at login. About 100k.
        /// </summary>
        LoginPacket = 1,

        /// <summary>
        /// Describes the login status of the player
        /// </summary>
        PlayStatusPacket = 2,

        /// <summary>
        /// Server-&gt;Client Handshake
        /// </summary>
        ServerToClientHandshakePacket = 3,

        /// <summary>
        /// Sets up encryption and authenticates in educational version once at level startup from client.
        /// </summary>
        ClientToServerHandshakePacket = 4,

        /// <summary>
        /// Sent from the server to a client to trigger a disconnection.
        /// </summary>
        DisconnectPacket = 5,

        /// <summary>
        /// Resource Packs Info
        /// </summary>
        ResourcePacksInfoPacket = 6,

        /// <summary>
        /// Resource Pack Stack
        /// </summary>
        ResourcePackStackPacket = 7,

        /// <summary>
        /// Resource Pack Client Response
        /// </summary>
        ResourcePackClientResponsePacket = 8,

        /// <summary>
        /// Represents a text message that needs to be displayed in-game
        /// </summary>
        TextPacket = 9,

        /// <summary>
        /// Set Time
        /// </summary>
        SetTimePacket = 10,

        /// <summary>
        /// Sent from the server to client when the game is starting (or client joins), gives ids and current tick.
        /// </summary>
        StartGamePacket = 11,

        /// <summary>
        /// Add Player
        /// </summary>
        AddPlayerPacket = 12,

        /// <summary>
        /// Add Actor
        /// </summary>
        AddActorPacket = 13,

        /// <summary>
        /// Occasionally, during the server player tick some time is taken to remove nearby actors from the world.
        /// </summary>
        RemoveActorPacket = 14,

        /// <summary>
        /// When an item actor is added to the world (punch a tree-&gt;tree block, for example)
        /// </summary>
        AddItemActorPacket = 15,

        /// <summary>
        /// Used to send a player's server position to the respective client at the end of movement.
        /// </summary>
        ServerPlayerPostMovePositionPacket = 16,

        /// <summary>
        /// A packet sent to the server when deal with picking up an item off the ground in the world.
        /// </summary>
        TakeItemActorPacket = 17,

        /// <summary>
        /// This is used primarily for motion updates of all actors from server to client.
        /// </summary>
        MoveActorAbsolutePacket = 18,

        /// <summary>
        /// For client authoritative and server authoritative movement modes, it is only intended as a client-bound packet. It forces the player to a specified position with slightly different behavior depending on the position mode.
        /// </summary>
        MovePlayerPacket = 19,

        /// <summary>
        /// Occasional packets sent from server when blocks update or are ticked. (For example, when digging.)
        /// </summary>
        UpdateBlockPacket = 21,

        /// <summary>
        /// Sends the information for a new painting actor from server to client.
        /// </summary>
        AddPaintingPacket = 22,

        /// <summary>
        /// Splash Potions, weather events, global pause, simlock commands, oh my!
        /// </summary>
        LevelEventPacket = 25,

        /// <summary>
        /// Whenever a block event happens it is sent from the server to sync client and server, with arbitrarily encoded information in b0 and b1.
        /// </summary>
        BlockEventPacket = 26,

        /// <summary>
        /// All kinds of actor state changes (see Actor::handleEntityEvent).
        /// </summary>
        ActorEventPacket = 27,

        /// <summary>
        /// Mob Effect
        /// </summary>
        MobEffectPacket = 28,

        /// <summary>
        /// Update Attributes
        /// </summary>
        UpdateAttributesPacket = 29,

        /// <summary>
        /// Inventory Transaction. Sent for item interaction UI depending on if ItemStackNetManager is enabled as well as when the player uses items in gameplay. See ComplexInventoryTransaction::Type for more details.
        /// </summary>
        InventoryTransactionPacket = 30,

        /// <summary>
        /// Mob Equipment. Changes to any mob's held/offhand item are fired from server to client with this packet. Clients also send this to the server when their held slot or its contents change.
        /// </summary>
        MobEquipmentPacket = 31,

        /// <summary>
        /// Mob Armor Equipment
        /// </summary>
        MobArmorEquipmentPacket = 32,

        /// <summary>
        /// Interact
        /// </summary>
        InteractPacket = 33,

        /// <summary>
        /// Player picks up a block in the world; client to server.
        /// </summary>
        BlockPickRequestPacket = 34,

        /// <summary>
        /// Player clicks on an actor in the world, eg a chicken.
        /// </summary>
        ActorPickRequestPacket = 35,

        /// <summary>
        /// Sent from the client whenever the player performs an action (dashing, un-dashing, use an item, mine/hit, use a block, etc).
        /// </summary>
        PlayerActionPacket = 36,

        /// <summary>
        /// Hurt Armor
        /// </summary>
        HurtArmorPacket = 38,

        /// <summary>
        /// Regular Actor Data delta packets, sent from the level on tick, mob and actor during normal tick
        /// </summary>
        SetActorDataPacket = 39,

        /// <summary>
        /// This is used for the server to set the velocity of a client actor.
        /// </summary>
        SetActorMotionPacket = 40,

        /// <summary>
        /// Sent by both client and server, only received by LegacyClientHandler.
        /// </summary>
        SetActorLinkPacket = 41,

        /// <summary>
        /// This packet is sent to the client when the player is spawned in and when they respawn.
        /// </summary>
        SetHealthPacket = 42,

        /// <summary>
        /// When a player logs in or the SetWorldSpawnCommand is used this is sent from the server to the client. Does not change when using a bed, that is a separate packet (RespawnPacket)
        /// </summary>
        SetSpawnPositionPacket = 43,

        /// <summary>
        /// Animate Actor
        /// </summary>
        AnimatePacket = 44,

        /// <summary>
        /// Sent as a handshake between the client and server to respawn the player.
        /// </summary>
        RespawnPacket = 45,

        /// <summary>
        /// Sent from the server so that the client knows to open the container screen and do the chest opening animation.
        /// </summary>
        ContainerOpenPacket = 46,

        /// <summary>
        /// After the game deletes the container manager on the client, the client sends this packet. Then the server deletes its container manager, and sends a packet back to the client that closes the container screen.
        /// </summary>
        ContainerClosePacket = 47,

        /// <summary>
        /// Sent from the server when the player uses pick block on actors or blocks, in addition to the player uses the clear, give, or replace item command or if the serverplayer uses _sendAdditionalLevelData().
        /// </summary>
        PlayerHotbarPacket = 48,

        /// <summary>
        /// This is used for updating an entire container. Example uses include: player respawned, replace items command, 3rd party content calls sendInventory(), block picking.
        /// </summary>
        InventoryContentPacket = 49,

        /// <summary>
        /// Updates one slot in an inventory rather than the whole thing. So like animal inventory (horses, donkeys, etc) and chests. Hotbar, offhand, and some player inventory changes.
        /// </summary>
        InventorySlotPacket = 50,

        /// <summary>
        /// This is sent from the server basically any time that the "cooking" state of the brewing stand or the furnace changes (i.e. the loading bar)
        /// </summary>
        ContainerSetDataPacket = 51,

        /// <summary>
        /// Crafting Data
        /// </summary>
        CraftingDataPacket = 52,

        /// <summary>
        /// The server telling the client what item slot to hover over in the hotbar.
        /// </summary>
        GuiDataPickItemPacket = 54,

        /// <summary>
        /// Sends the entire user data compound tag and the block position to the client.
        /// </summary>
        BlockActorDataPacket = 56,

        /// <summary>
        /// Level Chunk Packet
        /// </summary>
        LevelChunkPacket = 58,

        /// <summary>
        /// This is used by the world settings screen, cheats, EDU builds for teachers, and various other places to enable cheats/commands
        /// </summary>
        SetCommandsEnabledPacket = 59,

        /// <summary>
        /// Set Difficulty
        /// </summary>
        SetDifficultyPacket = 60,

        /// <summary>
        /// The server sends this packet from the level to kick off dimension changing process.
        /// </summary>
        ChangeDimensionPacket = 61,

        /// <summary>
        /// Set Player Game Type
        /// </summary>
        SetPlayerGameTypePacket = 62,

        /// <summary>
        /// Player List
        /// </summary>
        PlayerListPacket = 63,

        /// <summary>
        /// This packet is used for enabling/disabling commands and for unlocking world template settings (both unlocking UI buttons on client and the actual setting on the server).
        /// </summary>
        SimpleEventPacket = 64,

        /// <summary>
        /// Sends telemetry events to the client so the client can then send that on to the eventing system
        /// </summary>
        LegacyTelemetryEventPacket = 65,

        /// <summary>
        /// Spawn Experience Orb
        /// </summary>
        SpawnExperienceOrbPacket = 66,

        /// <summary>
        /// Map item data including pixels, decorations, and creation info
        /// </summary>
        ClientboundMapItemDataPacket = 67,

        /// <summary>
        /// In the case of the client being unable to find map data for a map item it sends a uuid for a map to the server.
        /// </summary>
        MapInfoRequestPacket = 68,

        /// <summary>
        /// The client can't just change the view radius without the server's approval, otherwise there could be holes on unrendered area.
        /// </summary>
        RequestChunkRadiusPacket = 69,

        /// <summary>
        /// Sent when the world is loading. We don't know why it is sent four times. Defines the tick distance.
        /// </summary>
        ChunkRadiusUpdatedPacket = 70,

        /// <summary>
        /// Updates game rules.
        /// </summary>
        GameRulesChangedPacket = 72,

        /// <summary>
        /// Used only in EDU through the tripod camera item or the TakePictureCommand. Sends the camera actor id and the target player id from the server.
        /// </summary>
        CameraPacket = 73,

        /// <summary>
        /// Sent when a boss gets updated
        /// </summary>
        BossEventPacket = 74,

        /// <summary>
        /// Starts on server when the credits screen should pop up.
        /// </summary>
        ShowCreditsPacket = 75,

        /// <summary>
        /// Contains all the available commands.
        /// </summary>
        AvailableCommandsPacket = 76,

        /// <summary>
        /// "slash" command execution, client to server.
        /// </summary>
        CommandRequestPacket = 77,

        /// <summary>
        /// Command Block Update
        /// </summary>
        CommandBlockUpdatePacket = 78,

        /// <summary>
        /// "slash" command execution output, server to client.
        /// </summary>
        CommandOutputPacket = 79,

        /// <summary>
        /// This is used when the player trades with an npc. This sends all of the updated trade info in one big ol' packet.
        /// </summary>
        UpdateTradePacket = 80,

        /// <summary>
        /// Seemingly only used for the Horse Inventory... More specifically when the player opens the horse inventory.
        /// </summary>
        UpdateEquipPacket = 81,

        /// <summary>
        /// Resource Pack Data Info
        /// </summary>
        ResourcePackDataInfoPacket = 82,

        /// <summary>
        /// Resource Pack Chunk Data
        /// </summary>
        ResourcePackChunkDataPacket = 83,

        /// <summary>
        /// Resource Pack Chunk Request
        /// </summary>
        ResourcePackChunkRequestPacket = 84,

        /// <summary>
        /// Used to kick off transferring the client between online games, or it can be used to cause players to quit the world and rejoin.
        /// </summary>
        TransferPacket = 85,

        /// <summary>
        /// This packet is only used via command or script event. This is for 3rd party content.
        /// </summary>
        PlaySoundPacket = 86,

        /// <summary>
        /// Allows you to stop a sound or all sounds on all clients, only used in a /command
        /// </summary>
        StopSoundPacket = 87,

        /// <summary>
        /// Used by 3rd party content for the purpose of showing ui banners
        /// </summary>
        SetTitlePacket = 88,

        /// <summary>
        /// Add Behavior Tree
        /// </summary>
        AddBehaviorTreePacket = 89,

        /// <summary>
        /// After the client makes changes in the Structure Block Screen we tell the server to update based off of that. This only sent when you close the UI.
        /// </summary>
        StructureBlockUpdatePacket = 90,

        /// <summary>
        /// Used for redirecting a user to the right offer.
        /// </summary>
        ShowStoreOfferPacket = 91,

        /// <summary>
        /// Sent from client to server
        /// </summary>
        PurchaseReceiptPacket = 92,

        /// <summary>
        /// Used when the player changes their skin
        /// </summary>
        PlayerSkinPacket = 93,

        /// <summary>
        /// This packet starts the login process of subclients (splitscreen)
        /// </summary>
        SubClientLoginPacket = 94,

        /// <summary>
        /// Initiates websocket connection
        /// </summary>
        AutomationClientConnectPacket = 95,

        /// <summary>
        /// Any time a player is hit, the id of the last mob that attacked them is sent to the client
        /// </summary>
        SetLastHurtByPacket = 96,

        /// <summary>
        /// Sends the updated state of the Book and Quill item from client to server during use.
        /// </summary>
        BookEditPacket = 97,

        /// <summary>
        /// Used for a number of interactions with the NPC Component
        /// </summary>
        NpcRequestPacket = 98,

        /// <summary>
        /// There is a camera item in EDU and they can use it to take screenshots and add them to a scrapbook.
        /// </summary>
        PhotoTransferPacket = 99,

        /// <summary>
        /// Modal Form Request
        /// </summary>
        ModalFormRequestPacket = 100,

        /// <summary>
        /// Fired in response to third party server request to show the custom UI screen.
        /// </summary>
        ModalFormResponsePacket = 101,

        /// <summary>
        /// Sent during the initialization of world settings on the client.
        /// </summary>
        ServerSettingsRequestPacket = 102,

        /// <summary>
        /// Server Settings Response
        /// </summary>
        ServerSettingsResponsePacket = 103,

        /// <summary>
        /// Show Profile
        /// </summary>
        ShowProfilePacket = 104,

        /// <summary>
        /// Same as SetPlayerGameTypePacket &amp; UpdatePlayerGameTypePacket, the only difference is that this changes the default for all clients.
        /// </summary>
        SetDefaultGameTypePacket = 105,

        /// <summary>
        /// Using the scoreboard command, users can remove objectives that are tracked on the scoreboard.
        /// </summary>
        RemoveObjectivePacket = 106,

        /// <summary>
        /// Sent from the server for 3rd party content to display current objectives and status
        /// </summary>
        SetDisplayObjectivePacket = 107,

        /// <summary>
        /// Set Score
        /// </summary>
        SetScorePacket = 108,

        /// <summary>
        /// For the EDU Chemistry Lab Table block actor.
        /// </summary>
        LabTablePacket = 109,

        /// <summary>
        /// Used to sync moving blocks with clients so they render correctly
        /// </summary>
        UpdateBlockSyncedPacket = 110,

        /// <summary>
        /// This packet is all the deltas of actors position, rotation, and head rotation. This is used for all actors.
        /// </summary>
        MoveActorDeltaPacket = 111,

        /// <summary>
        /// Set Scoreboard Identity
        /// </summary>
        SetScoreboardIdentityPacket = 112,

        /// <summary>
        /// Set Local Player As Initialized
        /// </summary>
        SetLocalPlayerAsInitializedPacket = 113,

        /// <summary>
        /// This is used for the scoreboard and tag systems (overwhelmingly used by 3rd party content)
        /// </summary>
        UpdateSoftEnumPacket = 114,

        /// <summary>
        /// Ping Packet
        /// </summary>
        NetworkStackLatencyPacket = 115,

        /// <summary>
        /// Tell client to spawn a particle effect.
        /// </summary>
        SpawnParticleEffectPacket = 118,

        /// <summary>
        /// Sends the whole list of actor identifiers at game start from the server.
        /// </summary>
        AvailableActorIdentifiersPacket = 119,

        /// <summary>
        /// Tells clients to update the chunk view for the local player.
        /// </summary>
        NetworkChunkPublisherUpdatePacket = 121,

        /// <summary>
        /// On world start, send clients the info for all available biomes.
        /// </summary>
        BiomeDefinitionListPacket = 122,

        /// <summary>
        /// Level Sound Event
        /// </summary>
        LevelSoundEventPacket = 123,

        /// <summary>
        /// LevelEventGenericPacket
        /// </summary>
        LevelEventGenericPacket = 124,

        /// <summary>
        /// This is used for the Lectern Block Actor.
        /// </summary>
        LecternUpdatePacket = 125,

        /// <summary>
        /// It is sent by the Client once, at login, to communicate if it supports the cache or not.
        /// </summary>
        ClientCacheStatusPacket = 129,

        /// <summary>
        /// On-Screen Texture Animation
        /// </summary>
        OnScreenTextureAnimationPacket = 130,

        /// <summary>
        /// This is fired when the user locks a map item utilizing the Cartography Table in game.
        /// </summary>
        MapCreateLockedCopyPacket = 131,

        /// <summary>
        /// Used to request structure information from a server.
        /// </summary>
        StructureTemplateDataRequestPacket = 132,

        /// <summary>
        /// This is used in exporting from load, exporting from save, and querying saved structures from structure blocks.
        /// </summary>
        StructureTemplateDataResponsePacket = 133,

        /// <summary>
        /// Client Cache Blob Status Packet
        /// </summary>
        ClientCacheBlobStatusPacket = 135,

        /// <summary>
        /// Only active in a real client-server scenario. This packet is just a list of &lt;blobId, blob&gt; pairs sent from server to client.
        /// </summary>
        ClientCacheMissResponsePacket = 136,

        /// <summary>
        /// Transmits EducationLevelSettings to all clients.
        /// </summary>
        EducationSettingsPacket = 137,

        /// <summary>
        /// A client sends this to the server to notify other clients about the emote.
        /// </summary>
        EmotePacket = 138,

        /// <summary>
        /// Syncs multiplayer settings
        /// </summary>
        MultiplayerSettingsPacket = 139,

        /// <summary>
        /// Requests a setting to be changed through commands.
        /// </summary>
        SettingsCommandPacket = 140,

        /// <summary>
        /// Requests an anvil to be damaged.
        /// </summary>
        AnvilDamagePacket = 141,

        /// <summary>
        /// Send server to client to complete the using item process. An example is when you finish drinking or eating.
        /// </summary>
        CompletedUsingItemPacket = 142,

        /// <summary>
        /// Sends tunable options from host to client (compression threshold and algorithm)
        /// </summary>
        NetworkSettingsPacket = 143,

        /// <summary>
        /// Player Auth Input
        /// </summary>
        PlayerAuthInputPacket = 144,

        /// <summary>
        /// Creative Content
        /// </summary>
        CreativeContentPacket = 145,

        /// <summary>
        /// Sends the of new Enchanting options from the server when a player right clicks the enchantment table.
        /// </summary>
        PlayerEnchantOptionsPacket = 146,

        /// <summary>
        /// The new server auth inventory item transaction request. This is done in batches of items.
        /// </summary>
        ItemStackRequestPacket = 147,

        /// <summary>
        /// Server auth inventory item transaction response, sent from the server.
        /// </summary>
        ItemStackResponsePacket = 148,

        /// <summary>
        /// Sent from server whenever the player's armor takes damage.
        /// </summary>
        PlayerArmorDamagePacket = 149,

        /// <summary>
        /// Code Builder Packet
        /// </summary>
        CodeBuilderPacket = 150,

        /// <summary>
        /// The server will send this back to all clients on receipt of the SetPlayerGameTypePacket so that cached game type and permissions flags in mLevel on all clients is kept up to date.
        /// </summary>
        UpdatePlayerGameTypePacket = 151,

        /// <summary>
        /// Allows clients to download emotes that other clients have equipped.
        /// </summary>
        EmoteListPacket = 152,

        /// <summary>
        /// Server to client packet for server authoratative runtime database (with persistent LevelStorage backup) designed primarily to track lodestone stuff. See Position Tracking DB Notes.md in bedrock-docs.
        /// </summary>
        PositionTrackingDBServerBroadcastPacket = 153,

        /// <summary>
        /// Position Tracking DB Client Request
        /// </summary>
        PositionTrackingDBClientRequestPacket = 154,

        /// <summary>
        /// The system sends debug information via a generic network packet. This enables rendering of any server information on the client in for instance ImGui.
        /// </summary>
        DebugInfoPacket = 155,

        /// <summary>
        /// This is sent when the client detects a malformed packet
        /// </summary>
        PacketViolationWarningPacket = 156,

        /// <summary>
        /// It is essentially a SetActionMotionPacket with a bool indicating if the actor was on the ground at the time the packet is sent or not.
        /// </summary>
        MotionPredictionHintsPacket = 157,

        /// <summary>
        /// The AnimateEntityPacket is used to trigger a one - off animation on the client it is sent to.
        /// </summary>
        AnimateEntityPacket = 158,

        /// <summary>
        /// Used to control trigger camera shake movements on the client's player camera
        /// </summary>
        CameraShakePacket = 159,

        /// <summary>
        /// Player Fog Packet
        /// </summary>
        PlayerFogPacket = 160,

        /// <summary>
        /// Sent to a player when their simulation of movement mismatches enough from the server that it wants to correct the client.
        /// </summary>
        CorrectPlayerMovePredictionPacket = 161,

        /// <summary>
        /// Item data from the server. Contains component information.
        /// </summary>
        ItemRegistryPacket = 162,

        /// <summary>
        /// Used to add/remove debug rendering objects.
        /// </summary>
        ClientboundDebugRendererPacket = 164,

        /// <summary>
        /// Allows us to synchronize Actor properties across the network.
        /// </summary>
        SyncActorPropertyPacket = 165,

        /// <summary>
        /// Sent from the server to the client when setting the simulation type for toolbox mode. (Not yet suported)
        /// </summary>
        SimulationTypePacket = 168,

        /// <summary>
        /// Sent from the server to client when remote firing an NPC dialogue window for a client
        /// </summary>
        NpcDialoguePacket = 169,

        /// <summary>
        /// Transmits Edu Shared Uri Resource settings to all clients.
        /// </summary>
        EduUriResourcePacket = 170,

        /// <summary>
        /// Players now have the possibility to export photos from their portfolios into photo items in their inventory. EDU.
        /// </summary>
        CreatePhotoPacket = 171,

        /// <summary>
        /// Packet sent for every set of blocks changed in a sub chunk every tick.
        /// </summary>
        UpdateSubChunkBlocksPacket = 172,

        /// <summary>
        /// Sent from server to client representing a batch of subchunk data.
        /// </summary>
        SubChunkPacket = 174,

        /// <summary>
        /// Sent from the client to the server representing a batch of subchunks that the client requests from the server
        /// </summary>
        SubChunkRequestPacket = 175,

        /// <summary>
        /// Player Start Item Cooldown
        /// </summary>
        PlayerStartItemCooldownPacket = 176,

        /// <summary>
        /// Used to send custom messages between client and server.
        /// </summary>
        ScriptMessagePacket = 177,

        /// <summary>
        /// This is EDU exclusive, used in getInterface() of WebviewSystem
        /// </summary>
        CodeBuilderSourcePacket = 178,

        /// <summary>
        /// Used to inform the client that the server is waiting for ticking areas to finish preloading.
        /// </summary>
        TickingAreasLoadStatusPacket = 179,

        /// <summary>
        /// Brief packet containing data-driven dimension properties
        /// </summary>
        DimensionDataPacket = 180,

        /// <summary>
        /// packet containing data of Agent Action Type
        /// </summary>
        AgentActionEventPacket = 181,

        /// <summary>
        /// packet containing data for changing mob property
        /// </summary>
        ChangeMobPropertyPacket = 182,

        /// <summary>
        /// Lesson Progress
        /// </summary>
        LessonProgressPacket = 183,

        /// <summary>
        /// Sent from client to server. Used to request an ability change.
        /// </summary>
        RequestAbilityPacket = 184,

        /// <summary>
        /// Sent from client to server. Used to request a new Permissions Levels.
        /// </summary>
        RequestPermissionsPacket = 185,

        /// <summary>
        /// Pushes a UI toast message to be displayed by the client
        /// </summary>
        ToastRequestPacket = 186,

        /// <summary>
        /// Sent by the server to update the state of a player's Abilities.
        /// </summary>
        UpdateAbilitiesPacket = 187,

        /// <summary>
        /// UpdateAdventureSettingsPacket
        /// </summary>
        UpdateAdventureSettingsPacket = 188,

        /// <summary>
        /// Sent from the server to client when player dies (Level::onPlayerDeath).
        /// </summary>
        DeathInfoPacket = 189,

        /// <summary>
        /// General use Editor specific packet - carries a payload of whatever serialized data that the individual IEditorNetworkPayload generates.
        /// </summary>
        EditorNetworkPacket = 190,

        /// <summary>
        /// This is the packet that tracks the active feature registry data from the server so that client can place the features themselves.
        /// </summary>
        FeatureRegistryPacket = 191,

        /// <summary>
        /// Used to send performance and other valuable stats back to the client
        /// </summary>
        ServerStatsPacket = 192,

        /// <summary>
        /// Requests tunable options from host to client (compression threshold and algorithm).
        /// </summary>
        RequestNetworkSettingsPacket = 193,

        /// <summary>
        /// Internal Text Packet
        /// </summary>
        GameTestRequestPacket = 194,

        /// <summary>
        /// Game Test Results Packet
        /// </summary>
        GameTestResultsPacket = 195,

        /// <summary>
        /// UpdateClientInputLocksPacket
        /// </summary>
        UpdateClientInputLocksPacket = 196,

        /// <summary>
        /// Used to sync CameraPresets data from server to clients.
        /// </summary>
        CameraPresetsPacket = 198,

        /// <summary>
        /// Sent from server to client, for all previously unlocked recipes on load and for any newly unlocked recipes during gameplay.
        /// </summary>
        UnlockedRecipesPacket = 199,

        /// <summary>
        /// Used to send a CameraInstruction from the server to the specified clients.
        /// </summary>
        CameraInstructionPacket = 300,

        /// <summary>
        /// Sent from the server on level startup to send all trim patterns to the client.
        /// </summary>
        TrimDataPacket = 302,

        /// <summary>
        /// Sent from the server so that the client knows to open the sign screen.
        /// </summary>
        OpenSignPacket = 303,

        /// <summary>
        /// Broadcasted to other players when an Agent performs an animation so it gets properly replicated.
        /// </summary>
        AgentAnimationPacket = 304,

        /// <summary>
        /// Refresh Entitlements
        /// </summary>
        RefreshEntitlementsPacket = 305,

        /// <summary>
        /// PlayerToggleCrafterSlotRequestPacket
        /// </summary>
        PlayerToggleCrafterSlotRequestPacket = 306,

        /// <summary>
        /// SetPlayerInventoryOptionsPacket
        /// </summary>
        SetPlayerInventoryOptionsPacket = 307,

        /// <summary>
        /// This packet is only used via the set hud command. This is for 3rd party content.
        /// </summary>
        SetHudPacket = 308,

        /// <summary>
        /// Contains the ID of the achievement to award
        /// </summary>
        AwardAchievementPacket = 309,

        /// <summary>
        /// Sent from the server to client to force close all server forms on the stack and return to the HUD screen.
        /// </summary>
        ClientboundCloseFormPacket = 310,

        /// <summary>
        /// Sent from the client to the server to message to the server about the state of the loading screen.
        /// </summary>
        ServerboundLoadingScreenPacket = 312,

        /// <summary>
        /// Jigsaw Structure data used by client jigsaw structure worldgen. This packet contains a copy of the behavior pack jigsaw structure rules.
        /// </summary>
        JigsawStructureDataPacket = 313,

        /// <summary>
        /// Informs the client of which Structure Feature they are currently occupying.
        /// </summary>
        CurrentStructureFeaturePacket = 314,

        /// <summary>
        /// Sent from the client to the server IF ProfilerLite is enabled AND the creator toggle for additional client telemetry is enabled AND new telemetry data is ready (every 500 ms).
        /// </summary>
        ServerboundDiagnosticsPacket = 315,

        /// <summary>
        /// CameraAimAssist
        /// </summary>
        CameraAimAssistPacket = 316,

        /// <summary>
        /// This is used to trigger a clientside cleanup of the dynamic container registry.
        /// </summary>
        ContainerRegistryCleanupPacket = 317,

        /// <summary>
        /// These packets are sent to the client to update specific MovementEffects
        /// </summary>
        MovementEffectPacket = 318,

        /// <summary>
        /// Camera aim-assist registry presets/categories data sent from the server to clients.
        /// </summary>
        CameraAimAssistPresetsPacket = 320,

        /// <summary>
        /// Client-side activation of aim-assist
        /// </summary>
        ClientCameraAimAssistPacket = 321,

        /// <summary>
        /// Sends client movement prediction information to the server
        /// </summary>
        ClientMovementPredictionSyncPacket = 322,

        /// <summary>
        /// Sync the player's options (mostly settings) to the server.
        /// </summary>
        UpdateClientOptionsPacket = 323,

        /// <summary>
        /// Used by a test command to start/stop video capture
        /// </summary>
        PlayerVideoCapturePacket = 324,

        /// <summary>
        /// Player Update Entity Overrides
        /// </summary>
        PlayerUpdateEntityOverridesPacket = 325,

        /// <summary>
        /// Updates the client with the position of a player outside simulation distance, or hides a player.
        /// </summary>
        PlayerLocationPacket = 326,

        /// <summary>
        /// Set the control scheme that the player should use
        /// </summary>
        ClientboundControlSchemeSetPacket = 327,

        /// <summary>
        /// Send primitive drawing shape info (from scripting) to the client for rendering
        /// </summary>
        PrimitiveShapesPacket = 328,

        /// <summary>
        /// Sent from the client to the server when players change Pack Settings (pack UI).
        /// </summary>
        ServerboundPackSettingChangePacket = 329,

        /// <summary>
        /// Sends a list of data store properties from the server to the client.
        /// </summary>
        ClientboundDataStorePacket = 330,

        /// <summary>
        /// Sent from the server to the client when a server script changes the rendering settings
        /// </summary>
        GraphicsOverrideParameterPacket = 331,

        /// <summary>
        /// Applies a single update to the server data store from the client.
        /// </summary>
        ServerboundDataStorePacket = 332,

        /// <summary>
        /// Allows the server to tell the client to show a Data Driven UI screen.
        /// </summary>
        ClientboundDataDrivenUIShowScreenPacket = 333,

        /// <summary>
        /// Allows the server to tell the client to close Data Driven UI screens.
        /// </summary>
        ClientboundDataDrivenUICloseScreenPacket = 334,

        /// <summary>
        /// Allows the server to tell the client to reload the Data Driven UI.
        /// </summary>
        ClientboundDataDrivenUIReloadPacket = 335,

        /// <summary>
        /// Sends a set of update properties for the texture shift system from the server to the client.
        /// </summary>
        ClientboundTextureShiftPacket = 336,

        /// <summary>
        /// Syncs client with server voxel shape data on world join. This packet contains a copy of all behavior pack voxel shapes data and is used by StartGamePacket.
        /// </summary>
        VoxelShapesPacket = 337,

        /// <summary>
        /// Camera custom spline data sent from server to client.
        /// </summary>
        CameraSplinePacket = 338,

        /// <summary>
        /// Camera aim-assist actor priority data sent from the server to clients.
        /// </summary>
        CameraAimAssistActorPriorityPacket = 339,

        /// <summary>
        /// Used to inform the server that the client has finished loading all resource packs.
        /// </summary>
        ResourcePacksReadyForValidationPacket = 340,

        /// <summary>
        /// Syncs LocatorBar changes on the server with the client.
        /// </summary>
        LocatorBarPacket = 341,

        /// <summary>
        /// Sent by the client to provide additional client metadata.
        /// </summary>
        PartyChangedPacket = 342,

        /// <summary>
        /// Sent from the client to the server when a data driven screen is closed.
        /// </summary>
        ServerboundDataDrivenScreenClosedPacket = 343,

        /// <summary>
        /// Initializes and syncs world clocks from the server to clients. (Currently disabled)
        /// </summary>
        SyncWorldClocksPacket = 344,

        /// <summary>
        /// Syncs Attribute Layers to the Client (Currently disabled)
        /// </summary>
        ClientboundAttributeLayerSyncPacket = 345,

        /// <summary>
        /// Sent by the server to provide ClientStoreEntryPointConfiguration to the client.
        /// </summary>
        ServerStoreInfoPacket = 346,

        /// <summary>
        /// Sent by the server to provide PresenceConfiguration to the client.
        /// </summary>
        ServerPresenceInfoPacket = 347,

        /// <summary>
        /// Sent to update sound data.
        /// </summary>
        ClientboundUpdateSoundDataPacket = 348,

        /// <summary>
        /// Sent by the server to a client with a party destination cookie.
        /// </summary>
        SendPartyDestinationCookiePacket = 349,

        /// <summary>
        /// Sent by the client to the server with a party destination cookie response.
        /// </summary>
        PartyDestinationCookieResponsePacket = 350,

        /// <summary>
        /// SetPlayerFurnaceOptionsPacket
        /// </summary>
        SetPlayerFurnaceOptionsPacket = 351,

        /// <summary>
        /// Sent to notify the client that a record started playing at a block position.
        /// </summary>
        RecordStartedPacket = 352,

        /// <summary>
        /// Informs the client when its matchmaking state changes.
        /// </summary>
        ClientboundMatchmakingStatePacket = 353,

        /// <summary>
        /// When a player selects a recipe in the stonecutter UI on the client, the client sends this packet. Then the server synchronizes the selected recipe in the server-side stonecutter model.
        /// </summary>
        ServerboundStonecutterSetRecipePacket = 354,

        /// <summary>
        /// When the server updates a recipe in the stonecutter UI model, the server sends this packet. Then the client synchronizes the selected recipe in the client-side stonecutter model.
        /// </summary>
        ClientboundStonecutterSetRecipePacket = 355,

        /// <summary>
        /// Requests cancellation of the sending player's matchmaking ticket.
        /// </summary>
        ServerboundMatchmakingCancelPacket = 356,

        /// <summary>
        /// Sent by server only.
        /// </summary>
        SetPassengerOfBlockPacket = 357,

        /// <summary>
        /// Serverbound Cursor Item Drag
        /// </summary>
        ServerboundCursorItemDragPacket = 358,

        /// <summary>
        /// Provides signed audio content and requests playback.
        /// </summary>
        ClientboundPlayAudioContentPacket = 359,

        /// <summary>
        /// Registers signed audio content for the sending player.
        /// </summary>
        ServerboundRegisterAudioContentPacket = 360,

        /// <summary>
        /// Player chat routed through Mojang Managed Chat
        /// </summary>
        ChatPacket = 361,

        /// <summary>
        /// Requests online music playback information for a sound event.
        /// </summary>
        ServerboundRequestOnlineMusicPlaybackPacket = 362,

        /// <summary>
        /// Provides online music playback information for a correlated request.
        /// </summary>
        ClientboundOnlineMusicPlaybackResponsePacket = 363,

        /// <summary>
        /// Requests that the server update the sending player's armor visibility.
        /// </summary>
        PlayerToggleArmorVisibilityRequestPacket = 364,

        /// <summary>
        /// Reports music discs that could not play so the client can classify their failures.
        /// </summary>
        ClientboundMusicPlaybackFailurePacket = 365,

    }

}
