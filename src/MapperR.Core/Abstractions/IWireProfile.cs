using System.Linq.Expressions;

namespace MapperR.Core.Abstractions;

public interface IWireProfile
{
    Type SourceType { get; }
    Type DestinationType { get; }
    IMemberProfile[] MemberProfiles { get; }
}

public interface IWireProfile<TSource, TDestination> : IWireProfile
{
    IWireProfile<TSource, TDestination> ForMember<TProp>(Expression<Func<TDestination, TProp>> selector,
        Expression<Func<TSource, object>> path);

    IWireProfile<TSource, TDestination> Ignore<TProp>(Expression<Func<TDestination, TProp>> selector);
}