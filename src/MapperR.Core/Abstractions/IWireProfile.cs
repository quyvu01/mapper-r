using System.Linq.Expressions;

namespace MapperR.Core.Abstractions;

public interface IWireProfile
{
    ParameterExpression SourceParam { get; }
    ParameterExpression DestinationParam { get; }
    Type SourceType { get; }
    Type DestinationType { get; }
    IMemberProfile[] MemberProfiles { get; }
}

public interface IWireProfile<TSource, TDestination> : IWireProfile
{
    IWireProfile<TSource, TDestination> ForMember<TProp>(Expression<Func<TDestination, TProp>> selector,
        Action<IMemberOptions<TSource, TDestination>> options);
}