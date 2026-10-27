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
    private LambdaExpression _initializedProfile;
    private Func<TSource, TDestination, TDestination> _initialWithDestinationFunc;
    private Func<TSource, TDestination> _initialWithoutDestinationFunc;

    public TDestination Map(TSource source)
    {
        _initializedProfile ??= profileBuilder.GetProfile();
        if (_initializedProfile.Parameters.Count == 1)
        {
            _initialWithoutDestinationFunc = _initializedProfile.Compile() as Func<TSource, TDestination>;
            ArgumentNullException.ThrowIfNull(_initialWithoutDestinationFunc);
            return _initialWithoutDestinationFunc.Invoke(source);
        }

        if (_initializedProfile.Parameters.Count == 2)
        {
            _initialWithDestinationFunc = _initializedProfile.Compile() as Func<TSource, TDestination, TDestination>;
            ArgumentNullException.ThrowIfNull(_initialWithDestinationFunc);
            return _initialWithDestinationFunc.Invoke(source, default);
        }

        throw new NotSupportedException();
    }

    public TDestination Map(TSource source, TDestination destination)
    {
        throw MapperException.NotImplemented("Mapping onto an existing destination");
    }

    internal override TDestination MapInternal(object source) => Map((TSource)source);

    internal override TDestination MapInternal(object source, object destination) =>
        Map((TSource)source, (TDestination)destination);
}