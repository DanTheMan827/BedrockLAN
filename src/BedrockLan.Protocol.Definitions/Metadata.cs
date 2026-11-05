using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace BedrockLan.Protocol.Definitions;

internal enum FieldKind
{
    Primitive,
    Struct,
    Array,
    Nbt,
}

/// <summary>What the attributes say about one [Field] property.</summary>
internal sealed class FieldMeta
{
    public required PropertyInfo Property { get; init; }

    /// <summary>Name in a declared type (camelCase property name unless [Field(Name = ...)]).</summary>
    public required string Name { get; init; }

    public required int Order { get; init; }

    /// <summary>The config's type name: a primitive, a declared type, NetworkNbt or Array&lt;...&gt;.</summary>
    public required string TypeName { get; init; }

    public required FieldKind Kind { get; init; }

    /// <summary>The primitive encoding (for an array of primitives: the element's).</summary>
    public Wire Wire { get; init; }

    /// <summary>For an array: the element's CLR type.</summary>
    public Type? ElementType { get; init; }

    public Wire? Count { get; init; }

    public int? Since { get; init; }

    public int? Until { get; init; }

    public bool Optional { get; init; }

    public string? Variable { get; init; }

    public string? Doc { get; init; }

    public bool AppliesTo(int protocol) => (Since is null || protocol >= Since) && (Until is null || protocol < Until);
}

/// <summary>Reads the definition attributes (cached per type).</summary>
internal static class Metadata
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<FieldMeta>> Cache = new();

    public static bool IsDeclaredType(Type type) => type.GetCustomAttribute<ProtocolTypeAttribute>() is not null;

    public static string TypeName(Type type) => type.GetCustomAttribute<ProtocolTypeAttribute>()?.Name ?? type.Name;

    public static int PacketId(Type type) =>
        type.GetCustomAttribute<PacketAttribute>()?.Id ?? throw new InvalidOperationException($"{type.Name} has no [Packet] attribute.");

    public static string? Doc(MemberInfo member) => member.GetCustomAttribute<DocAttribute>()?.Text;

    public static IReadOnlyList<FieldMeta> Fields(Type type) => Cache.GetOrAdd(type, Read);

    private static IReadOnlyList<FieldMeta> Read(Type type)
    {
        var fields = new List<FieldMeta>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<FieldAttribute>() is not { } field)
            {
                continue;
            }

            var clr = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            FieldKind kind;
            string typeName;
            var wire = field.Wire;
            Type? element = null;
            if (typeof(NbtCompound).IsAssignableFrom(clr))
            {
                kind = FieldKind.Nbt;
                typeName = "NetworkNbt";
                wire = Wire.NetworkNbt;
            }
            else if (IsDeclaredType(clr))
            {
                kind = FieldKind.Struct;
                typeName = TypeName(clr);
            }
            else if (clr != typeof(string) && clr != typeof(byte[]) && typeof(IEnumerable).IsAssignableFrom(clr))
            {
                kind = FieldKind.Array;
                element = clr.IsArray ? clr.GetElementType()! : clr.GetGenericArguments().Single();
                if (IsDeclaredType(element))
                {
                    typeName = $"Array<{TypeName(element)}>";
                }
                else
                {
                    wire = PrimitiveWire(element, wire, type, property);
                    typeName = $"Array<{wire}>";
                }
            }
            else
            {
                kind = FieldKind.Primitive;
                wire = PrimitiveWire(clr, wire, type, property);
                typeName = wire.ToString();
            }

            var count = property.GetCustomAttribute<CountAttribute>()?.Wire;
            if (count is not null && kind != FieldKind.Array)
            {
                throw new InvalidOperationException($"{type.Name}.{property.Name}: [Count] is only for lists.");
            }

            fields.Add(new FieldMeta
            {
                Property = property,
                Name = field.Name ?? CamelCase(property.Name),
                Order = field.Order,
                TypeName = typeName,
                Kind = kind,
                Wire = wire,
                ElementType = element,
                Count = count,
                Since = property.GetCustomAttribute<SinceAttribute>()?.Protocol,
                Until = property.GetCustomAttribute<UntilAttribute>()?.Protocol,
                Optional = property.GetCustomAttribute<OptionalAttribute>() is not null,
                Variable = property.GetCustomAttribute<VariableAttribute>()?.Name,
                Doc = Doc(property),
            });
        }

        var ordered = fields.OrderBy(f => f.Order).ToList();
        var duplicate = ordered.GroupBy(f => f.Order).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"{type.Name}: two fields have order {duplicate.Key}.");
        }

        return ordered;
    }

    private static Wire PrimitiveWire(Type clr, Wire declared, Type owner, PropertyInfo property)
    {
        if (declared != Wire.Auto)
        {
            return declared;
        }

        if (clr == typeof(bool))
        {
            return Wire.Bool;
        }

        if (clr == typeof(float))
        {
            return Wire.FloatLE;
        }

        if (clr == typeof(string))
        {
            return Wire.String;
        }

        if (clr == typeof(byte))
        {
            return Wire.Byte;
        }

        if (clr == typeof(byte[]))
        {
            return Wire.Bytes;
        }

        throw new InvalidOperationException($"{owner.Name}.{property.Name}: {clr.Name} fields need [Field(n, Wire = ...)].");
    }

    private static string CamelCase(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
