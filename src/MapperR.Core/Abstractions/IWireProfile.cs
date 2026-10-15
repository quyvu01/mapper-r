using System.Linq.Expressions;

namespace MapperR.Core.Abstractions;

public interface IWireProfile
{
    Type SourceType { get; }
    Type DestinationType { get; }
    IDestinationPath[] DestinationPaths { get; }
}

public interface IWireProfile<TSource, TDestination> : IWireProfile
{
    IWireProfile<TSource, TDestination> ForMember<TProp>(Expression<Func<TDestination, TProp>> selector,
        Action<IDestinationPath<TSource, TDestination>> options);
}