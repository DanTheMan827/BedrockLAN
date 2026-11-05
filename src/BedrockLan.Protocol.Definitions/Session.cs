using BedrockLan.Bedrock.Packets;

namespace BedrockLan.Protocol.Definitions;

/// <summary>The conversation with a client, from its first packet to the Transfer (the config's "session").</summary>
public sealed class SessionDefinition
{
    public required string Start { get; init; }

    public required string TransferPacket { get; init; }

    public required string OfflinePacket { get; init; }

    public int CompressionHeaderSince { get; init; } = 649;

    public List<StageDefinition> Stages { get; } = new();
}

/// <summary>A stage: waits for one client packet, reads it, then runs its steps.</summary>
public sealed class StageDefinition
{
    public StageDefinition(string name, PacketId expect)
    {
        Name = name;
        Expect = expect;
    }

    public string Name { get; }

    public PacketId Expect { get; }

    public string? Doc { get; init; }

    public List<ReadDefinition> Read { get; } = new();

    public List<StepDefinition> Steps { get; } = new();

    public TimeoutDefinition? Timeout { get; init; }
}

/// <summary>One value read from the client's packet: stored in a variable, or required to match.</summary>
public sealed record ReadDefinition(Wire Wire, string? Variable, IReadOnlyList<long>? Values, bool Keep, long? ClampMin, long? ClampMax)
{
    /// <summary>Store the value in <paramref name="variable"/> (only if unset with <paramref name="keep"/>).</summary>
    public static ReadDefinition Into(Wire wire, string variable, bool keep = false) => new(wire, variable, null, keep, null, null);

    public static ReadDefinition IntoClamped(Wire wire, string variable, long min, long max) => new(wire, variable, null, false, min, max);

    /// <summary>The value must be one of these, or the packet is ignored.</summary>
    public static ReadDefinition Match(Wire wire, params long[] values) => new(wire, null, values, false, null, null);
}

/// <summary>A step: optional condition, then log / send / compression, then at most one ending.</summary>
public sealed class StepDefinition
{
    public Condition? If { get; init; }

    public string? Log { get; init; }

    public List<string> Send { get; init; } = new();

    public bool Compression { get; init; }

    public string? Goto { get; init; }

    public bool Transfer { get; init; }

    public bool Close { get; init; }

    public string? Doc { get; init; }
}

public sealed class TimeoutDefinition
{
    /// <summary>A number of seconds, or a variable name like "$spawnTimeout".</summary>
    public required object Seconds { get; init; }

    public List<StepDefinition> Steps { get; init; } = new();
}

public abstract record Condition;

public sealed record TimingCondition(IReadOnlyList<TransferTiming> Timings) : Condition;

public sealed record ProtocolCondition(int? Min, int? Max) : Condition;

public sealed record TargetOnlineCondition(bool Online) : Condition;

public sealed record AnyCondition(IReadOnlyList<Condition> Conditions) : Condition;

public sealed record AllCondition(IReadOnlyList<Condition> Conditions) : Condition;

public sealed record NotCondition(Condition Condition) : Condition;

/// <summary>A variable (e.g. one a stage read) is one of these values.</summary>
public sealed record VariableCondition(string Name, IReadOnlyList<object> Values) : Condition;

/// <summary>Condition helpers: When.Timing(...), When.Protocol(min: ...), When.Any(...).</summary>
public static class When
{
    public static Condition Timing(params TransferTiming[] timings) => new TimingCondition(timings);

    public static Condition Protocol(int? min = null, int? max = null) => new ProtocolCondition(min, max);

    public static Condition TargetOnline(bool online = true) => new TargetOnlineCondition(online);

    public static Condition Any(params Condition[] conditions) => new AnyCondition(conditions);

    public static Condition All(params Condition[] conditions) => new AllCondition(conditions);

    public static Condition Not(Condition condition) => new NotCondition(condition);

    public static Condition Variable(string name, params long[] values) => new VariableCondition(name, values.Cast<object>().ToList());

    public static Condition Variable(string name, params string[] values) => new VariableCondition(name, values);
}

/// <summary>A whole protocol config: packets (named instances of [Packet] classes) and the session.</summary>
public sealed class ProtocolDefinition
{
    public required int FormatVersion { get; init; }

    public required string ConfigVersion { get; init; }

    /// <summary>Packets in output order: a name and an instance whose property values are the values sent.</summary>
    public required IReadOnlyList<(string Name, object Packet)> Packets { get; init; }

    public required SessionDefinition Session { get; init; }
}
