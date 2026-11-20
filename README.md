# BedrockLan

A .NET library and command-line advertiser that make Minecraft: Bedrock Edition servers show up under
**Worlds → LAN Games**, with the real server's name, level, version and player count. Game traffic is
**never proxied**: a client either connects straight to the real server, or gets a single Transfer packet that
points it there.

```
                      ┌──────────────────────────── this process ────────────────────────────┐
 Bedrock client ──►   │ UDP 7551  NetherNet LAN discovery ─► minimal NetherNet server ─► Transfer │ ──► NetherNet server
   (LAN)              │ UDP 19132 RakNet LAN discovery    ─► minimal RakNet server    ─► Transfer │ ──► RakNet server
                      └────────────────────────────────────────────────────────────────────────────┘    (client reconnects)
```

## Why there are two paths

Bedrock **26.60 (27 Oct 2026)** removed RakNet from the client. Current clients find LAN games only through
**NetherNet discovery (UDP 7551)** and connect over **WebRTC**. Clients up to 26.5x still use RakNet discovery
on UDP 19132. So:

| Upstream | How it is advertised | What happens when a client joins |
|---|---|---|
| **NetherNet** (BDS 26.50+, Geyser with NetherNet) | NetherNet LAN, status from `GET /v1/join`; also RakNet LAN for older clients. | **NetherNet clients:** a minimal built-in NetherNet server accepts the LAN join and sends a **Transfer** to `host:port`. The client then connects to the server itself, with its own identity. **RakNet clients:** Transfer to `host:port`. |
| **NetherNet LAN host** (a world opened to LAN on another subnet or VPN) | NetherNet LAN, status from a unicast discovery request. | LAN signaling is relayed under a separate relay id per client; the WebRTC connection is direct. |
| **RakNet** (older BDS, PocketMine, Nukkit, Geyser in RakNet mode) | RakNet LAN, status from an unconnected ping. | A minimal RakNet server runs NetworkSettings → Login → PlayStatus → empty resource packs → **Transfer**. Only clients older than 26.60 can do this, since newer clients can't speak RakNet. |

In every case game traffic goes straight between the client and the real server. This process sends at most
one Transfer packet.

## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cd src/BedrockLanAdvertiser
dotnet run -- play.example.com 192.168.1.20:19133     # servers as arguments, no config needed
dotnet run -- --config servers.json                   # or a config file
```

Options: `--config <file>`, `--poll <seconds>`, `--verbose`, `--no-raknet`, `--no-nethernet`,
`--protocol <file|url|builtin>`. Persistent data (the NetherNet operator key) goes in `./persist` under the
working directory.

Open these firewall ports on the advertising machine: **UDP 7551**, **UDP 19132**, and **UDP 19140 and up**
(one per RakNet entry). Run it on the same broadcast domain as the players: discovery is broadcast-based and
doesn't cross routers.

### servers.json

List hosts; nothing else is required:

```jsonc
{
  "servers": [
    "play.example.com",
    "192.168.10.20",
    "10.8.0.15:19133",
    { "host": "192.168.10.30", "name": "Survival (basement PC)" }
  ]
}
```

**The type is detected by querying each host.** These probes run at the same time:

| Probe | If it answers, the host is… |
|---|---|
| `GET http://host:port/v1/join`, then `https://…` | a NetherNet server (HTTP signaling) |
| NetherNet discovery request to `host:7551` | a NetherNet LAN host (a world opened to LAN) |
| RakNet unconnected ping to `host:port` (UDP) | a RakNet server |

If more than one answers, HTTP signaling wins over the LAN host, and the LAN host over RakNet. A Geyser server
with both transports is treated as NetherNet, and pre-26.60 clients still get a Transfer to the same address.
The port defaults to 19132. A server that stays offline past `offlineAfterSeconds` is probed again from scratch,
so an upgraded or replaced server is picked up without a restart.

An entry is a `"host[:port]"` string (IPv6 as `"[fd00::5]:19132"`), or an object to override something:

| Key | Meaning |
|---|---|
| `host` | Host name or IP, optionally with `:port`. |
| `name`, `level` | Override the forwarded server name and level/sub-MOTD. |
| `type` | `raknet`, `nethernet` or `nethernet-lan` to skip detection. Default `auto`. |
| `transferHost`, `transferPort` | Destination written into Transfer packets, if it differs from the query address. |
| `advertiseRakNet`, `advertiseNetherNet` | `false` keeps this entry off one discovery path. |
| `localPort` | Fixed UDP port for this entry's minimal RakNet server. |
| `netherNetMode` | `auto` (default), `transfer` or `relay`. See [NetherNet joins](#nethernet-joins). |
| `https` | Explicit `type: nethernet` only: try https first (default http). Joins fall back to the other scheme if the first gets no response. |
| `allowInvalidCertificate` | Accept https signaling certificates that don't validate (default `true`; NetherNet servers often use self-signed ones). |
| `enabled` | `false` keeps an entry in the file without advertising it. |

Optional top-level settings, with defaults: `bindAddress` (`0.0.0.0`), `pollIntervalSeconds` (5),
`offlineAfterSeconds` (20), `hideOfflineServers` (true), `transferTiming` (`AfterSpawn`, `AfterResourcePacks` or
`Immediate`), `verbose` (false), `protocol` (see [Protocol config](#protocol-config)), `protocolRefreshHours` (12),
`raknet { enabled, discoveryPort: 19132, firstServerPort: 19140 }` and
`nethernet { enabled, discoveryPort: 7551, candidateWindowMilliseconds: 500 }`.

### Docker

A multi-arch image (amd64, arm64) is published to the GitHub Container Registry:

```bash
docker run -d --name bedrock-lan --restart unless-stopped --network host \
    -v bedrock-lan-data:/data ghcr.io/dantheman827/bedrocklan:latest play.example.com 192.168.1.20:19133
```

Or `docker compose up -d` with the included `docker-compose.yml` (edit its `command:` to list your servers).
To build the image yourself: `docker build -t bedrock-lan .`

- **`--network host` is required.** LAN discovery uses UDP broadcasts on 19132 and 7551, which Docker's bridge
  network doesn't carry. Host networking works on Linux. Docker Desktop on Windows and macOS runs containers in a
  VM whose broadcasts don't reach your LAN; run the advertiser natively there.
- **`/data` is the working directory.** The NetherNet operator key is kept in `/data/persist`; use a volume to
  keep the same key (and avoid repeat trust prompts) across upgrades.
- **Config file instead of arguments:** mount it at `/data/servers.json` and pass no arguments, or pass
  `--config /data/servers.json`. A bind-mounted directory must be writable by the container user (UID 1654,
  `app`).
- The container runs as that unprivileged user; every port it uses is above 1024.

Tags: `latest` (the `master` branch), `sha-<commit>`, and `<version>` / `<major>.<minor>` for `v*` git tags.

## NetherNet joins

By default (`auto`), a NetherNet client that joins from the LAN list is answered by a minimal NetherNet server
built into the advertiser (on [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery)). It signs its answer
with an `a=identity`, logs the client in just far enough, and sends a **Transfer** to `host:port`. The client then
connects to the real server through that server's own HTTP signaling, with its own signed identity.

**Transfer timing.** A 26.60+ client ignores a Transfer sent during login, so the default `AfterSpawn` first starts
a minimal empty game (StartGame → chunk radius → PlayStatus(PlayerSpawn)) and sends the Transfer once the client
reports it has spawned. This runs for protocol 2207 (1.26.60 previews) and newer; older clients fall back to
`AfterResourcePacks`.

**Relaying instead (opt-in).** With `"netherNetMode": "relay"`, the client's offer is posted to the server's
`/v1/join` and its answer is sent back, so the client connects directly. If the server refuses the relayed offer
(authenticated servers do: a LAN offer carries no player identity, giving `400`, `401`, or `500` with body `37`) or
doesn't answer within 4 seconds, the built-in server answers the same offer instead; a refusal is remembered for
30 minutes. Use it only for servers known to work this way: a server can accept the relayed offer and the client
still fail to connect (seen with a 1.26.61 dedicated server, error `InitialConnection-94`), and that failure isn't
visible to the advertiser. Signaling tries the detected scheme first, then the other.

NetherNet LAN hosts are always relayed, since they have no `host:port` to transfer to.

| `netherNetMode` | Behaviour |
|---|---|
| `auto` | Transfer when the WebRTC server is built in, otherwise relay. |
| `relay` | Relay, falling back to transfer if the server refuses. |
| `transfer` | Always answer with the built-in server and Transfer. |

To build without SIPSorcery: `dotnet build -p:EnableWebRtc=false`. NetherNet servers then fall back to relay.

The operator key is saved to `nethernet-identity.pem` on first run. Players may see a one-time trust prompt for
it; its fingerprint is printed at startup so players can check it.

## Using the library

```csharp
using BedrockLan;

await using var relay = new BedrockRelay(new BedrockRelayOptions
{
    Logger = (level, message) => Console.WriteLine($"{level}: {message}"),
});

var server = relay.AddServer("server.example.com:19132");      // type auto-detected in the background
server.Transferred   += (_, e) => Console.WriteLine($"{e.Client} -> {e.Target} via {e.Transport}");
server.StatusChanged += (_, e) => Console.WriteLine($"{e.Server.Name}: {e.Current.Players}/{e.Current.MaxPlayers}");
server.OnlineChanged += (_, e) => Console.WriteLine($"{e.Server.Name} online: {e.IsOnline}");

await server.RefreshAsync();                                    // optional: wait for the first query
Console.WriteLine($"{server.Name} [{server.Kind}] {server.Players}/{server.MaxPlayers} v{server.Version}");

relay.Options.PollInterval = TimeSpan.FromSeconds(2);           // live; takes effect immediately
server.DisplayName = "Friday Night Survival";                   // live; next LAN ping uses it

await server.DisposeAsync();                                    // stop advertising just this one
// disposing the relay stops and removes everything
```

- **`BedrockRelay`** binds the discovery sockets and starts polling as soon as it's constructed.
  `AddServer(address, options?)` / `AddServerAsync(...)` add servers; `Servers` returns a snapshot. Relay-wide
  events mirror the per-server ones: `ServerAdded`, `ServerRemoved`, `Transferred`, `StatusChanged`,
  `OnlineChanged`, `KindDetected`.
- **`RelayServer`** exposes identity (`Address`, `Host`, `Port`, `Kind`), live details (`Name`, `Level`,
  `Players`, `MaxPlayers`, `Version`, `Protocol`, and an immutable `Status` snapshot), health (`IsOnline`,
  `LastUpdated`, `LastError`), transfers (`TransferTarget`, `TransferCount`) and advertising (`RakNetPort`,
  `IsAdvertisedOnRakNet`, `IsAdvertisedOnNetherNet`). `DisplayName`, `LevelName`, `TransferHost` and
  `TransferPort` can be changed at runtime. Disposing a server removes it from its relay.
- **`BedrockRelayOptions`**: `init` properties (sockets, ports, persist folder) are fixed at creation. The rest,
  including `PollInterval`, `QueryTimeout`, `OfflineAfter`, `HideOfflineServers`, `Logger` and everything under
  `Transfer`, can be changed from any thread while the relay runs.
- **WebRTC transfer:** when the `BedrockLan.WebRtc` assembly is referenced it's picked up automatically. Its
  operator key is kept in `./persist/nethernet-identity.pem`; `PersistDirectory` changes the folder.
- **Thread safety:** every public member can be called concurrently. Events are raised in order on a dedicated
  event thread, never under internal locks, so a handler can call back into the relay or even dispose it.

In the NetherNet LAN list the client shows the *world name* as the title, so the relay puts the server's name
there and its second MOTD line in the host-name slot. `DisplayName` always sets the title.

**Server names:** formatting codes (`§`) are stripped from relayed names and levels, along with the padding
servers use to centre their MOTD. `ServerStatus.Name` and `Level` are cleaned; `RawName` and `RawLevel` keep the
original.

## Protocol config

Everything the transfer server says to a client is data, not code: each packet's layout per protocol version, and
the conversation itself (which client packet each stage waits for, what's sent back, when to transfer). The config
is JSON with comments and trailing commas, and documents its format at the top of
[`default-protocol.jsonc`](src/BedrockLan/Protocol/default-protocol.jsonc).

**Updates without a new build.** The latest config is published to GitHub Pages at
`https://dantheman827.github.io/BedrockLAN/protocol-<format>.jsonc` (`ProtocolConfig.DefaultUpdateUrl`). The
number is the config format the library reads (`protocol-1.jsonc` today), so a future format can't break older
builds. Downloads follow HTTP caching (`ProtocolDownloader`):
- While the copy is fresh per `Cache-Control: max-age`/`Expires`, no request is made.
- After that, requests carry `If-None-Match`/`If-Modified-Since`; an unchanged file gets `304`.
- Anything but a `200` with a valid config is logged and ignored, and the current config stays in use.

The advertiser uses the published URL by default and checks again every `protocolRefreshHours`. `"protocol"` in
`servers.json` or `--protocol` takes a file, another URL, or `builtin`; a broken local file stops startup, a URL
never does. In the library, `ProtocolConfig.Parse(text)` validates a config completely (types, ranges, packet and
stage names, variables); set it on `BedrockRelayOptions.Transfer.Protocol`, live.

**Generated from C#.** The config comes from definitions in `src/BedrockLan.Protocol.Definitions`:
- **Packets** (`Packets/Packets.cs`) and the **types they use** (`Types/`, e.g. `LevelSettings`,
  `ServerBlockProperty`, `GatheringsConfig`) are classes named and laid out as in
  [Mojang's protocol docs](https://mojang.github.io/bedrock-protocol-docs/). Attributes describe the wire format:
  `[Field(order, Wire = ...)]`, `[Since]`/`[Until]` (protocol cut-offs), `[Optional]` (`[OPT]` fields), `[Count]`
  (array count type), `[Variable("worldName")]` (value from the session) and `[Doc("...")]` (a comment in the
  config). Property initializers are the values sent. A field whose layout changed is declared twice with the
  same `Name` and disjoint ranges.
- **The session** (`DefaultProtocol.cs`) lists the stages, reads, steps and conditions.

Building `BedrockLan` runs `tools/ProtocolGen` first, which rewrites `src/BedrockLan/Protocol/default-protocol.jsonc`
when a definition changed; commit that file with the change. The tool only runs when its inputs changed; a build
without `tools/` (such as the Docker image) embeds the committed file; `-p:GenerateProtocolConfig=false` turns it
off. By hand: `dotnet run --project tools/ProtocolGen` (`-- --check` to only compare, `-- --out-dir site` to also
write `site/protocol-<format>.jsonc`).

To follow a game update: edit the definitions, bump `DefaultProtocol.ConfigVersion`, build, and push with the
regenerated JSONC. Newer protocols than the last cut-off get the newest layout.

## Development

```bash
dotnet test tests/BedrockLan.Tests
```

The tests check that the built-in config matches the generator's output; that it writes exactly the bytes the
definitions describe, for every protocol; that both write fixed known-good bytes for every packet at every
cut-off; that each packet matches readers written from Mojang's docs; and that the transfer session follows the
handshake for every documented protocol. NetherNet discovery is checked against byte-exact vectors from
[df-mc/go-nethernet](https://github.com/df-mc/go-nethernet).

**Workflows** (`.github/workflows`):
- **Publish protocol config** — on pushes to `master` that touch the library (and on demand): builds, tests,
  checks the committed JSONC is current, and publishes `protocol-<format>.jsonc` to GitHub Pages.
- **Docker image** — on pushes to `master`, `v*` tags (and on demand): builds the image for amd64 and arm64 and
  pushes it to `ghcr.io/dantheman827/bedrocklan`.

## Status and known limitations

Confirmed with real clients: detection, status forwarding, LAN listing, and the NetherNet transfer end to end with
a 26.60 client (WebRTC handshake, temporary game, Transfer after spawning). Packet layouts were checked against
Mojang's docs up to protocol 2259 (1.26.70-preview.26).

- **Several LAN entries from one IP.** Each RakNet entry answers from its own port with its own GUID, and each
  NetherNet entry has its own network id. Client de-duplication rules aren't documented.
- RakNet IPv6 discovery (UDP 19133) isn't implemented.
- If a real server on the same machine holds UDP 19132 or 7551 exclusively, that discovery path is disabled with a
  warning.

## Layout

```
src/BedrockLan/                       core library (no dependencies)
  RakNet/                             discovery, ping, minimal reliability layer, transfer server
  Bedrock/                            batch/compression codec, TransferSession
  NetherNet/                          LAN discovery crypto and hub, HTTP signaling, bridges, identity
  Protocol/                           protocol config parser, packet builder, built-in config
  Relay/                              BedrockRelay / RelayServer public API, options, events
src/BedrockLan.WebRtc/                optional SIPSorcery-based NetherNet transfer server
src/BedrockLan.Protocol.Definitions/  C# source of the protocol config (shared source, not a project)
src/BedrockLanAdvertiser/             command-line advertiser + sample servers.json
tests/BedrockLan.Tests/               xUnit tests
tools/ProtocolGen/                    generates the protocol config from the definitions
```
