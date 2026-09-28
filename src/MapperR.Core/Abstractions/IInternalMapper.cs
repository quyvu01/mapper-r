namespace MapperR.Core.Abstractions;

internal interface IInternalMapper<in TSource, out TDestination>
{
    TDestination Map(TSource source);
}