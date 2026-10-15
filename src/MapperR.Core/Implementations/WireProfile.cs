using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;

namespace MapperR.Core.Implementations;

internal class WireProfile<TSource, TDestination> : IWireProfile<TSource, TDestination>
{
    public Type SourceType => typeof(TSource);
    public Type DestinationType => typeof(TDestination);
    private readonly Dictionary<string, IDestinationPath> _destinationPaths = [];
    public IDestinationPath[] DestinationPaths => [.. _destinationPaths.Select(x => x.Value)];

    public IWireProfile<TSource, TDestination> ForMember<TProp>(Expression<Func<TDestination, TProp>> selector,
        [NotNull] Action<IDestinationPath<TSource, TDestination>> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var path = new DestinationPath<TSource, TDestination>();
        options.Invoke(path);
        if (path.IsIgnore) return this;
        var selectorName = selector.GetMemberName();
        _destinationPaths[selectorName] = path;
        return this;
    }
}