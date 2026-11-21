// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text.Json;

namespace BedrockLan.Protocol;

/// <summary>
/// Reads and validates a protocol config (JSON with comments and trailing commas). Everything is checked up
/// front, so a config that parses can't fail later on a bad reference or type: unknown keys, packets, stages
/// and variables, wrong value types and out-of-range numbers are all rejected with the path to the problem.
/// </summary>
internal static class ProtocolConfigParser
{
    public const int SupportedFormatVersion = ProtocolFormat.Version;

    /// <summary>Variables every session provides.</summary>
    public static readonly IReadOnlySet<string> BuiltInVariables = new HashSet<string>(StringComparer.Ordinal)
    {
        "protocol", "target.host", "target.port", "worldName", "offlineMessage", "spawnTimeout", "uuid.random", "timing",
    };

    private static readonly IReadOnlySet<FieldType> ReadableTypes = new HashSet<FieldType>
    {
        FieldType.Byte, FieldType.Bool, FieldType.UInt16LE, FieldType.Int32LE, FieldType.Int32BE,
        FieldType.UInt32LE, FieldType.VarInt32, FieldType.VarUInt32, FieldType.String,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
    };

    public static ProtocolConfig Parse(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new ProtocolConfigException($"Not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var parser = new Parser();
            return parser.ParseRoot(document.RootElement, text);
        }
    }

    private sealed class Parser
    {
        // Variables used (path → name) and stored by reads, checked once everything is parsed.
        private readonly List<(string Path, string Name)> _used = new();

        // Types declared in the config's "types" section.
        private readonly Dictionary<string, StructSpec> _types = new(StringComparer.Ordinal);
        private readonly HashSet<string> _stored = new(StringComparer.Ordinal) { "protocol" };

        public ProtocolConfig ParseRoot(JsonElement root, string text)
        {
            var o = Object(root, "$");
            Keys(o, "$", "formatVersion", "configVersion", "types", "packets", "session");
            var format = Int(Required(o, "formatVersion", "$"), "formatVersion");
            if (format > SupportedFormatVersion)
            {
                throw Error("formatVersion", $"format {format} is newer than this version of the library understands ({SupportedFormatVersion}); update the app");
            }

            if (format < 1)
            {
                throw Error("formatVersion", "must be 1 or more");
            }

            var configVersion = o.TryGetValue("configVersion", out var v) ? String(v, "configVersion") : "";

            if (o.TryGetValue("types", out var types))
            {
                ParseTypes(types, "types");
            }

            var packets = new Dictionary<string, PacketSpec>(StringComparer.Ordinal);
            foreach (var (name, element) in Object(Required(o, "packets", "$"), "packets"))
            {
                packets[name] = ParsePacket(name, element, $"packets.{name}");
            }

            var session = ParseSession(Required(o, "session", "$"), "session");
            Validate(packets, session);
            return new ProtocolConfig(format, configVersion, packets, session, text);
        }

        private PacketSpec ParsePacket(string name, JsonElement element, string path)
        {
            var o = Object(element, path);
            Keys(o, path, "id", "body");
            var id = Int(Required(o, "id", path), $"{path}.id");
            if (id is < 0 or > 1023)
            {
                throw Error($"{path}.id", "packet ids are 0-1023");
            }

            var body = new List<FieldSpec>();
            var i = 0;
            foreach (var field in Array(Required(o, "body", path), $"{path}.body"))
            {
                body.Add(ParseField(field, $"{path}.body[{i++}]"));
            }

            return new PacketSpec(name, id, body);
        }

        private FieldSpec ParseField(JsonElement element, string path)
        {
            var o = Object(element, path);
            var (typeName, value) = TypeKey(o, path, "since", "until", "optional", "count");
            var type = ResolveType(typeName, o, path);
            var optional = o.TryGetValue("optional", out var opt) && Bool(opt, $"{path}.optional");
            return new FieldSpec(type, ParseValue(type, value, $"{path}.{typeName}", optional), Range(o, path), optional);
        }

        // ---- Declared types ----

        /// <summary>
        /// "types": { "Name": [ { "&lt;Type&gt;": "fieldName", modifiers... }, ... ] }. Names are collected first, so
        /// types can use each other in any order; a type that contains itself is rejected.
        /// </summary>
        private void ParseTypes(JsonElement element, string path)
        {
            var declarations = Object(element, path);
            foreach (var name in declarations.Keys)
            {
                if (Enum.TryParse<FieldType>(name, false, out _) || name.StartsWith("Array<", StringComparison.Ordinal) || !IsIdentifier(name))
                {
                    throw Error($"{path}.{name}", "type names must be identifiers and can't reuse a primitive name or Array<...>");
                }

                _types[name] = new StructSpec(name);
            }

            foreach (var (name, fields) in declarations)
            {
                var spec = _types[name];
                var i = 0;
                foreach (var field in Array(fields, $"{path}.{name}"))
                {
                    var fieldPath = $"{path}.{name}[{i++}]";
                    var o = Object(field, fieldPath);
                    var (typeName, fieldName) = TypeKey(o, fieldPath, "since", "until", "optional", "count");
                    var nameText = String(fieldName, $"{fieldPath}.{typeName}");
                    if (!IsIdentifier(nameText))
                    {
                        throw Error($"{fieldPath}.{typeName}", "a declared type's field is { \"<Type>\": \"fieldName\" }");
                    }

                    // A name may repeat for versioned variants of a field, as long as no protocol has both.
                    var range = Range(o, fieldPath);
                    if (spec.Fields.Any(f => f.Name == nameText && f.Range.Overlaps(range)))
                    {
                        throw Error(fieldPath, $"{name} already has a field \"{nameText}\" for some of these protocols");
                    }

                    var optional = o.TryGetValue("optional", out var opt) && Bool(opt, $"{fieldPath}.optional");
                    spec.Fields.Add(new StructFieldSpec(nameText, ResolveType(typeName, o, fieldPath), range, optional));
                }

                if (spec.Fields.Count == 0)
                {
                    throw Error($"{path}.{name}", "a type needs at least one field");
                }
            }

            foreach (var spec in _types.Values)
            {
                CheckNotRecursive(spec, new List<string>(), path);
            }
        }

        private static void CheckNotRecursive(StructSpec spec, List<string> chain, string path)
        {
            if (chain.Contains(spec.Name))
            {
                throw Error($"{path}.{spec.Name}", $"type contains itself ({string.Join(" > ", chain)} > {spec.Name})");
            }

            chain.Add(spec.Name);
            foreach (var field in spec.Fields)
            {
                var type = field.Type;
                while (type is ArrayType array)
                {
                    type = array.Element;
                }

                if (type is StructType inner)
                {
                    CheckNotRecursive(inner.Spec, chain, path);
                }
            }

            chain.RemoveAt(chain.Count - 1);
        }

        /// <summary>
        /// A type name: a wire primitive, a declared type, or Array&lt;T&gt; (with the field's "count" naming the count's
        /// own type: VarUInt32 by default, UInt32LE for the docs' "No size compression").
        /// </summary>
        private TypeRef ResolveType(string name, IReadOnlyDictionary<string, JsonElement> field, string path)
        {
            var count = field.TryGetValue("count", out var c) ? String(c, $"{path}.count") : null;
            if (count is not null && !name.StartsWith("Array<", StringComparison.Ordinal))
            {
                throw Error($"{path}.count", "\"count\" only applies to Array<...> fields");
            }

            return Resolve(name, count, path);
        }

        private TypeRef Resolve(string name, string? count, string path)
        {
            if (name.StartsWith("Array<", StringComparison.Ordinal) && name.EndsWith('>'))
            {
                var countType = count is null
                    ? FieldType.VarUInt32
                    : Enum.TryParse<FieldType>(count, false, out var ct) && CountTypes.Contains(ct) && !int.TryParse(count, out _)
                        ? ct
                        : throw Error($"{path}.count", $"the count is one of {string.Join(", ", CountTypes)}");
                return new ArrayType(Resolve(name[6..^1], null, path), countType);
            }

            if (Enum.TryParse<FieldType>(name, false, out var primitive) && !int.TryParse(name, out _))
            {
                return new PrimitiveType(primitive);
            }

            if (_types.TryGetValue(name, out var spec))
            {
                return new StructType(spec);
            }

            throw Error($"{path}.{name}", $"unknown type (primitives: {string.Join(", ", Enum.GetNames<FieldType>())}; "
                + $"declared: {(_types.Count == 0 ? "none" : string.Join(", ", _types.Keys))}; or Array<Type>)");
        }

        private static readonly FieldType[] CountTypes =
        {
            FieldType.VarUInt32, FieldType.UInt32LE, FieldType.UInt16LE, FieldType.Byte, FieldType.VarInt32, FieldType.Int32LE,
        };

        /// <summary>
        /// A value for a field of any type. A declared type takes an object of its fields (optional ones may be left
        /// out), an array of them in order, or, if it has a single field, just that field's value. Arrays take a JSON
        /// array. null is only for optional fields (written as absent).
        /// </summary>
        private ValueNode ParseValue(TypeRef type, JsonElement value, string path, bool optional)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return optional ? AbsentValue.Instance : throw Error(path, "null is only allowed for optional fields");
            }

            switch (type)
            {
                case PrimitiveType p:
                    return new ScalarValue(Value(p.Kind, value, path));

                case ArrayType array:
                    var items = new List<ValueNode>();
                    var i = 0;
                    foreach (var item in Array(value, path))
                    {
                        items.Add(ParseValue(array.Element, item, $"{path}[{i++}]", false));
                    }

                    return new ArrayValue(items);

                case StructType st:
                    var fields = st.Spec.Fields;
                    var values = new ValueNode[fields.Count];
                    var names = fields.Select(f => f.Name).Distinct().ToList();
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        var given = Object(value, path);
                        foreach (var key in given.Keys.Where(k => fields.All(f => f.Name != k)))
                        {
                            throw Error($"{path}.{key}", $"{st.Name} has no field \"{key}\" (fields: {string.Join(", ", names)})");
                        }

                        for (var j = 0; j < fields.Count; j++)
                        {
                            var field = fields[j];
                            if (given.TryGetValue(field.Name, out var v))
                            {
                                values[j] = ParseValue(field.Type, v, $"{path}.{field.Name}", field.Optional);
                            }
                            else if (field.Optional)
                            {
                                values[j] = AbsentValue.Instance;
                            }
                            else
                            {
                                throw Error(path, $"{st.Name} needs \"{field.Name}\"");
                            }
                        }
                    }
                    else if (value.ValueKind == JsonValueKind.Array)
                    {
                        var list = value.EnumerateArray().ToList();
                        if (list.Count != fields.Count)
                        {
                            throw Error(path, $"{st.Name} has {fields.Count} field(s) ({string.Join(", ", fields.Select(f => f.Name))}); {list.Count} given");
                        }

                        for (var j = 0; j < fields.Count; j++)
                        {
                            values[j] = ParseValue(fields[j].Type, list[j], $"{path}[{j}]", fields[j].Optional);
                        }
                    }
                    else if (fields.Count == 1)
                    {
                        values[0] = ParseValue(fields[0].Type, value, path, fields[0].Optional);
                    }
                    else
                    {
                        throw Error(path, $"{st.Name} takes an object of its fields ({string.Join(", ", names)})");
                    }

                    return new StructValue(values);

                default:
                    throw Error(path, "unsupported type");
            }
        }

        private static bool IsIdentifier(string name) =>
            name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(ch => char.IsLetterOrDigit(ch) || ch == '_');

        private ValueSpec Value(FieldType type, JsonElement value, string path)
        {
            if (value.ValueKind == JsonValueKind.String && value.GetString() is { } s && s.StartsWith('$'))
            {
                if (s.StartsWith("$$", StringComparison.Ordinal))
                {
                    return ValueSpec.Of(type == FieldType.String ? s[1..] : throw Error(path, "only String fields take text"));
                }

                if (type is FieldType.Bytes or FieldType.NetworkNbt)
                {
                    throw Error(path, $"{type} fields can't use variables");
                }

                var name = s[1..];
                _used.Add((path, name));
                return ValueSpec.Var(name);
            }

            return ValueSpec.Of(Literal(type, value, path));
        }

        private static object Literal(FieldType type, JsonElement value, string path)
        {
            switch (type)
            {
                case FieldType.Bool:
                    return value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => throw Error(path, "expected true or false"),
                    };
                case FieldType.String:
                    return String(value, path);
                case FieldType.FloatLE:
                    return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : throw Error(path, "expected a number");
                case FieldType.Bytes:
                    return Hex(String(value, path), path);
                case FieldType.NetworkNbt:
                    return NbtRootOf(value, path);
                default:
                    var number = Long(value, path);
                    var (min, max) = IntegerRange(type);
                    if (number < min || number > max)
                    {
                        throw Error(path, $"{number} is out of range for {type} ({min} to {max})");
                    }

                    return number;
            }
        }

        private static (long Min, long Max) IntegerRange(FieldType type) => type switch
        {
            FieldType.Byte => (sbyte.MinValue, byte.MaxValue),
            FieldType.Int16LE => (short.MinValue, short.MaxValue),
            FieldType.UInt16LE => (0, ushort.MaxValue),
            FieldType.Int32LE or FieldType.Int32BE or FieldType.VarInt32 => (int.MinValue, int.MaxValue),
            FieldType.UInt32LE or FieldType.VarUInt32 => (0, uint.MaxValue),
            FieldType.UInt64LE or FieldType.VarUInt64 => (0, long.MaxValue),
            _ => (long.MinValue, long.MaxValue),
        };

        private ReadSpec ParseRead(JsonElement element, string path)
        {
            var o = Object(element, path);
            var (typeName, value) = TypeKey(o, path, "keep", "clamp", "since", "until");
            if (!Enum.TryParse<FieldType>(typeName, false, out var type) || int.TryParse(typeName, out _) || !ReadableTypes.Contains(type))
            {
                throw Error(path, $"{typeName} can't be read (readable: {string.Join(", ", ReadableTypes)})");
            }

            var range = Range(o, path);
            var keep = o.TryGetValue("keep", out var k) && Bool(k, $"{path}.keep");
            long? clampMin = null, clampMax = null;
            if (o.TryGetValue("clamp", out var c))
            {
                var bounds = Array(c, $"{path}.clamp").ToList();
                if (bounds.Count != 2)
                {
                    throw Error($"{path}.clamp", "expected [min, max]");
                }

                clampMin = Long(bounds[0], $"{path}.clamp[0]");
                clampMax = Long(bounds[1], $"{path}.clamp[1]");
                if (clampMin > clampMax)
                {
                    throw Error($"{path}.clamp", "min is greater than max");
                }
            }

            if (value.ValueKind == JsonValueKind.String && value.GetString() is { } s && s.StartsWith('$') && !s.StartsWith("$$", StringComparison.Ordinal))
            {
                var name = s[1..];
                if (!BuiltInVariables.Contains(name) || name == "protocol")
                {
                    _stored.Add(name);
                }
                else
                {
                    throw Error($"{path}.{type}", $"${name} is provided by the session and can't be read into");
                }

                return new ReadSpec(type, name, null, keep, clampMin, clampMax, range);
            }

            if (keep || clampMin is not null)
            {
                throw Error(path, "\"keep\" and \"clamp\" only apply when reading into a variable");
            }

            var match = value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select((e, i) => MatchValue(type, e, $"{path}.{type}[{i}]")).ToList()
                : new List<object> { MatchValue(type, value, $"{path}.{type}") };
            if (match.Count == 0)
            {
                throw Error($"{path}.{type}", "expected at least one value to match");
            }

            return new ReadSpec(type, null, match, false, null, null, range);
        }

        private static object MatchValue(FieldType type, JsonElement value, string path) => type switch
        {
            FieldType.Bool => Bool(value, path),
            FieldType.String => String(value, path).StartsWith("$$", StringComparison.Ordinal) ? String(value, path)[1..] : String(value, path),
            _ => Literal(type, value, path),
        };

        private SessionSpec ParseSession(JsonElement element, string path)
        {
            var o = Object(element, path);
            Keys(o, path, "start", "transferPacket", "offlinePacket", "compressionHeaderSince", "stages");
            var start = String(Required(o, "start", path), $"{path}.start");
            var transfer = String(Required(o, "transferPacket", path), $"{path}.transferPacket");
            var offline = String(Required(o, "offlinePacket", path), $"{path}.offlinePacket");
            var header = o.TryGetValue("compressionHeaderSince", out var h) ? Int(h, $"{path}.compressionHeaderSince") : 649;

            var stages = new Dictionary<string, StageSpec>(StringComparer.Ordinal);
            foreach (var (name, stage) in Object(Required(o, "stages", path), $"{path}.stages"))
            {
                stages[name] = ParseStage(name, stage, $"{path}.stages.{name}");
            }

            return new SessionSpec(start, transfer, offline, header, stages);
        }

        private StageSpec ParseStage(string name, JsonElement element, string path)
        {
            var o = Object(element, path);
            Keys(o, path, "expect", "steps", "timeout");
            var expect = Object(Required(o, "expect", path), $"{path}.expect");
            Keys(expect, $"{path}.expect", "packet", "read");
            var packet = Int(Required(expect, "packet", $"{path}.expect"), $"{path}.expect.packet");
            var reads = new List<ReadSpec>();
            if (expect.TryGetValue("read", out var read))
            {
                var i = 0;
                foreach (var r in Array(read, $"{path}.expect.read"))
                {
                    reads.Add(ParseRead(r, $"{path}.expect.read[{i++}]"));
                }
            }

            var steps = ParseSteps(Required(o, "steps", path), $"{path}.steps");
            TimeoutSpec? timeout = null;
            if (o.TryGetValue("timeout", out var t))
            {
                var to = Object(t, $"{path}.timeout");
                Keys(to, $"{path}.timeout", "seconds", "steps");
                var seconds = Required(to, "seconds", $"{path}.timeout");
                var secondsSpec = Value(FieldType.FloatLE, seconds, $"{path}.timeout.seconds");
                if (secondsSpec.Literal is double d && d < 0)
                {
                    throw Error($"{path}.timeout.seconds", "can't be negative");
                }

                timeout = new TimeoutSpec(secondsSpec, ParseSteps(Required(to, "steps", $"{path}.timeout"), $"{path}.timeout.steps"));
            }

            return new StageSpec(name, packet, reads, steps, timeout);
        }

        private List<StepSpec> ParseSteps(JsonElement element, string path)
        {
            var steps = new List<StepSpec>();
            var i = 0;
            foreach (var step in Array(element, path))
            {
                steps.Add(ParseStep(step, $"{path}[{i++}]"));
            }

            return steps;
        }

        private StepSpec ParseStep(JsonElement element, string path)
        {
            var o = Object(element, path);
            Keys(o, path, "if", "log", "send", "compression", "goto", "transfer", "close");
            var condition = o.TryGetValue("if", out var c) ? ParseCondition(c, $"{path}.if") : null;
            var log = o.TryGetValue("log", out var l) ? String(l, $"{path}.log") : null;
            var send = new List<string>();
            if (o.TryGetValue("send", out var s))
            {
                if (s.ValueKind == JsonValueKind.String)
                {
                    send.Add(String(s, $"{path}.send"));
                }
                else
                {
                    var i = 0;
                    foreach (var name in Array(s, $"{path}.send"))
                    {
                        send.Add(String(name, $"{path}.send[{i++}]"));
                    }
                }
            }

            var compression = o.TryGetValue("compression", out var comp) && True(comp, $"{path}.compression");
            var endings = new List<(StepEnding Ending, string? Goto)>();
            if (o.TryGetValue("goto", out var g))
            {
                endings.Add((StepEnding.Goto, String(g, $"{path}.goto")));
            }

            if (o.TryGetValue("transfer", out var tr) && True(tr, $"{path}.transfer"))
            {
                endings.Add((StepEnding.Transfer, null));
            }

            if (o.TryGetValue("close", out var cl) && True(cl, $"{path}.close"))
            {
                endings.Add((StepEnding.Close, null));
            }

            if (endings.Count > 1)
            {
                throw Error(path, "a step can end with only one of goto, transfer or close");
            }

            var (ending, target) = endings.Count == 1 ? endings[0] : (StepEnding.None, null);
            return new StepSpec(condition, log, send, compression, ending, target);
        }

        private Condition ParseCondition(JsonElement element, string path)
        {
            var o = Object(element, path);
            if (o.Count == 0)
            {
                throw Error(path, "empty condition");
            }

            var parts = new List<Condition>();
            foreach (var (key, value) in o)
            {
                var p = $"{path}.{key}";
                switch (key)
                {
                    case "timing":
                        var names = value.ValueKind == JsonValueKind.Array
                            ? value.EnumerateArray().Select((e, i) => String(e, $"{p}[{i}]")).ToList()
                            : new List<string> { String(value, p) };
                        foreach (var name in names)
                        {
                            if (!Enum.TryParse<TransferTiming>(name, false, out _) || int.TryParse(name, out _))
                            {
                                throw Error(p, $"unknown timing \"{name}\" ({string.Join(", ", Enum.GetNames<TransferTiming>())})");
                            }
                        }

                        parts.Add(new TimingCondition(new HashSet<string>(names, StringComparer.Ordinal)));
                        break;
                    case "protocol":
                        var range = Object(value, p);
                        Keys(range, p, "min", "max");
                        int? min = range.TryGetValue("min", out var mn) ? Int(mn, $"{p}.min") : null;
                        int? max = range.TryGetValue("max", out var mx) ? Int(mx, $"{p}.max") : null;
                        if (min is null && max is null)
                        {
                            throw Error(p, "expected min and/or max");
                        }

                        parts.Add(new ProtocolCondition(min, max));
                        break;
                    case "targetOnline":
                        parts.Add(new TargetOnlineCondition(Bool(value, p)));
                        break;
                    case "any":
                    case "all":
                        var list = Array(value, p).Select((e, i) => ParseCondition(e, $"{p}[{i}]")).ToList();
                        if (list.Count == 0)
                        {
                            throw Error(p, "expected at least one condition");
                        }

                        parts.Add(key == "any" ? new AnyCondition(list) : new AllCondition(list));
                        break;
                    case "not":
                        parts.Add(new NotCondition(ParseCondition(value, p)));
                        break;
                    case "variable":
                        // { "variable": { "$name": value } } or { "$name": [values] }: the variable is one of them.
                        var vars = Object(value, p);
                        if (vars.Count != 1)
                        {
                            throw Error(p, "expected one variable, like { \"$name\": [1, 2] }");
                        }

                        var (varKey, varValues) = vars.First();
                        if (!varKey.StartsWith('$') || varKey.StartsWith("$$", StringComparison.Ordinal) || varKey.Length == 1)
                        {
                            throw Error($"{p}.{varKey}", "expected a variable name starting with $");
                        }

                        var varName = varKey[1..];
                        _used.Add(($"{p}.{varKey}", varName));
                        var accepted = (varValues.ValueKind == JsonValueKind.Array ? varValues.EnumerateArray().ToList() : new List<JsonElement> { varValues })
                            .Select((e, i) => ConditionValue(e, $"{p}.{varKey}[{i}]"))
                            .ToList();
                        if (accepted.Count == 0)
                        {
                            throw Error($"{p}.{varKey}", "expected at least one value");
                        }

                        parts.Add(new VariableCondition(varName, accepted));
                        break;
                    default:
                        throw Error(p, "unknown condition (timing, protocol, targetOnline, variable, any, all, not)");
                }
            }

            return parts.Count == 1 ? parts[0] : new AllCondition(parts);
        }

        private static object ConditionValue(JsonElement value, string path) => value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Number when value.TryGetInt64(out var n) => n,
            _ => throw Error(path, "expected an integer, a bool or a string"),
        };

        private void Validate(IReadOnlyDictionary<string, PacketSpec> packets, SessionSpec session)
        {
            void Packet(string name, string path)
            {
                if (!packets.ContainsKey(name))
                {
                    throw Error(path, $"no packet named \"{name}\"");
                }
            }

            void Stage(string name, string path)
            {
                if (!session.Stages.ContainsKey(name))
                {
                    throw Error(path, $"no stage named \"{name}\"");
                }
            }

            if (session.Stages.Count == 0)
            {
                throw Error("session.stages", "at least one stage is needed");
            }

            Stage(session.Start, "session.start");
            Packet(session.TransferPacket, "session.transferPacket");
            Packet(session.OfflinePacket, "session.offlinePacket");
            foreach (var stage in session.Stages.Values)
            {
                var path = $"session.stages.{stage.Name}";
                var stepLists = new List<(IReadOnlyList<StepSpec> Steps, string Path)> { (stage.Steps, $"{path}.steps") };
                if (stage.Timeout is { } timeout)
                {
                    stepLists.Add((timeout.Steps, $"{path}.timeout.steps"));
                }

                foreach (var (steps, stepsPath) in stepLists)
                {
                    for (var i = 0; i < steps.Count; i++)
                    {
                        foreach (var name in steps[i].Send)
                        {
                            Packet(name, $"{stepsPath}[{i}].send");
                        }

                        if (steps[i].Goto is { } target)
                        {
                            Stage(target, $"{stepsPath}[{i}].goto");
                        }
                    }
                }
            }

            foreach (var (path, name) in _used)
            {
                if (!BuiltInVariables.Contains(name) && !_stored.Contains(name))
                {
                    throw Error(path, $"unknown variable ${name}: not built in ({string.Join(", ", BuiltInVariables.Select(v => "$" + v))}) and no stage reads it");
                }
            }
        }

        // ---- Small JSON helpers ----

        /// <summary>The one key of a field object that isn't a modifier: its type name, and the value under it.</summary>
        private static (string TypeName, JsonElement Value) TypeKey(IReadOnlyDictionary<string, JsonElement> o, string path, params string[] modifiers)
        {
            var types = o.Keys.Where(k => !modifiers.Contains(k)).ToList();
            if (types.Count != 1)
            {
                throw Error(path, types.Count == 0
                    ? "expected one type key, like { \"VarInt32\": 0 }"
                    : $"expected one type key, found {string.Join(", ", types)} (modifiers: {string.Join(", ", modifiers)})");
            }

            return (types[0], o[types[0]]);
        }

        private static ProtocolRange Range(IReadOnlyDictionary<string, JsonElement> o, string path)
        {
            int? since = o.TryGetValue("since", out var s) ? Int(s, $"{path}.since") : null;
            int? until = o.TryGetValue("until", out var u) ? Int(u, $"{path}.until") : null;
            if (since is not null && until is not null && since >= until)
            {
                throw Error(path, $"since ({since}) must be below until ({until})");
            }

            return new ProtocolRange(since, until);
        }

        private static NbtRoot NbtRootOf(JsonElement value, string path)
        {
            var o = Object(value, path);
            Keys(o, path, "name", "compound");
            var name = o.TryGetValue("name", out var n) ? String(n, $"{path}.name") : "";
            var tag = NbtTagOf(new Dictionary<string, JsonElement> { ["compound"] = Required(o, "compound", path) }, path);
            return new NbtRoot(name, (NbtCompound)tag);
        }

        private static NbtTag NbtTagOf(IReadOnlyDictionary<string, JsonElement> o, string path)
        {
            if (o.Count != 1)
            {
                throw Error(path, "an NBT tag is one of { compound, list, string, byte, short, int, long, float }");
            }

            var (kind, value) = o.First();
            var p = $"{path}.{kind}";
            return kind switch
            {
                "compound" => new NbtCompound(Object(value, p).Select(e => new KeyValuePair<string, NbtTag>(e.Key, NbtTagOf(Object(e.Value, $"{p}.{e.Key}"), $"{p}.{e.Key}"))).ToList()),
                "list" => NbtListOf(value, p),
                "string" => new NbtString(String(value, p)),
                "byte" => new NbtByte((sbyte)Ranged(value, p, sbyte.MinValue, sbyte.MaxValue)),
                "short" => new NbtShort((short)Ranged(value, p, short.MinValue, short.MaxValue)),
                "int" => new NbtInt((int)Ranged(value, p, int.MinValue, int.MaxValue)),
                "long" => new NbtLong(Long(value, p)),
                "float" => value.ValueKind == JsonValueKind.Number ? new NbtFloat(value.GetSingle()) : throw Error(p, "expected a number"),
                _ => throw Error(p, "unknown NBT tag (compound, list, string, byte, short, int, long, float)"),
            };
        }

        private static NbtList NbtListOf(JsonElement value, string path)
        {
            var o = Object(value, path);
            Keys(o, path, "of", "items");
            var of = String(Required(o, "of", path), $"{path}.of");
            var typeId = (byte)(of switch
            {
                "byte" => 1,
                "short" => 2,
                "int" => 3,
                "long" => 4,
                "float" => 5,
                "string" => 8,
                "list" => 9,
                "compound" => 10,
                _ => throw Error($"{path}.of", "unknown NBT tag type"),
            });
            var items = new List<NbtTag>();
            var i = 0;
            foreach (var item in Array(Required(o, "items", path), $"{path}.items"))
            {
                var p = $"{path}.items[{i++}]";
                var tag = NbtTagOf(Object(item, p), p);
                if (tag.TypeId != typeId)
                {
                    throw Error(p, $"list items must all be {of}");
                }

                items.Add(tag);
            }

            return new NbtList(typeId, items);
        }

        private static long Ranged(JsonElement value, string path, long min, long max)
        {
            var n = Long(value, path);
            return n >= min && n <= max ? n : throw Error(path, $"{n} is out of range ({min} to {max})");
        }

        private static byte[] Hex(string text, string path)
        {
            var digits = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
            try
            {
                return Convert.FromHexString(digits);
            }
            catch (FormatException)
            {
                throw Error(path, "expected hex digits (spaces allowed), an even number of them");
            }
        }

        private static IReadOnlyDictionary<string, JsonElement> Object(JsonElement element, string path)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw Error(path, "expected an object");
            }

            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!result.TryAdd(property.Name, property.Value))
                {
                    throw Error(path, $"\"{property.Name}\" appears twice");
                }
            }

            return result;
        }

        private static IEnumerable<JsonElement> Array(JsonElement element, string path) =>
            element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToList() : throw Error(path, "expected an array");

        private static void Keys(IReadOnlyDictionary<string, JsonElement> o, string path, params string[] allowed)
        {
            foreach (var key in o.Keys)
            {
                if (!allowed.Contains(key))
                {
                    throw Error($"{path}.{key}", $"unknown key (expected: {string.Join(", ", allowed)})");
                }
            }
        }

        private static JsonElement Required(IReadOnlyDictionary<string, JsonElement> o, string key, string path) =>
            o.TryGetValue(key, out var value) ? value : throw Error(path, $"\"{key}\" is required");

        private static string String(JsonElement element, string path) =>
            element.ValueKind == JsonValueKind.String ? element.GetString()! : throw Error(path, "expected a string");

        private static bool Bool(JsonElement element, string path) => element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Error(path, "expected true or false"),
        };

        private static bool True(JsonElement element, string path) =>
            Bool(element, path) ? true : throw Error(path, "only true is allowed here (leave the key out instead)");

        private static long Long(JsonElement element, string path) =>
            element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var n) ? n : throw Error(path, "expected a whole number");

        private static int Int(JsonElement element, string path)
        {
            var n = Long(element, path);
            return n is >= int.MinValue and <= int.MaxValue ? (int)n : throw Error(path, "number out of range");
        }

        private static ProtocolConfigException Error(string path, string message) =>
            new($"{path}: {message}");
    }
}
