// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BedrockLan.Bedrock;
using BedrockLan.Bedrock.Packets;
using BedrockLan.Internal;
using BedrockLan.NetherNet;
using BedrockLan.RakNet;
using System.Net;
using Xunit;

namespace BedrockLan.Tests;

public class NetherNetTests
{
    [Fact]
    public void ServerData_MatchesReferenceEncoding()
    {
        // Vector from df-mc/go-nethernet discovery/server_data_test.go.
        var data = new NetherNetServerData
        {
            ServerName = "server",
            Protocol = 2181,
            Version = "1.26.50",
            LevelName = "world",
            PlayerCount = 1,
            MaxPlayerCount = 8,
            GameType = 2,
            EditorWorld = false,
            Hardcore = true,
            AcceptsOnlineAuth = true,
            AcceptsSelfSignedAuth = true,
            Nonce = "nonce",
            ConnectionType = 4,
        };

        byte[] expected =
        {
            0x07,
            0x06, (byte)'s', (byte)'e', (byte)'r', (byte)'v', (byte)'e', (byte)'r',
            0x8A, 0x22,
            0x07, (byte)'1', (byte)'.', (byte)'2', (byte)'6', (byte)'.', (byte)'5', (byte)'0',
            0x05, (byte)'w', (byte)'o', (byte)'r', (byte)'l', (byte)'d',
            0x02, 0x10, 0x04,
            0x00, 0x01, 0x01, 0x01,
            0x05, (byte)'n', (byte)'o', (byte)'n', (byte)'c', (byte)'e',
            0x08,
        };

        Assert.Equal(expected, data.ToBytes());
        Assert.True(NetherNetServerData.TryParse(expected, out var parsed));
        Assert.Equal("world", parsed!.LevelName);
        Assert.Equal(2181, parsed.Protocol);
    }

    [Fact]
    public void DiscoveryPacket_RoundTripsMessage()
    {
        var bytes = DiscoveryPacketCodec.EncodeMessage(12345, 67890, "CONNECTREQUEST 42 v=0");
        Assert.Equal(0, (bytes.Length - 32) % 16);
        Assert.True(DiscoveryPacketCodec.TryDecode(bytes, out var packet));
        Assert.Equal(DiscoveryPacketType.Message, packet!.Type);
        Assert.Equal(12345UL, packet.SenderId);
        var (recipient, text) = DiscoveryPacketCodec.DecodeMessageBody(packet.Body);
        Assert.Equal(67890UL, recipient);
        Assert.Equal("CONNECTREQUEST 42 v=0", text);
    }

    [Fact]
    public void DiscoveryPacket_RejectsTamperedChecksum()
    {
        var bytes = DiscoveryPacketCodec.EncodeRequest(1);
        bytes[0] ^= 0xFF;
        Assert.False(DiscoveryPacketCodec.TryDecode(bytes, out _));
    }

    [Fact]
    public void DiscoveryResponse_IsLowercaseHex()
    {
        var bytes = DiscoveryPacketCodec.EncodeResponse(7, new byte[] { 0xAB, 0x01 });
        Assert.True(DiscoveryPacketCodec.TryDecode(bytes, out var packet));
        Assert.Equal(new byte[] { 4, 0, 0, 0, (byte)'a', (byte)'b', (byte)'0', (byte)'1' }, packet!.Body);
        Assert.Equal(new byte[] { 0xAB, 0x01 }, DiscoveryPacketCodec.DecodeResponseBody(packet.Body));
    }

    [Fact]
    public void Signal_ParsesAndFormats()
    {
        Assert.True(NetherNetSignal.TryParse("CANDIDATEADD 99 candidate:1 1 udp 1 1.2.3.4 5 typ host", out var signal));
        Assert.Equal(NetherNetSignal.Candidate, signal.Type);
        Assert.Equal(99UL, signal.ConnectionId);
        Assert.Equal("candidate:1 1 udp 1 1.2.3.4 5 typ host", signal.Data);
        Assert.Equal("CONNECTERROR 5 11", NetherNetSignal.ErrorSignal(5, 11).ToString());
        Assert.False(NetherNetSignal.TryParse("Ping", out _));
    }

    [Fact]
    public void Sdp_AddsTrickledCandidatesToMediaSection()
    {
        const string offer = "v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0\r\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\nc=IN IP4 0.0.0.0\r\na=mid:0\r\n";
        var result = Sdp.AddCandidates(offer, new[] { "candidate:1 1 udp 2130706431 192.168.1.5 50000 typ host", "a=candidate:1 1 udp 2130706431 192.168.1.5 50000 typ host" });
        Assert.EndsWith("a=mid:0\r\na=candidate:1 1 udp 2130706431 192.168.1.5 50000 typ host\r\na=end-of-candidates\r\n", result);
        Assert.True(Sdp.HasCompleteCandidates(result));
        Assert.Single(Sdp.ExtractCandidates(result));
    }

    [Fact]
    public void Identity_CanonicalFingerprintsMatchReference()
    {
        var json = NetherNetServerIdentity.CanonicalFingerprints(new[] { ("sha-256", "4A:AD") });
        Assert.Equal("{\"fingerprint\":[{\"algorithm\":\"sha-256\",\"digest\":\"4A:AD\"}]}", json);
    }
}

public class RakNetTests
{
    [Fact]
    public void Motd_RoundTripsAndPreservesExtraFields()
    {
        const string raw = "MCPE;My Server;800;1.21.80;3;20;123456;Bedrock level;Survival;1;19132;19133;0;";
        Assert.True(BedrockMotd.TryParse(raw, out var motd));
        Assert.Equal("My Server", motd.Motd);
        Assert.Equal(20, motd.MaxPlayers);
        Assert.Equal(raw, motd.ToString());
    }

    [Fact]
    public void Address_RoundTripsIPv4()
    {
        var writer = new ByteWriter();
        RakNetProtocol.WriteAddress(writer, new IPEndPoint(IPAddress.Parse("192.168.1.20"), 19132));
        var bytes = writer.ToArray();
        Assert.Equal(new byte[] { 4, 0x3F, 0x57, 0xFE, 0xEB, 0x4A, 0xBC }, bytes);
        var endPoint = RakNetProtocol.ReadAddress(new ByteReader(bytes));
        Assert.Equal("192.168.1.20:19132", endPoint.ToString());
    }

    [Fact]
    public void Connection_AcceptsConnectionRequestAndAcks()
    {
        var sent = new List<byte[]>();
        var remote = new IPEndPoint(IPAddress.Loopback, 50000);
        var connection = new RakNetConnection(remote, 1400, -1, 99, (data, _) => sent.Add(data), null);

        var request = new ByteWriter();
        request.Byte(RakNetProtocol.ConnectionRequest);
        request.Int64BE(-1);
        request.Int64BE(1000);
        request.Bool(false);
        var body = request.ToArray();

        var datagram = new ByteWriter();
        datagram.Byte(0x84);
        datagram.UInt24LE(0);
        datagram.Byte(3 << 5); // reliable ordered
        datagram.UInt16BE((ushort)(body.Length << 3));
        datagram.UInt24LE(0); // reliable index
        datagram.UInt24LE(0); // order index
        datagram.Byte(0);     // channel
        datagram.Bytes(body);

        connection.HandleDatagram(datagram.ToArray());

        Assert.Contains(sent, d => d[0] == 0x84 && d.Length > 14 && d[14] == RakNetProtocol.ConnectionRequestAccepted);
        Assert.Contains(sent, d => d[0] == 0xC0);
    }
}

public class RelayApiTests
{
    [Theory]
    [InlineData("play.example.com", "play.example.com", null)]
    [InlineData("play.example.com:19133", "play.example.com", 19133)]
    [InlineData("[fd00::5]:19132", "fd00::5", 19132)]
    [InlineData("fd00::5", "fd00::5", null)]
    public void ParseAddress_HandlesHostsPortsAndIPv6(string input, string host, int? port)
    {
        Assert.Equal((host, port), BedrockRelay.ParseAddress(input));
    }

    [Fact]
    public void ParseAddress_RejectsBadPort()
    {
        Assert.Throws<FormatException>(() => BedrockRelay.ParseAddress("host:99999"));
    }

    [Fact]
    public void Options_ClampPollInterval()
    {
        var options = new BedrockRelayOptions { PollInterval = TimeSpan.FromMilliseconds(10) };
        Assert.Equal(BedrockRelayOptions.MinimumPollInterval, options.PollInterval);
    }

    [Fact]
    public async Task Relay_AddAndDisposeServer_RaisesEventsAndRemoves()
    {
        await using var relay = new BedrockRelay(new BedrockRelayOptions
        {
            BindAddress = IPAddress.Loopback,
            RakNetDiscoveryPort = 0,
            NetherNetDiscoveryPort = 0,
            RakNetFirstServerPort = 0,
            AutoEnableWebRtcTransfer = false,
            QueryTimeout = TimeSpan.FromMilliseconds(300),
        });

        var removed = new TaskCompletionSource<RelayServer>(TaskCreationOptions.RunContinuationsAsynchronously);
        relay.ServerRemoved += (_, e) => removed.TrySetResult(e.Server);

        var server = relay.AddServer("127.0.0.1:9", new RelayServerOptions { DisplayName = "Test" });
        Assert.Single(relay.Servers);
        Assert.Equal("Test", server.Name);
        Assert.Equal(9, server.Port);

        await server.DisposeAsync();
        await server.DisposeAsync(); // idempotent
        Assert.Empty(relay.Servers);
        Assert.True(server.IsDisposed);
        Assert.Same(server, await removed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Relay_ConcurrentAddRemove_IsSafe()
    {
        await using var relay = new BedrockRelay(new BedrockRelayOptions
        {
            BindAddress = IPAddress.Loopback,
            RakNetDiscoveryPort = 0,
            NetherNetDiscoveryPort = 0,
            RakNetFirstServerPort = 0,
            AutoEnableWebRtcTransfer = false,
        });

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            var server = relay.AddServer($"127.0.0.{i + 1}:9");
            relay.Options.PollInterval = TimeSpan.FromSeconds(1 + i % 3);
            await server.DisposeAsync();
        })));

        Assert.Empty(relay.Servers);
    }
}

public class BedrockTextTests
{
    [Theory]
    [InlineData("§5D§dr§5a§dg§5o§dn §7Standart", "Dragon Standart")]
    [InlineData("      §l§6BOSSCRAFT§r   1.17-1.21      ", "BOSSCRAFT 1.17-1.21")]
    [InlineData("Plain name", "Plain name")]
    [InlineData("trailing §", "trailing")]
    public void StripFormatting_RemovesCodesAndPadding(string input, string expected) =>
        Assert.Equal(expected, BedrockText.StripFormatting(input));
}
