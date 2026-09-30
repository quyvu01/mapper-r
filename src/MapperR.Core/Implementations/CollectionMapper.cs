using System.Linq.Expressions;
using MapperR.Core.Abstractions;
using MapperR.Core.Entities;
using MapperR.Core.Helpers;
using MapperR.Core.Registries;

namespace MapperR.Core.Implementations;

/// <summary>
/// Maps a collection to a collection with no profile of its own (DESIGN #25): <c>Map&lt;Dst[]&gt;(list)</c>,
/// <c>Map&lt;List&lt;Dst&gt;&gt;(array)</c>, ... It is a pair of one member: the tree is the same expression a collection
/// member gets (<see cref="ValueExpressions.BuildCollection"/>), so elements follow the same rules and run through the
/// same optimization steps, and the elements use the element pair's mapper (generated or runtime).
/// <typeparamref name="TSource"/> is <c>IEnumerable&lt;TElement&gt;</c> whatever collection the caller passes, so one
/// mapper serves arrays, lists, sets and LINQ iterators alike.
/// </summary>
internal sealed class CollectionMapper<TSource, TDestination> : AbstractInternalMapper<TDestination>,
    IInternalMapper<TSource, TDestination>
{
    private readonly Lazy<Func<TSource, MappingContext, TDestination>> _compiledMap;
    private readonly ContextKind _contextKind;

    internal CollectionMapper(WireProfileRegistry registry, IMapperResolver resolver,
        MapperOptimizations optimizations) : base(resolver)
    {
        var classification = MemberClassifier.ClassifyCollection(typeof(TSource), typeof(TDestination), registry.Find);
        if (classification.Kind == MapClassify.Invalid)
            throw new InvalidOperationException(
                $"Cannot map {TypeNames.Describe(typeof(TSource))} to {TypeNames.Describe(typeof(TDestination))}: " +
                $"{classification.Reason}.");

        var source = Expression.Parameter(typeof(TSource), "source");
        var context = Expression.Parameter(typeof(MappingContext), "context");
        var map = RuntimeExpressionOptimizer.Inline(
            Expression.Lambda<Func<TSource, MappingContext, TDestination>>(
                ValueExpressions.BuildCollection(source, typeof(TDestination), registry, context), source, context),
            optimizations, registry);

        // Elements share one context, so an object reached from several of them is still one object.
        var reachesCycle = classification.NestedProfile is not null && registry.ReachesCycle(classification.NestedProfile);
        _contextKind = ContextKinds.Choose(tracksReferences: false, ParameterUsageVisitor.Uses(map, map.Parameters[1]),
            reachesCycle, optimizations);
        _compiledMap = new Lazy<Func<TSource, MappingContext, TDestination>>(
            () => RuntimeExpressionOptimizer.Hoist(map, optimizations).Compile());
    }

    // A null source collection is handled by the tree itself: an empty collection, or null (AllowNullCollections).
    public TDestination Map(TSource source) => _compiledMap.Value(source, NewContext(_contextKind));

    public TDestination Map(TSource source, MappingContext context) => _compiledMap.Value(source, context);

    public TDestination Map(TSource source, TDestination destination) => throw UpdateNotSupported();

    public TDestination Map(TSource source, TDestination destination, MappingContext context) =>
        throw UpdateNotSupported();

    public override TDestination MapInternal(object source) => Map((TSource)source);

    public override TDestination MapInternal(object source, TDestination destination) => throw UpdateNotSupported();

    private static NotSupportedException UpdateNotSupported() => new(
        $"Mapping onto an existing {TypeNames.Describe(typeof(TDestination))} is not supported yet; " +
        "map to a new collection with Map<TDestination>(source).");
}
