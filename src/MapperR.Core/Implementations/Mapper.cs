using System.Collections.Concurrent;
using MapperR.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Core.Implementations;

internal class Mapper(IServiceProvider serviceProvider) : IMapper
{
    private readonly ConcurrentDictionary<(Type SourceType, Type DestinationType), Type> _internalMappers = [];

    public TDestination Map<TDestination>(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var internalMapperType = _internalMappers.GetOrAdd((source.GetType(), typeof(TDestination)),
            static x => typeof(IInternalMapper<,>).MakeGenericType(x.SourceType, x.DestinationType));
        var internalMapper = (AbstractInternalMapper<TDestination>)serviceProvider
            .GetRequiredService(internalMapperType);
        var result = internalMapper.MapInternal(source);
        return result;
    }

    public TDestination Map<TSource, TDestination>(TSource source)
    {
        var internalMapper = serviceProvider.GetRequiredService<IInternalMapper<TSource, TDestination>>();
        return internalMapper.Map(source);
    }
}