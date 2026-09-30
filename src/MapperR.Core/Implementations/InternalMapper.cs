using MapperR.Core.Abstractions;
using MapperR.Core.Registries;

namespace MapperR.Core.Implementations;

internal class InternalMapper<TSource, TDestination> : AbstractInternalMapper<TDestination>,
    IInternalMapper<TSource, TDestination>
    where TDestination : new()
{
    private enum ContextKind
    {
        /// <summary>The trees never touch the context.</summary>
        None,

        /// <summary>Nested members only: nothing below can reach a cycle, so one stateless context serves every call.</summary>
        Shared,

        /// <summary>Depth guard and reference tracking: one context per top-level call.</summary>
        PerCall
    }

    private readonly Lazy<Func<TSource, MappingContext, TDestination>> _compiledMap;
    private readonly Lazy<Func<TSource, TDestination, MappingContext, TDestination>> _mapUpdate;
    private readonly bool _tracksReferences;
    private readonly ContextKind _contextKind;

    // Used by DI (open generic registration).
    public InternalMapper(RegistryProvider registryProvider, IMapperResolver resolver,
        MapperOptimizations optimizations) : this(registryProvider.ProfileRegistry, resolver, optimizations)
    {
    }

    internal InternalMapper(WireProfileRegistry registry, IMapperResolver resolver,
        MapperOptimizations optimizations = MapperOptimizations.Default) : base(resolver)
    {
        // Both trees are built eagerly so configuration errors surface when the mapper is resolved; compiling
        // stays lazy.
        var profile = registry.Find(typeof(TSource), typeof(TDestination));
        var map = RuntimeExpressionOptimizer.Inline(
            TypeMapExpressionBuilder<TSource, TDestination>.Build(registry), optimizations, registry);
        var update = RuntimeExpressionOptimizer.Inline(
            TypeMapExpressionBuilder<TSource, TDestination>.BuildUpdate(registry), optimizations, registry);

        _tracksReferences = registry.TracksReferences(profile);
        var usesContext = ParameterUsageVisitor.Uses(map, map.Parameters[1]) ||
                          ParameterUsageVisitor.Uses(update, update.Parameters[2]);
        _contextKind = _tracksReferences || (usesContext && registry.ReachesCycle(profile)) ? ContextKind.PerCall
            : !usesContext ? ContextKind.None
            : optimizations.HasFlag(MapperOptimizations.SharedContextWhenAcyclic) ? ContextKind.Shared
            : ContextKind.PerCall;

        _compiledMap = new Lazy<Func<TSource, MappingContext, TDestination>>(() =>
            RuntimeExpressionOptimizer.Hoist(map, optimizations).Compile());
        _mapUpdate = new Lazy<Func<TSource, TDestination, MappingContext, TDestination>>(() =>
            RuntimeExpressionOptimizer.Hoist(update, optimizations).Compile());
    }

    private MappingContext NewContext() => _contextKind switch
    {
        ContextKind.None => null,
        ContextKind.Shared => SharedContext,
        _ => CreateContext()
    };

    public TDestination Map(TSource source) => Map(source, NewContext());

    public TDestination Map(TSource source, TDestination destination) => Map(source, destination, NewContext());

    public TDestination Map(TSource source, MappingContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_tracksReferences) return _compiledMap.Value(source, context);

        // Register the new object before mapping its members, so a cycle in the data that leads back to this
        // source reuses it instead of recursing.
        if (context.TryGetVisited(source, out TDestination existing)) return existing;
        return _mapUpdate.Value(source, context.Register(source, new TDestination()), context);
    }

    public TDestination Map(TSource source, TDestination destination, MappingContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (destination is null) return Map(source, context);

        if (!_tracksReferences) return _mapUpdate.Value(source, destination, context);

        if (context.TryGetVisited(source, out TDestination existing)) return existing;
        return _mapUpdate.Value(source, context.Register(source, destination), context);
    }

    public override TDestination MapInternal(object source)
    {
        var result = Map((TSource)source);
        return result;
    }

    public override TDestination MapInternal(object source, TDestination destination)
    {
        var result = Map((TSource)source, destination);
        return result;
    }
}