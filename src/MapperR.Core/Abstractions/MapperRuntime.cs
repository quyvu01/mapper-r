namespace MapperR.Core.Abstractions;

/// <summary>
/// The calls the expression builder emits for nested members. At runtime they defer to the child pair's mapper
/// through the <see cref="MappingContext"/>; <c>maprgen</c> recognizes them and prints a direct call to the
/// child's generated mapper instead (or keeps this call when the child pair was not generated).
/// </summary>
public static class MapperRuntime
{
    public static TDestination MapNested<TSource, TDestination>(TSource source, MappingContext context) =>
        context.Map<TSource, TDestination>(source);

    public static TDestination MapNestedInto<TSource, TDestination>(TSource source, TDestination destination,
        MappingContext context) =>
        context.MapInto(source, destination);
}
