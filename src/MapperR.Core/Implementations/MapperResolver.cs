using System.Collections.Concurrent;
using System.Reflection;
using MapperR.Core.Abstractions;
using MapperR.Core.Registries;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Core.Implementations;

/// <summary>Finds the mapper of a nested pair while mapping.</summary>
internal interface IMapperResolver
{
    IInternalMapper<TSource, TDestination> Get<TSource, TDestination>();

    /// <summary>The stateless context used by pairs that cannot reach a cycle.</summary>
    MappingContext SharedContext { get; }

    /// <summary>Whether a <c>null</c> source collection stays <c>null</c> instead of becoming an empty one.</summary>
    bool AllowNullCollections { get; }
}

/// <summary>
/// Resolves nested mappers from DI. Each closed pair gets one process-wide slot that records the resolver that
/// filled it, so the lookup is a field read, and several service providers in one process never share mappers
/// (a slot owned by another resolver is simply re-resolved). Safe to cache: internal mappers are singletons.
/// </summary>
internal sealed class MapperResolver(IServiceProvider services, MapperSettings settings) : IMapperResolver
{
    public bool AllowNullCollections { get; } = settings.AllowNullCollections;

    public MappingContext SharedContext => field ??= new MappingContext(this, shared: true);

    public IInternalMapper<TSource, TDestination> Get<TSource, TDestination>()
    {
        var entry = Slot<TSource, TDestination>.Entry;
        if (entry?.Owner == this) return entry.Mapper;

        var mapper = services.GetRequiredService<IInternalMapper<TSource, TDestination>>();
        Slot<TSource, TDestination>.Entry = new SlotEntry<TSource, TDestination>(this, mapper);
        return mapper;
    }

    private static class Slot<TSource, TDestination>
    {
        public static SlotEntry<TSource, TDestination> Entry;
    }

    private sealed class SlotEntry<TSource, TDestination>(
        IMapperResolver owner,
        IInternalMapper<TSource, TDestination> mapper)
    {
        public IMapperResolver Owner { get; } = owner;
        public IInternalMapper<TSource, TDestination> Mapper { get; } = mapper;
    }
}

/// <summary>Builds runtime mappers straight from a registry, without DI (tests and tooling).</summary>
internal sealed class RegistryMapperResolver(
    WireProfileRegistry registry,
    MapperOptimizations optimizations = MapperOptimizations.Default,
    bool allowNullCollections = false) : IMapperResolver
{
    public bool AllowNullCollections { get; } = allowNullCollections;

    public MappingContext SharedContext => field ??= new MappingContext(this, shared: true);

    private readonly ConcurrentDictionary<(Type Source, Type Destination), object> _mappers = new();

    public IInternalMapper<TSource, TDestination> Get<TSource, TDestination>() =>
        (IInternalMapper<TSource, TDestination>)_mappers.GetOrAdd((typeof(TSource), typeof(TDestination)),
            key => Activator.CreateInstance(
                typeof(InternalMapper<,>).MakeGenericType(key.Source, key.Destination),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, args: [registry, this, optimizations], culture: null)!);
}