using System.Linq.Expressions;
using MapperR.Core.Helpers;

namespace MapperR.Core.Implementations;

/// <summary>
/// Creates the empty collection a null source maps to, with the same shapes the expression builder materializes:
/// an array, a <c>HashSet&lt;T&gt;</c>, or a <c>List&lt;T&gt;</c> (which also serves <c>IEnumerable&lt;T&gt;</c>,
/// <c>ICollection&lt;T&gt;</c>, <c>IList&lt;T&gt;</c> and the read-only interfaces).
/// </summary>
internal static class EmptyCollection<TDestination>
{
    public static readonly Func<TDestination> Create = Build();

    private static Func<TDestination> Build()
    {
        var type = typeof(TDestination);
        var element = MemberClassifier.GetElementType(type)
                      ?? throw new InvalidOperationException($"{type.Name} is not a collection type.");

        Expression empty = type.IsArray
            ? Expression.Call(typeof(Array), nameof(Array.Empty), [element])
            : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>)
                ? Expression.New(type)
                : Expression.Convert(Expression.New(typeof(List<>).MakeGenericType(element)), type);

        return Expression.Lambda<Func<TDestination>>(empty).Compile();
    }
}
