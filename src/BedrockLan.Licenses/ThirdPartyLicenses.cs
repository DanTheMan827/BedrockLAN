// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;

namespace BedrockLan.Licenses;

/// <summary>
/// The licenses of the open-source components BedrockLan uses or redistributes: the repository's licenses folder,
/// embedded in this assembly at build time.
/// </summary>
public static class ThirdPartyLicenses
{
    private const string ResourcePrefix = "licenses/";

    private static readonly Lazy<ReadOnlyDictionary<string, string>> s_all = new(Load);

    /// <summary>
    /// Every license, keyed by project name (the file name without ".txt"; keys are case-insensitive). Each value is
    /// the whole file with "\n" line endings: the project's name on the first line, then its URL, license, what it's
    /// used for, and the full license text.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetAll() => s_all.Value;

    private static ReadOnlyDictionary<string, string> Load()
    {
        var assembly = typeof(ThirdPartyLicenses).Assembly;
        var licenses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            var name = Path.GetFileNameWithoutExtension(resource[ResourcePrefix.Length..]);
            licenses[name] = reader.ReadToEnd().Replace("\r\n", "\n").Trim();
        }

        return licenses.AsReadOnly();
    }
}
