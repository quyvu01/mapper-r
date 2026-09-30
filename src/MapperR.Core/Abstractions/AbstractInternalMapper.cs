using MapperR.Core.Implementations;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Core.Abstractions;

/// <summary>
/// Type-erased bridge used when only the destination type is known at the call site
/// (<see cref="IMapper.Map{TDestination}(object)"/>). Every registered
/// <see cref="IInternalMapper{TSource,TDestination}"/> implementation, runtime-built or generated,
/// must also derive from this class.
/// </summary>
public abstract class AbstractInternalMapper<TDestination>
{
    private readonly IMapperResolver _resolver;

    /// <summary>For generated mappers: nested pairs are resolved through the application's service provider.</summary>
    protected AbstractInternalMapper(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _resolver = services.GetRequiredService<MapperResolver>();
    }

    private protected AbstractInternalMapper(IMapperResolver resolver) => _resolver = resolver;

    /// <summary>A fresh context for one top-level map call.</summary>
    protected MappingContext CreateContext() => new(_resolver);

    public abstract TDestination MapInternal(object source);
    public abstract TDestination MapInternal(object source, TDestination destination);
}
