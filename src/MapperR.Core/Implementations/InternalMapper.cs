using MapperR.Core.Abstractions;

namespace MapperR.Core.Implementations;

internal abstract class AbstractInternalMapper<TDestination>
{
    internal abstract TDestination MapInternal(object source);
}

internal class InternalMapper<TSource, TDestination> : AbstractInternalMapper<TDestination>,
    IInternalMapper<TSource, TDestination>
    where TDestination : new()
{
    private readonly Lazy<Func<TSource, TDestination>> _compiledMap;

    public InternalMapper(RegistryProvider registryProvider)
    {
        var wireProfileRegistry = registryProvider.ProfileRegistry;
        var expression = TypeMapExpressionBuilder<TSource, TDestination>.Build(wireProfileRegistry);
        _compiledMap = new Lazy<Func<TSource, TDestination>>(expression.Compile);
    }

    public TDestination Map(TSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _compiledMap.Value.Invoke(source);
    }

    internal override TDestination MapInternal(object source)
    {
        var result = Map((TSource)source);
        return result;
    }
}