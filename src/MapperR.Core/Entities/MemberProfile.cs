using System.Linq.Expressions;
using MapperR.Core.Abstractions;

namespace MapperR.Core.Entities;

internal sealed record MemberProfile<TSource, TDestination, TProp>(
    string MemberName,
    Expression<Func<TDestination, TProp>> Selector,
    Expression<Func<TSource, object>> Path) : IMemberProfile
{
    public Type DestinationMemberType => typeof(TProp);

    public Type SourceMemberType => Path.Body is UnaryExpression
    {
        NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
    } unary
        ? unary.Operand.Type
        : Path.Body.Type;

    LambdaExpression IMemberProfile.Selector => Selector;
    LambdaExpression IMemberProfile.Path => Path;
}