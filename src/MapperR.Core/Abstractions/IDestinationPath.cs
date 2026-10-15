using System.Linq.Expressions;

namespace MapperR.Core.Abstractions;

public interface IDestinationPath;

public interface IDestinationPath<TSource, TDestination> : IDestinationPath
{
    IDestinationPath<TSource, TDestination> MapFrom(Expression<Func<TSource, object>> path);
    IDestinationPath<TSource, TDestination> MapFrom(Expression<Func<TSource, TDestination, object>> path);
    IDestinationPath<TSource, TDestination> Ignore();
}