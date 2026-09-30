using System.Linq.Expressions;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;

namespace MapperR.Core.Entities;

internal sealed record MemberProfile<TSource, TDestination, TProp>(
    string MemberName,
    Expression<Func<TDestination, TProp>> Selector,
    Expression<Func<TSource, object>> Path,
    MapClassify Classify,
    bool IsExplicit) : IMemberProfile
{
    public Type DestinationMemberType => typeof(TProp);

    public Type SourceMemberType => Path.GetSourceType();

    LambdaExpression IMemberProfile.Selector => Selector;
    LambdaExpression IMemberProfile.Path => Path;
    MapClassify IMemberProfile.Classify => Classify;
}