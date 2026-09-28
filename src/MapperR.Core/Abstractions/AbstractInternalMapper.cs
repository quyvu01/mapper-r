namespace MapperR.Core.Abstractions;

/// <summary>
/// Type-erased bridge used when only the destination type is known at the call site
/// (<see cref="IMapper.Map{TDestination}(object)"/>). Every registered
/// <see cref="IInternalMapper{TSource,TDestination}"/> implementation, runtime-built or generated,
/// must also derive from this class.
/// </summary>
public abstract class AbstractInternalMapper<TDestination>
{
    public abstract TDestination MapInternal(object source);
    public abstract TDestination MapInternal(object source, TDestination destination);
}