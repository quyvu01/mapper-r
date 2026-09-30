using MapperR.Core.Abstractions;

namespace MapperR.Core.Implementations;

/// <summary>
/// Collection mapping for the runtime engine's hoisted element delegates
/// (<see cref="MapperOptimizations.HoistCollectionLambdas"/>): same results as
/// <c>source.Select(...).ToList()/ToArray()/ToHashSet()</c>, without LINQ iterators or a closure per call.
/// </summary>
internal static class RuntimeCollections
{
    public static List<TDestination> ToList<TSource, TDestination>(IEnumerable<TSource> source,
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

    public static TDestination[] ToArray<TSource, TDestination>(IEnumerable<TSource> source,
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
                return ToList(source, element, context).ToArray();
        }
    }

    public static HashSet<TDestination> ToHashSet<TSource, TDestination>(IEnumerable<TSource> source,
        Func<TSource, MappingContext, TDestination> element, MappingContext context)
    {
        var result = new HashSet<TDestination>();
        foreach (var item in source) result.Add(element(item, context));
        return result;
    }
}
