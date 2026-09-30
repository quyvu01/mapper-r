using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using MapperR.Core.Abstractions;
using MapperR.Core.Entities;
using MapperR.Core.Helpers;
using MapperR.Core.Registries;

namespace MapperR.Core.Implementations;

/// <summary>
/// The expressions that produce one value of an already-classified kind (a member, or an element of a collection)
/// and a whole collection. Shared by the pair builder (<see cref="TypeMapExpressionBuilder{TSource,TDestination}"/>)
/// and the top-level collection mapper, so a collection member and <c>Map&lt;Dst[]&gt;(source)</c> always behave the same.
/// </summary>
internal static class ValueExpressions
{
    private static readonly MethodInfo MapNestedMethod = typeof(MapperRuntime).GetMethod(nameof(MapperRuntime.MapNested))!;

    private static readonly MethodInfo NullCollectionMethod =
        typeof(MapperRuntime).GetMethod(nameof(MapperRuntime.NullCollection))!;

    /// <summary>Emits a single value of an already-classified kind (a member, or an element of a collection).</summary>
    internal static Expression EmitValue(MapClassify kind, Expression value, Type destinationType,
        ParameterExpression context) => kind switch
    {
        MapClassify.Nested => Expression.Call(MapNestedMethod.MakeGenericMethod(value.Type, destinationType),
            value, context),
        MapClassify.ToText => ToText(value),
        // TODO(DESIGN #20): EnumByName should map by name with a value fallback; converts by value for now.
        MapClassify.Direct or MapClassify.Numeric or MapClassify.EnumByName => ConvertValue(value, destinationType),
        _ => throw new InvalidOperationException($"A member classified as {kind} cannot be emitted.")
    };

    /// <summary>
    /// Builds <c>value == null ? &lt;empty or null&gt; : value.Select(item => &lt;element&gt;).ToList()/.ToArray()/...</c>. Nested
    /// elements are mapped through <see cref="MapperRuntime.MapNested{TSource,TDestination}"/>, which maps a null
    /// element to null.
    /// </summary>
    internal static Expression BuildCollection(Expression value, Type destinationType,
        WireProfileRegistry registry, ParameterExpression context)
    {
        var sourceElementType = MemberClassifier.GetElementType(value.Type)!;
        var destinationElementType = MemberClassifier.GetElementType(destinationType)!;
        var elementKind = MemberClassifier.Classify(sourceElementType, destinationElementType, registry.Find).Kind;

        var item = Expression.Parameter(sourceElementType, "item");
        var elementLambda =
            Expression.Lambda(EmitValue(elementKind, item, destinationElementType, context), item);

        var selectCall = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select),
            [sourceElementType, destinationElementType], value, elementLambda);

        // A null source collection becomes an empty destination collection, or null when AllowNullCollections is
        // on; the context carries that setting, so the same tree serves both and generated code needs no flag.
        return Expression.Condition(
            Expression.Equal(value, Expression.Constant(null, value.Type)),
            Expression.Call(NullCollectionMethod.MakeGenericMethod(destinationType), context),
            MaterializeCollection(selectCall, destinationType, destinationElementType));
    }

    /// <summary>
    /// Converts <paramref name="value"/> to <paramref name="targetType"/>. A null <c>Nullable&lt;T&gt;</c> going to a
    /// non-nullable value type becomes <c>default</c> (<c>value ?? default(T)</c>) instead of throwing
    /// "Nullable object must have a value" — the same result AutoMapper gives. <c>??</c> translates to SQL
    /// <c>COALESCE</c>, so this stays usable for projections.
    /// </summary>
    private static Expression ConvertValue(Expression value, Type targetType)
    {
        if (value.Type == targetType) return value;

        if (Nullable.GetUnderlyingType(value.Type) is { } underlying
            && targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null)
            value = Expression.Coalesce(value, Expression.Default(underlying));

        return value.Type == targetType ? value : Expression.Convert(value, targetType);
    }

    /// <summary>
    /// Scalar → <c>string</c> (DESIGN #17): the type's own non-obsolete <c>ToString(string, IFormatProvider)</c>
    /// with the invariant culture when it has one, otherwise <c>ToString()</c> (<c>bool</c>, <c>char</c>, enums —
    /// the enum provider overload is <c>[Obsolete]</c>). A null <c>Nullable&lt;T&gt;</c> gives <c>null</c>, not the
    /// <c>""</c> that <c>Nullable&lt;T&gt;.ToString()</c> would return.
    /// </summary>
    private static Expression ToText(Expression value)
    {
        if (Nullable.GetUnderlyingType(value.Type) is not null)
            return Expression.Condition(
                Expression.Property(value, nameof(Nullable<int>.HasValue)),
                ToText(Expression.Property(value, nameof(Nullable<int>.Value))),
                Expression.Constant(null, typeof(string)));

        var formatted = value.Type.GetMethod(nameof(ToString), [typeof(string), typeof(IFormatProvider)]);
        return formatted is not null && formatted.GetCustomAttribute<ObsoleteAttribute>() is null
            ? Expression.Call(value, formatted, Expression.Constant(null, typeof(string)),
                Expression.Property(null, typeof(CultureInfo), nameof(CultureInfo.InvariantCulture)))
            : Expression.Call(value, value.Type.GetMethod(nameof(ToString), Type.EmptyTypes)!);
    }

    private static Expression MaterializeCollection(Expression selectCall, Type destinationType,
        Type destinationElementType)
    {
        MethodInfo materializeMethod;

        if (destinationType.IsArray)
        {
            materializeMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray))!
                .MakeGenericMethod(destinationElementType);
        }
        else if (destinationType.IsGenericType && destinationType.GetGenericTypeDefinition() == typeof(HashSet<>))
        {
            materializeMethod = typeof(Enumerable).GetMethods()
                .First(m => m.Name == nameof(Enumerable.ToHashSet) && m.GetParameters().Length == 1)
                .MakeGenericMethod(destinationElementType);
        }
        else
        {
            materializeMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.ToList))!
                .MakeGenericMethod(destinationElementType);
        }

        var materialized = Expression.Call(materializeMethod, selectCall);
        return Expression.Convert(materialized, destinationType);
    }
}
