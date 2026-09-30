using System.Text;

namespace MapperR.Core.Helpers;

/// <summary>
/// Rejects <c>CreateMap</c> pairs that have no meaning (DESIGN #24), at the call, so the mistake is reported where
/// it was made instead of surfacing later as an obscure error while mapping.
/// </summary>
internal static class ProfileValidator
{
    public static void ValidateMap(Type source, Type destination)
    {
        var sourceIsCollection = IsFrameworkCollection(source);
        var destinationIsCollection = IsFrameworkCollection(destination);
        if (!sourceIsCollection && !destinationIsCollection) return;

        var pair = $"CreateMap<{Describe(source)}, {Describe(destination)}>()";
        if (sourceIsCollection != destinationIsCollection)
            throw new InvalidOperationException(
                $"{pair}: a collection cannot be mapped to a single object or the other way round. " +
                "Map each element with its own CreateMap, or map a member that holds the collection.");

        var (sourceElement, destinationElement) =
            (MemberClassifier.GetElementType(source)!, MemberClassifier.GetElementType(destination)!);
        throw new InvalidOperationException(
            $"{pair}: collections are not mapped through a profile of their own; they are mapped through their " +
            $"element pair. Register CreateMap<{Describe(sourceElement)}, {Describe(destinationElement)}>() instead.");
    }

    /// <summary>
    /// Arrays and the collection types of the framework (<c>List&lt;T&gt;</c>, <c>HashSet&lt;T&gt;</c>,
    /// <c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, <c>Dictionary&lt;,&gt;</c>, ...). A type you define
    /// yourself that derives from a collection (say, a paged list with a total count) has members of its own to
    /// map, so it is still a valid object pair.
    /// </summary>
    internal static bool IsFrameworkCollection(Type type) =>
        type.IsArray ||
        (MemberClassifier.GetElementType(type) is not null &&
         type.Namespace is { } ns && (ns == "System.Collections" || ns.StartsWith("System.Collections.", StringComparison.Ordinal)));

    /// <summary>A readable name for messages: <c>List&lt;Person&gt;</c> rather than <c>List`1</c>.</summary>
    private static string Describe(Type type)
    {
        if (type.IsArray) return $"{Describe(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";
        if (!type.IsGenericType) return type.Name;

        var name = type.Name;
        var builder = new StringBuilder(name[..name.IndexOf('`')]).Append('<');
        builder.AppendJoin(", ", type.GetGenericArguments().Select(Describe));
        return builder.Append('>').ToString();
    }
}
