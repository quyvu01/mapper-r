using MapperR.Core.Abstractions;

namespace MapperR.Core.Implementations;

internal class InternalMapper<TSource, TDestination> : AbstractInternalMapper<TDestination>,
    IInternalMapper<TSource, TDestination>
    where TDestination : new()
{
    private readonly Lazy<Func<TSource, TDestination>> _compiledMap;
    private readonly Lazy<Func<TSource, TDestination, TDestination>> _mapUpdate;

    public InternalMapper(RegistryProvider registryProvider)
    {
        var wireProfileRegistry = registryProvider.ProfileRegistry;
        var expression = TypeMapExpressionBuilder<TSource, TDestination>.Build(wireProfileRegistry);
        _compiledMap = new Lazy<Func<TSource, TDestination>>(expression.Compile);
        _mapUpdate = new Lazy<Func<TSource, TDestination, TDestination>>(
            () => TypeMapExpressionBuilder<TSource, TDestination>.BuildUpdate(wireProfileRegistry).Compile());
    }

    public TDestination Map(TSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _compiledMap.Value.Invoke(source);
    }

    public TDestination Map(TSource source, TDestination destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        return _mapUpdate.Value.Invoke(source, destination);
    }

    public override TDestination MapInternal(object source)
    {
        var result = Map((TSource)source);
        return result;
    }

    public override TDestination MapInternal(object source, TDestination destination)
    {
        var result = Map((TSource)source, destination);
        return result;
    }
}
