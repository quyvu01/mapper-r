namespace MapperR.Core.Abstractions;

public interface IInternalMapper<in TSource, TDestination>
{
    TDestination Map(TSource source);
    TDestination Map(TSource source, TDestination destination);
}