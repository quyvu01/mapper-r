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

    // Collection helpers: the runtime engine and generated code map a collection through one of these instead of
    // Select(...).ToList() with a lambda, which would allocate an iterator and a closure on every call. They give
    // the same result as the LINQ form. The element mapper takes the context as a parameter, so a lambda passed
    // by generated code captures nothing and is cached by the C# compiler.

    public static List<TDestination> MapToList<TSource, TDestination>(IEnumerable<TSource> source,
        Func<TSource, MappingContext, TDestination> element, MappingContext context)
    {
        switch (source)
        {
            case List<TSource> list:
            {
                var result = new List<TDestination>(list.Count);
                for (var i = 0; i < list.Count; i++) result.Add(element(list[i], context));
                return result;
            }
            case TSource[] array:
            {
                var result = new List<TDestination>(array.Length);
                for (var i = 0; i < array.Length; i++) result.Add(element(array[i], context));
                return result;
            }
            default:
            {
                var result = source is ICollection<TSource> collection
                    ? new List<TDestination>(collection.Count)
                    : [];
                foreach (var item in source) result.Add(element(item, context));
                return result;
            }
        }
    }

    public static TDestination[] MapToArray<TSource, TDestination>(IEnumerable<TSource> source,
        Func<TSource, MappingContext, TDestination> element, MappingContext context)
    {
        switch (source)
        {
            case List<TSource> list:
            {
                var result = new TDestination[list.Count];
                for (var i = 0; i < result.Length; i++) result[i] = element(list[i], context);
                return result;
            }
            case TSource[] array:
            {
                var result = new TDestination[array.Length];
                for (var i = 0; i < result.Length; i++) result[i] = element(array[i], context);
                return result;
            }
            default:
                return MapToList(source, element, context).ToArray();
        }
    }

    public static HashSet<TDestination> MapToHashSet<TSource, TDestination>(IEnumerable<TSource> source,
        Func<TSource, MappingContext, TDestination> element, MappingContext context)
    {
        var result = new HashSet<TDestination>();
        foreach (var item in source) result.Add(element(item, context));
        return result;
    }
}
