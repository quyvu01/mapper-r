using MapperR.Core.Abstractions;

namespace MapperR.Core.Implementations;

internal sealed class Mapper(IWireProfilesProvider wireProfilesProvider) : IMapper
{
    public TDestination Map<TDestination>(object source)
    {
        return default;
    }

    public TDestination Map<TSource, TDestination>(TSource source)
    {
        return default;
    }

    public TDestination Map<TDestination>(object source, TDestination destination)
    {
        return default;
    }

    public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
    {
        return default;
    }
}