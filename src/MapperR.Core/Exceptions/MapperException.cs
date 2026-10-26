using System.Diagnostics;
using System.Linq.Expressions;

namespace MapperR.Core.Exceptions;

/// <summary>
/// Builds every exception the library throws, so the wording lives in one place:
/// <c>throw MapperException.ProfileNotFound(source, destination);</c>
/// </summary>
internal static class MapperException
{
    /// <summary>No profile is registered for the pair (and none can be derived from collection elements).</summary>
    public static InvalidOperationException ProfileNotFound(Type source, Type destination) =>
        new($"Cannot find a profile for {Name(source)} -> {Name(destination)}. " +
            $"Add CreateMap<{Name(source)}, {Name(destination)}>().");

    /// <summary>A member needs the profile of another pair, and that pair has no profile.</summary>
    public static InvalidOperationException MissingProfile(Type ownerSource, Type ownerDestination, string member,
        Type source, Type destination) =>
        new($"{Name(ownerSource)} -> {Name(ownerDestination)}: member '{member}' maps {Name(source)} to " +
            $"{Name(destination)}, but no profile exists for that pair. Add CreateMap<{Name(source)}, " +
            $"{Name(destination)}>() or Ignore the member.");

    /// <summary>The source type of a member cannot be converted to the type of the destination member.</summary>
    public static InvalidOperationException UnsupportedConversion(Type ownerSource, Type ownerDestination,
        string member, Type from, Type to) =>
        new($"{Name(ownerSource)} -> {Name(ownerDestination)}: member '{member}' cannot be mapped from " +
            $"{Name(from)} to {Name(to)}. Use MapFrom(...) with an explicit conversion, or Ignore the member.");

    /// <summary>The destination of <c>ForMember</c> is not a direct member access such as <c>x => x.Name</c>.</summary>
    public static ArgumentException InvalidSelector(Expression selector, string paramName) =>
        new($"Expression '{selector}' must be a direct member access on the parameter (e.g. 'x => x.Name'), " +
            "not a nested path (e.g. 'x => x.Address.Name') or any other shape.", paramName);

    /// <summary>A member has no <c>MapFrom</c> and the source has no member with the same name.</summary>
    public static InvalidOperationException NoSourceMember(Type source, Type destination, string member) =>
        new($"'{Name(destination)}.{member}' has no MapFrom and {Name(source)} has no member named '{member}'. " +
            "Add MapFrom(...) or Ignore().");

    /// <summary>A feature that is planned but not built yet.</summary>
    public static NotImplementedException NotImplemented(string feature) =>
        new($"{feature} is not supported yet.");

    /// <summary>A state that the library itself should make impossible: a bug, not a usage error.</summary>
    public static UnreachableException Unreachable(string message) => new(message);

    /// <summary>Type name for messages: <c>List&lt;Address&gt;</c> instead of <c>List`1</c>.</summary>
    private static string Name(Type type)
    {
        if (!type.IsGenericType) return type.Name;
        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
    }
}
