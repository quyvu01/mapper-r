using System.Collections.Concurrent;
using MapperR.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Core.Implementations;

internal sealed class Mapper(IServiceProvider serviceProvider) : IMapper
{
    private readonly ConcurrentDictionary<(Type SourceType, Type DestinationType), Type> _servicesLookup = [];

    public TDestination Map<TDestination>(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var serviceType = _servicesLookup.GetOrAdd((source.GetType(), typeof(TDestination)),
            static x => typeof(IInternalMapper<,>).MakeGenericType(x.SourceType, x.DestinationType));
        var internalMapService = (AbstractInternalMapper<TDestination>)serviceProvider.GetRequiredService(serviceType);
        return internalMapService.MapInternal(source);
    }

    public TDestination Map<TSource, TDestination>(TSource source) =>
        serviceProvider.GetRequiredService<IInternalMapper<TSource, TDestination>>().Map(source);

    public TDestination Map<TDestination>(object source, TDestination destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        var serviceType = _servicesLookup.GetOrAdd((source.GetType(), typeof(TDestination)),
            static x => typeof(IInternalMapper<,>).MakeGenericType(x.SourceType, x.DestinationType));
        var internalMapService = (AbstractInternalMapper<TDestination>)serviceProvider.GetRequiredService(serviceType);
        return internalMapService.MapInternal(source, destination);
    }

    public TDestination Map<TSource, TDestination>(TSource source, TDestination destination) =>
        serviceProvider.GetRequiredService<IInternalMapper<TSource, TDestination>>().Map(source, destination);
}