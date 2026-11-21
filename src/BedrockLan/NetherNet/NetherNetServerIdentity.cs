// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BedrockLan.NetherNet;

/// <summary>
/// Long-lived operator key used to produce the server <c>a=identity</c> attribute that Minecraft requires in
/// every NetherNet SDP answer (self-signed ES384 JWT with a JWK <c>cpk</c> claim, plus a detached JWS over the
/// answer's DTLS fingerprints). Only needed when this process terminates WebRTC itself (transfer mode);
/// relayed answers already carry the upstream server's identity.
/// </summary>
public sealed class NetherNetServerIdentity : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly ECDsa _key;
    private readonly object _keyLock = new(); // ECDsa instances are not guaranteed thread-safe

    public NetherNetServerIdentity(ECDsa key, string domain)
    {
        if (key.KeySize != 384)
        {
            throw new ArgumentException("A P-384 key is required (ES384).", nameof(key));
        }

        _key = key;
        Domain = domain;
    }

    /// <summary>Shown to players as untrusted text in the first-use prompt.</summary>
    public string Domain { get; }

    /// <summary>SHA-256 of the public key (SubjectPublicKeyInfo), useful to publish so players can verify the prompt.</summary>
    public string PublicKeyFingerprint
    {
        get
        {
            lock (_keyLock)
            {
                return Convert.ToHexString(SHA256.HashData(_key.ExportSubjectPublicKeyInfo()));
            }
        }
    }

    /// <summary>Loads a PEM private key from <paramref name="path"/>, creating and saving one if it does not exist.</summary>
    public static NetherNetServerIdentity LoadOrCreate(string path, string domain)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        if (File.Exists(path))
        {
            key.ImportFromPem(File.ReadAllText(path));
        }
        else
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var writer = new StreamWriter(path, Encoding.ASCII, options);
            writer.Write(key.ExportPkcs8PrivateKeyPem());
        }

        return new NetherNetServerIdentity(key, domain);
    }

    /// <summary>Returns the answer SDP with a freshly signed session-level <c>a=identity</c> attribute.</summary>
    public string SignAnswer(string answerSdp)
    {
        var cleaned = Sdp.StripIdentity(answerSdp);
        var fingerprints = Sdp.GetFingerprints(cleaned);
        if (fingerprints.Count == 0)
        {
            throw new InvalidOperationException("Answer SDP has no a=fingerprint lines to sign.");
        }

        var assertion = JsonSerializer.Serialize(new
        {
            fingerprints = SignDetached(CanonicalFingerprints(fingerprints)),
            token = CreateToken(),
        }, JsonOptions);

        var envelope = JsonSerializer.Serialize(new
        {
            assertion,
            idp = new { domain = Domain, protocol = "default" },
        }, JsonOptions);

        var attribute = "a=identity:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(envelope));
        return Sdp.InsertSessionAttribute(cleaned, attribute);
    }

    /// <summary>Canonical JSON of the fingerprints, byte-for-byte as the client reconstructs it.</summary>
    internal static string CanonicalFingerprints(IReadOnlyList<(string Algorithm, string Digest)> fingerprints)
    {
        var sb = new StringBuilder("{\"fingerprint\":[");
        for (var i = 0; i < fingerprints.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append("{\"algorithm\":").Append(Quote(fingerprints[i].Algorithm))
              .Append(",\"digest\":").Append(Quote(fingerprints[i].Digest)).Append('}');
        }

        return sb.Append("]}").ToString();
    }

    private string CreateToken()
    {
        ECParameters parameters;
        byte[] spki;
        lock (_keyLock)
        {
            parameters = _key.ExportParameters(false);
            spki = _key.ExportSubjectPublicKeyInfo();
        }

        var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = JsonSerializer.Serialize(new
        {
            alg = "ES384",
            x5u = Convert.ToBase64String(spki),
        }, JsonOptions);
        var claims = JsonSerializer.Serialize(new
        {
            cpk = new
            {
                crv = "P-384",
                kty = "EC",
                x = Base64Url(parameters.Q.X!),
                y = Base64Url(parameters.Q.Y!),
            },
            exp = issuedAt + 3600,
            iat = issuedAt,
            iss = Domain,
        }, JsonOptions);

        var signingInput = Base64Url(Encoding.UTF8.GetBytes(header)) + "." + Base64Url(Encoding.UTF8.GetBytes(claims));
        return signingInput + "." + Base64Url(Sign(signingInput));
    }

    private string SignDetached(string payload)
    {
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"ES384\"}"));
        var signature = Sign(header + "." + Base64Url(Encoding.UTF8.GetBytes(payload)));
        return header + ".." + Base64Url(signature);
    }

    // IEEE P1363 (r||s), the JWS encoding for ES384, is .NET's default signature format.
    private byte[] Sign(string signingInput)
    {
        lock (_keyLock)
        {
            return _key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA384);
        }
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
        lock (_keyLock)
        {
            _key.Dispose();
        }
    }
}
