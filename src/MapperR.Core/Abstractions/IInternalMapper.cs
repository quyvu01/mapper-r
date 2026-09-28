namespace MapperR.Core.Abstractions;

public interface IInternalMapper<in TSource, out TDestination>
{
    TDestination Map(TSource source);
}
