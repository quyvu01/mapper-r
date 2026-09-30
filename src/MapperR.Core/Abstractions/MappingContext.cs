using System.Runtime.CompilerServices;
using MapperR.Core.Implementations;

namespace MapperR.Core.Abstractions;

/// <summary>
/// State of one top-level map call. Nested members are mapped by calling the child pair's mapper at runtime
/// through this context, so a pair never builds its children up front and cycles between types are harmless.
/// The context also guards against runaway nesting and, for pairs that can form cycles, remembers already-mapped
/// objects so cyclic object graphs keep their shape.
/// </summary>
public sealed class MappingContext
{
    /// <summary>Nesting depth beyond which mapping throws instead of overflowing the stack.</summary>
    public const int MaxDepth = 256;

    private readonly IMapperResolver _resolver;
    private readonly bool _shared;
    private Dictionary<(object Source, Type Destination), object> _visited;
    private int _depth;

    internal MappingContext(IMapperResolver resolver)
    {
        _resolver = resolver;
        AllowNullCollections = resolver.AllowNullCollections;
    }

    /// <summary>Whether a <c>null</c> source collection maps to <c>null</c> instead of an empty collection.</summary>
    internal bool AllowNullCollections { get; }

    /// <summary>
    /// A context shared by every call of a resolver, for pairs that cannot reach a cycle: it counts no depth and
    /// remembers no objects, so it holds no state and is safe to use from any thread.
    /// </summary>
    internal MappingContext(IMapperResolver resolver, bool shared) : this(resolver) => _shared = shared;

    public TDestination Map<TSource, TDestination>(TSource source)
    {
        if (source is null) return default;
        if (_shared) return _resolver.Get<TSource, TDestination>().Map(source, this);

        Enter<TSource, TDestination>();
        try
        {
            return _resolver.Get<TSource, TDestination>().Map(source, this);
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>
    /// Updates <paramref name="destination"/> in place; creates it when it is null, returns <c>default</c> when the
    /// source is null.
    /// </summary>
    public TDestination MapInto<TSource, TDestination>(TSource source, TDestination destination)
    {
        if (source is null) return default;
        if (destination is null) return Map<TSource, TDestination>(source);
        if (_shared) return _resolver.Get<TSource, TDestination>().Map(source, destination, this);

        Enter<TSource, TDestination>();
        try
        {
            return _resolver.Get<TSource, TDestination>().Map(source, destination, this);
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>Whether <paramref name="source"/> was already mapped to a <typeparamref name="TDestination"/> in this call.</summary>
    public bool TryGetVisited<TDestination>(object source, out TDestination destination)
    {
        if (_visited is not null && _visited.TryGetValue((source, typeof(TDestination)), out var found))
        {
            destination = (TDestination)found;
            return true;
        }

        destination = default;
        return false;
    }

    /// <summary>Remembers the destination created for <paramref name="source"/>; must happen before its members are mapped.</summary>
    public TDestination Register<TDestination>(object source, TDestination destination)
    {
        (_visited ??= new Dictionary<(object, Type), object>(VisitedKeyComparer.Instance))
            [(source, typeof(TDestination))] = destination;
        return destination;
    }

    private void Enter<TSource, TDestination>()
    {
        if (++_depth <= MaxDepth) return;

        _depth--;
        throw new InvalidOperationException(
            $"Mapping {typeof(TSource).Name} -> {typeof(TDestination).Name} exceeded the maximum nesting depth of " +
            $"{MaxDepth}: the source object graph is too deep, or contains a cycle between pairs that are not tracked.");
    }

    /// <summary>Source objects are compared by reference, so value-equal records are still distinct objects.</summary>
    private sealed class VisitedKeyComparer : IEqualityComparer<(object Source, Type Destination)>
    {
        public static readonly VisitedKeyComparer Instance = new();

        public bool Equals((object Source, Type Destination) x, (object Source, Type Destination) y) =>
            ReferenceEquals(x.Source, y.Source) && x.Destination == y.Destination;

        public int GetHashCode((object Source, Type Destination) key) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(key.Source), key.Destination);
    }
}
