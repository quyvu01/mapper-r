namespace MapperR.Core.Abstractions;

public interface IMapper
{
    /// <summary>
    /// Maps <paramref name="source"/> to <typeparamref name="TDestination"/> using the pair's profile. When both are
    /// collections (<c>Map&lt;Dst[]&gt;(list)</c>, <c>Map&lt;List&lt;Dst&gt;&gt;(array)</c>, ...) no profile is needed
    /// for the collection itself: it is built from the element pair. A null collection maps to an empty one, or to
    /// null when <c>AllowNullCollections</c> is on.
    /// </summary>
    TDestination Map<TDestination>(object source);

    /// <inheritdoc cref="Map{TDestination}(object)"/>
    TDestination Map<TSource, TDestination>(TSource source);

    /// <summary>
    /// Maps <paramref name="source"/> onto an existing <paramref name="destination"/>. Mapping onto an existing
    /// collection is not supported yet.
    /// </summary>
    TDestination Map<TDestination>(object source, TDestination destination);

    /// <inheritdoc cref="Map{TDestination}(object, TDestination)"/>
    TDestination Map<TSource, TDestination>(TSource source, TDestination destination);
}
