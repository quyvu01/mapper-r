using System.Linq.Expressions;
using MapperR.Core.Abstractions;

namespace MapperR.Core.Implementations;

internal sealed class DestinationPath<TSource, TDestination> : IDestinationPath<TSource, TDestination>
{
    internal bool IsIgnore { get; private set; }
    internal LambdaExpression Path { get; private set; }


    public IDestinationPath<TSource, TDestination> MapFrom(Expression<Func<TSource, object>> path)
    {
        Path = path;
        return this;
    }

    public IDestinationPath<TSource, TDestination> MapFrom(Expression<Func<TSource, TDestination, object>> path)
    {
        Path = path;
        return this;
    }

    public IDestinationPath<TSource, TDestination> Ignore()
    {
        IsIgnore = true;
        return this;
    }
}