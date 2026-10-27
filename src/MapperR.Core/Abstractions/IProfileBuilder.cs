using System.Linq.Expressions;

namespace MapperR.Core.Abstractions;

internal interface IProfileBuilder<TSource, TDestination>
{
    LambdaExpression GetProfile();
}