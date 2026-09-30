using MapperR.Core.Abstractions;
using MapperR.Core.Entities;
using MapperR.Core.Extensions;

namespace MapperR.Core.Helpers;

internal static class MemberClassifier
{
    internal static MapClassify ClassifyLocal(Type s, Type d)
    {
        if (d.IsAssignableFrom(s)) return MapClassify.Direct;
        var (us, ud) = (Unwrap(s), Unwrap(d));
        if (us.IsEnum && ud.IsEnum) return us == ud ? MapClassify.Numeric : MapClassify.EnumByName;
        if (IsNumericOrEnum(us) && IsNumericOrEnum(ud)) return MapClassify.Numeric;
        if (d == typeof(string)) return s.IsScalar() ? MapClassify.ToText : MapClassify.Invalid;
        if (IsLeaf(s) || IsLeaf(d)) return MapClassify.Invalid;
        return MapClassify.Candidate;
    }

    public readonly record struct Result(MapClassify Kind, IWireProfile NestedProfile, string Reason);

    public static Result Classify(Type s, Type d, Func<Type, Type, IWireProfile> find)
    {
        var local = ClassifyLocal(s, d);
        if (local == MapClassify.Invalid) return Invalid($"{s.Name} → {d.Name} has no conversion");
        if (local != MapClassify.Candidate) return new Result(local, null, null);

        if (find(s, d) is { } nested) return new Result(MapClassify.Nested, nested, null);

        if (GetElementType(s) is not null && GetElementType(d) is not null) return ClassifyCollection(s, d, find);

        return Invalid($"no CreateMap<{s.Name}, {d.Name}>() is registered");
    }

    /// <summary>
    /// A collection mapped to a collection: the destination must be a shape the mapper can build and the element
    /// pair must be mappable. Used for collection members and for <c>Map&lt;Dst[]&gt;(source)</c> alike. The result's
    /// <see cref="Result.NestedProfile"/> is the element pair when the elements are objects with their own profile.
    /// </summary>
    public static Result ClassifyCollection(Type s, Type d, Func<Type, Type, IWireProfile> find)
    {
        if (!IsSupportedCollectionDestination(d))
            return Invalid($"destination collection type {TypeNames.Describe(d)} is not supported; use an array, " +
                           "List<T>, HashSet<T>, IEnumerable<T>, ICollection<T>, IList<T>, IReadOnlyCollection<T> or " +
                           "IReadOnlyList<T>");

        var element = Classify(GetElementType(s)!, GetElementType(d)!, find);
        return element.Kind switch
        {
            MapClassify.Collection => Invalid("nested collections are not supported yet"),
            MapClassify.Invalid => Invalid($"elements: {element.Reason}"),
            _ => new Result(MapClassify.Collection, element.NestedProfile, null)
        };
    }

    private static readonly HashSet<Type> SupportedCollectionDefinitions =
    [
        typeof(List<>), typeof(HashSet<>), typeof(IEnumerable<>), typeof(ICollection<>), typeof(IList<>),
        typeof(IReadOnlyCollection<>), typeof(IReadOnlyList<>)
    ];

    /// <summary>
    /// The destinations a collection can be built as: a one-dimensional array, <c>List&lt;T&gt;</c>,
    /// <c>HashSet&lt;T&gt;</c>, and the interfaces a <c>List&lt;T&gt;</c> satisfies (they come back as a
    /// <c>List&lt;T&gt;</c>).
    /// </summary>
    internal static bool IsSupportedCollectionDestination(Type type) =>
        type.IsArray
            ? type.GetArrayRank() == 1
            : type.IsGenericType && SupportedCollectionDefinitions.Contains(type.GetGenericTypeDefinition());

    private static Result Invalid(string reason) => new(MapClassify.Invalid, null, reason);
    private static bool IsLeaf(Type type) => type == typeof(string) || type.IsScalar();

    private static bool IsNumericOrEnum(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType.IsEnum || Type.GetTypeCode(underlyingType) is >= TypeCode.SByte and <= TypeCode.Decimal;
    }

    /// <summary>
    /// Returns the element type of an enumerable type (array, <c>List&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>,
    /// custom collections implementing it, ...), or <c>null</c> if the type isn't a collection of something
    /// (strings are deliberately excluded, even though they implement <c>IEnumerable&lt;char&gt;</c>).
    /// </summary>
    internal static Type GetElementType(Type type)
    {
        if (type == typeof(string)) return null;
        if (type.IsArray) return type.GetElementType();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];

        var enumerableInterface = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableInterface?.GetGenericArguments()[0];
    }

    private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;
}