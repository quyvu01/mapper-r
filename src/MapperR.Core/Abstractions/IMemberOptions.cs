using System.Linq.Expressions;

namespace MapperR.Core.Abstractions;

public interface IMemberAction;

public interface IMemberOptions<TSource, TDestination> : IMemberAction
{
    IMemberOptions<TSource, TDestination> PreCondition(Expression<Func<TSource, bool>> predicate);
    IMemberOptions<TSource, TDestination> PreCondition(Expression<Func<TSource, TDestination, bool>> predicate);
    IMemberOptions<TSource, TDestination> MapFrom(Expression<Func<TSource, object>> path);
    IMemberOptions<TSource, TDestination> MapFrom(Expression<Func<TSource, TDestination, object>> path);
    IMemberOptions<TSource, TDestination> Ignore();
}