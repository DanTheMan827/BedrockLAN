using BedrockLan.Protocol.Definitions;

// Writes the protocol config generated from the C# definitions (src/BedrockLan.Protocol.Definitions).
// Building BedrockLan runs this automatically whenever a definition changes; by hand:
//
//   dotnet run --project tools/ProtocolGen                  update src/BedrockLan/Protocol/default-protocol.jsonc
//   dotnet run --project tools/ProtocolGen -- --check       fail if that file is out of date
//   dotnet run --project tools/ProtocolGen -- --out-dir X   also write X/protocol-<formatVersion>.jsonc (GitHub Pages)
//   dotnet run --project tools/ProtocolGen -- --stdout      print it instead
//   --default-path <file>                                   where the built-in copy is (default: found from the repo root)

var check = false;
var toStdout = false;
string? outDir = null;
string? builtIn = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--check":
            check = true;
            break;
        case "--stdout":
            toStdout = true;
            break;
        case "--out-dir" when i + 1 < args.Length:
            outDir = args[++i];
            break;
        case "--default-path" when i + 1 < args.Length:
            builtIn = args[++i];
            break;
        default:
            Console.Error.WriteLine($"Unknown option {args[i]}. Options: --check, --stdout, --out-dir <dir>, --default-path <file>.");
            return 2;
    }
}

var definition = DefaultProtocol.Definition;
var text = JsoncGenerator.Generate(definition);

if (toStdout)
{
    Console.Write(text);
    return 0;
}

builtIn ??= Path.Combine(FindRepoRoot(), "src", "BedrockLan", "Protocol", "default-protocol.jsonc");
if (check)
{
    var current = File.Exists(builtIn) ? Normalize(File.ReadAllText(builtIn)) : "";
    if (current != Normalize(text))
    {
        Console.Error.WriteLine($"{builtIn} is out of date. Build BedrockLan or run: dotnet run --project tools/ProtocolGen");
        return 1;
    }

    Console.WriteLine($"{builtIn} is up to date ({definition.ConfigVersion}).");
}
else
{
    Write(builtIn, text);
}

if (outDir is not null)
{
    Directory.CreateDirectory(outDir);
    Write(Path.Combine(outDir, $"protocol-{definition.FormatVersion}.jsonc"), text);
}

return 0;

// Writes only if the content changed (line endings aside), keeping the file's existing line endings.
static void Write(string path, string text)
{
    var existing = File.Exists(path) ? File.ReadAllText(path) : null;
    if (existing is not null && Normalize(existing) == Normalize(text))
    {
        Console.WriteLine($"{path} is up to date.");
        return;
    }

    var crlf = existing?.Contains("\r\n") ?? false;
    File.WriteAllText(path, crlf ? text.Replace("\n", "\r\n") : text);
    Console.WriteLine($"Wrote {path}");
}

static string Normalize(string text) => text.Replace("\r\n", "\n").TrimStart('﻿');

static string FindRepoRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BedrockLan.sln")))
            {
                return dir.FullName;
            }
        }
    }

    throw new InvalidOperationException("Can't find the repository root (BedrockLan.sln); pass --default-path.");
}
