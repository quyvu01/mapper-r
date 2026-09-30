using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using MapperR.Core.Abstractions;
using MapperR.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Core.Implementations;

internal class Mapper(IServiceProvider serviceProvider) : IMapper
{
    /// <summary>How a (source, destination) pair is mapped: through its profile, or as a collection (DESIGN #25).</summary>
    private sealed record Strategy(Type ProfileMapperType, object CollectionMapper);

    private readonly ConcurrentDictionary<(Type Source, Type Destination), Strategy> _strategies = [];

    // One collection mapper per (IEnumerable<element>, destination), shared by every collection type that fits.
    private readonly ConcurrentDictionary<(Type Source, Type Destination), object> _collectionMappers = [];

    public TDestination Map<TDestination>(object source)
    {
        if (source is null)
        {
            // The source type is unknown, but a collection destination still has a defined answer (DESIGN #26).
            if (MemberClassifier.IsSupportedCollectionDestination(typeof(TDestination)))
                return serviceProvider.GetRequiredService<MapperSettings>().AllowNullCollections
                    ? default
                    : EmptyCollection<TDestination>.Create();

            ArgumentNullException.ThrowIfNull(source);
        }

        return AbstractMapperFor<TDestination>(source.GetType()).MapInternal(source);
    }

    public TDestination Map<TSource, TDestination>(TSource source) =>
        InternalMapperFor<TSource, TDestination>().Map(source);

    public TDestination Map<TDestination>(object source, TDestination destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        return AbstractMapperFor<TDestination>(source.GetType()).MapInternal(source, destination);
    }

    public TDestination Map<TSource, TDestination>(TSource source, TDestination destination) =>
        InternalMapperFor<TSource, TDestination>().Map(source, destination);

    private AbstractInternalMapper<TDestination> AbstractMapperFor<TDestination>(Type sourceType)
    {
        var strategy = StrategyFor(sourceType, typeof(TDestination));
        return (AbstractInternalMapper<TDestination>)(strategy.CollectionMapper
                                                      ?? serviceProvider.GetRequiredService(strategy.ProfileMapperType));
    }

    private IInternalMapper<TSource, TDestination> InternalMapperFor<TSource, TDestination>()
    {
        // The usual pair has no collection on either side: straight to DI, as before collection maps existed.
        if (!CollectionPair<TSource, TDestination>.MayBeCollectionMap)
            return serviceProvider.GetRequiredService<IInternalMapper<TSource, TDestination>>();

        var strategy = StrategyFor(typeof(TSource), typeof(TDestination));
        // CollectionMapper<IEnumerable<T>, D> is an IInternalMapper<List<T>, D> too: the source parameter is contravariant.
        return strategy.CollectionMapper is null
            ? serviceProvider.GetRequiredService<IInternalMapper<TSource, TDestination>>()
            : (IInternalMapper<TSource, TDestination>)strategy.CollectionMapper;
    }

    private Strategy StrategyFor(Type source, Type destination) =>
        _strategies.GetOrAdd((source, destination), static (key, mapper) => mapper.Resolve(key.Source, key.Destination),
            this);

    private Strategy Resolve(Type source, Type destination)
    {
        var profileMapperType = typeof(IInternalMapper<,>).MakeGenericType(source, destination);
        var registry = serviceProvider.GetRequiredService<RegistryProvider>().ProfileRegistry;
        if (registry.Find(source, destination) is not null) return new Strategy(profileMapperType, null);

        var (sourceElement, destinationElement) =
            (MemberClassifier.GetElementType(source), MemberClassifier.GetElementType(destination));
        // An ordinary pair without a profile: DI reports it, as it always did.
        if (sourceElement is null && destinationElement is null) return new Strategy(profileMapperType, null);
        if (sourceElement is null || destinationElement is null)
            throw new InvalidOperationException(
                $"Cannot map {TypeNames.Describe(source)} to {TypeNames.Describe(destination)}: a collection cannot " +
                "be mapped to a single object or the other way round.");

        var normalizedSource = typeof(IEnumerable<>).MakeGenericType(sourceElement);
        var collectionMapper = _collectionMappers.GetOrAdd((normalizedSource, destination),
            static (key, state) => CreateCollectionMapper(key.Source, key.Destination, state.Registry,
                state.ServiceProvider), (Registry: registry, ServiceProvider: serviceProvider));
        return new Strategy(profileMapperType, collectionMapper);
    }

    private static object CreateCollectionMapper(Type source, Type destination, Registries.WireProfileRegistry registry,
        IServiceProvider serviceProvider)
    {
        try
        {
            return Activator.CreateInstance(
                typeof(CollectionMapper<,>).MakeGenericType(source, destination),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null,
                args:
                [
                    registry, serviceProvider.GetRequiredService<IMapperResolver>(),
                    serviceProvider.GetRequiredService<MapperOptimizations>()
                ],
                culture: null)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            // The constructor reports what cannot be mapped (an unsupported destination, elements without a
            // profile); the caller should see that error, not the reflection wrapper around it.
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Whether both types are collections, worked out once per closed pair.</summary>
    private static class CollectionPair<TSource, TDestination>
    {
        public static readonly bool MayBeCollectionMap =
            MemberClassifier.GetElementType(typeof(TSource)) is not null ||
            MemberClassifier.GetElementType(typeof(TDestination)) is not null;
    }
}
