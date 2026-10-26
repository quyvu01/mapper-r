using System.Linq.Expressions;
using MapperR.Core.Abstractions;
using MapperR.Core.Exceptions;

namespace MapperR.Core.Implementations;

internal abstract class AbstractInternalMapper<TDestination>
{
    internal abstract TDestination MapInternal(object source);
    internal abstract TDestination MapInternal(object source, object destination);
}

internal sealed class InternalMapper<TSource, TDestination>(IProfileBuilder<TSource, TDestination> profileBuilder)
    : AbstractInternalMapper<TDestination>, IInternalMapper<TSource, TDestination>
{
    private Expression<Func<TSource, TDestination, TDestination>> _initializedProfile;
    private Func<TSource, TDestination, TDestination> _initialFunc;

    public TDestination Map(TSource source)
    {
        _initializedProfile ??= profileBuilder.GetProfile();
        _initialFunc ??= _initializedProfile.Compile();
        return _initialFunc.Invoke(source, default);
    }

    public TDestination Map(TSource source, TDestination destination)
    {
        throw MapperException.NotImplemented("Mapping onto an existing destination");
    }

    internal override TDestination MapInternal(object source) => Map((TSource)source);

    internal override TDestination MapInternal(object source, object destination) =>
        Map((TSource)source, (TDestination)destination);
}