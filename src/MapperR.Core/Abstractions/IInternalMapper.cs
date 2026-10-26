namespace MapperR.Core.Abstractions;

internal interface IInternalMapper<in TSource, TDestination>
{
    TDestination Map(TSource source);
    TDestination Map(TSource source, TDestination destination);
}