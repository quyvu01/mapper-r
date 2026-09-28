using MapperR.Core.Abstractions;

namespace MapperR.Core.Implementations;

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

    public override TDestination MapInternal(object source)
    {
        var result = Map((TSource)source);
        return result;
    }
}