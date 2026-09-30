namespace MapperR.Core.Abstractions;

public interface IInternalMapper<in TSource, TDestination>
{
    TDestination Map(TSource source);
    TDestination Map(TSource source, TDestination destination);

    /// <summary>Maps as part of an ongoing map call (a nested member), sharing its <paramref name="context"/>.</summary>
    TDestination Map(TSource source, MappingContext context);

    /// <summary>Updates as part of an ongoing map call (a nested member), sharing its <paramref name="context"/>.</summary>
    TDestination Map(TSource source, TDestination destination, MappingContext context);
}
