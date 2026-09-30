using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using MapperR.Core.Abstractions;
using MapperR.Core.Entities;
using MapperR.Core.Helpers;
using MapperR.Core.Registries;

namespace MapperR.Core.Implementations;

/// <summary>
/// Composes the expression trees for one type pair — compiled for the runtime engine, and printed as C# by
/// <c>maprgen</c>, so both always agree. Every member's handling comes from <see cref="MemberClassifier"/>.
/// Nested pairs are not inlined: they become calls to <see cref="MapperRuntime"/>, resolved at runtime through
/// the <see cref="MappingContext"/> parameter, so building a pair never builds its children and cycles between
/// types cannot make building recurse.
/// </summary>
internal static class TypeMapExpressionBuilder<TSource, TDestination>
    where TDestination : new()
{
    private static readonly MethodInfo MapNestedMethod = typeof(MapperRuntime).GetMethod(nameof(MapperRuntime.MapNested))!;

    private static readonly MethodInfo MapNestedIntoMethod =
        typeof(MapperRuntime).GetMethod(nameof(MapperRuntime.MapNestedInto))!;

    private static readonly MethodInfo NullCollectionMethod =
        typeof(MapperRuntime).GetMethod(nameof(MapperRuntime.NullCollection))!;

    public static Expression<Func<TSource, MappingContext, TDestination>> Build(WireProfileRegistry registry)
    {
        var profile = FindProfile(registry);
        var source = Expression.Parameter(typeof(TSource), "source");
        var context = Expression.Parameter(typeof(MappingContext), "context");

        MemberBinding[] bindings =
        [
            .. Resolve(profile, registry).Select(member => Expression.Bind(
                GetDestinationMember(member.Profile.Selector),
                GetValueExpression(member, registry, source, context)))
        ];

        var body = Expression.MemberInit(Expression.New(typeof(TDestination)), bindings);
        return Expression.Lambda<Func<TSource, MappingContext, TDestination>>(body, source, context);
    }

    /// <summary>
    /// The "map onto an existing instance" form: each mapped member is assigned the same value expression
    /// <see cref="Build"/> uses, except nested objects, which are updated in place through
    /// <see cref="MapperRuntime.MapNestedInto{TSource,TDestination}"/> (their reference is kept, as AutoMapper
    /// does). Collections are replaced. Members without a mapping keep their current value. Returns the
    /// destination so value-type destinations work too.
    /// </summary>
    public static Expression<Func<TSource, TDestination, MappingContext, TDestination>> BuildUpdate(
        WireProfileRegistry registry)
    {
        var profile = FindProfile(registry);
        var source = Expression.Parameter(typeof(TSource), "source");
        var destination = Expression.Parameter(typeof(TDestination), "destination");
        var context = Expression.Parameter(typeof(MappingContext), "context");

        Expression[] statements =
        [
            .. Resolve(profile, registry).Select(member =>
                BuildMemberUpdate(member, registry, source, destination, context))
        ];

        var body = Expression.Block(
            statements.Length == 0 ? Expression.Empty() : Expression.Block(typeof(void), statements),
            destination);
        return Expression.Lambda<Func<TSource, TDestination, MappingContext, TDestination>>(body, source,
            destination, context);
    }

    private readonly record struct ResolvedMember(IMemberProfile Profile, MemberClassifier.Result Resolution);

    /// <summary>
    /// Classifies every member against the registry. An explicit (<c>ForMember</c>) member that cannot be mapped
    /// is a configuration error; a convention candidate that cannot be mapped (e.g. <c>Address</c> →
    /// <c>AddressDto</c> without a <c>CreateMap</c>) is skipped.
    /// </summary>
    private static IEnumerable<ResolvedMember> Resolve(IWireProfile profile, WireProfileRegistry registry)
    {
        foreach (var member in profile.MemberProfiles)
        {
            var resolution =
                MemberClassifier.Classify(member.SourceMemberType, member.DestinationMemberType, registry.Find);
            if (resolution.Kind != MapClassify.Invalid)
            {
                yield return new ResolvedMember(member, resolution);
                continue;
            }

            if (member.IsExplicit)
                throw new InvalidOperationException(
                    $"{typeof(TSource).Name} -> {typeof(TDestination).Name}: member '{member.MemberName}' " +
                    $"cannot be mapped: {resolution.Reason}.");
        }
    }

    private static Expression BuildMemberUpdate(ResolvedMember member, WireProfileRegistry registry,
        Expression source, Expression destination, ParameterExpression context)
    {
        var destinationMember = GetDestinationMember(member.Profile.Selector);
        var target = Expression.MakeMemberAccess(destination, destinationMember);

        if (member.Resolution.Kind != MapClassify.Nested ||
            !CanUpdateInPlace(destinationMember, member.Profile.DestinationMemberType))
            return Expression.Assign(target, GetValueExpression(member, registry, source, context));

        // target = MapNestedInto(value, target, context): updates the existing object (reference kept), creates
        // it when target is null, clears it when value is null.
        var value = UnwrapAndRebind(member.Profile.Path, source);
        return Expression.Assign(target, Expression.Call(
            MapNestedIntoMethod.MakeGenericMethod(value.Type, member.Profile.DestinationMemberType),
            value, target, context));
    }

    /// <summary>
    /// A value-type member can't be updated in place (reading it yields a copy), and a write-only one can't be
    /// read back — both fall back to assigning a freshly mapped instance.
    /// </summary>
    private static bool CanUpdateInPlace(MemberInfo member, Type type) =>
        !type.IsValueType && member is FieldInfo or PropertyInfo { GetMethod: not null };

    private static IWireProfile FindProfile(WireProfileRegistry registry) =>
        registry.Find(typeof(TSource), typeof(TDestination))
        ?? throw new InvalidOperationException(
            $"No mapping profile registered for {typeof(TSource).Name} -> {typeof(TDestination).Name}. " +
            $"Register one via CreateMap<{typeof(TSource).Name}, {typeof(TDestination).Name}>().");

    private static Expression GetValueExpression(ResolvedMember member, WireProfileRegistry registry,
        Expression source, ParameterExpression context)
    {
        var value = UnwrapAndRebind(member.Profile.Path, source);
        var destinationType = member.Profile.DestinationMemberType;

        return member.Resolution.Kind == MapClassify.Collection
            ? BuildCollectionValueExpression(value, destinationType, registry, context)
            : EmitValue(member.Resolution.Kind, value, destinationType, context);
    }

    /// <summary>Emits a single value of an already-classified kind (a member, or an element of a collection).</summary>
    private static Expression EmitValue(MapClassify kind, Expression value, Type destinationType,
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
    private static Expression BuildCollectionValueExpression(Expression value, Type destinationType,
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

    private static Expression UnwrapAndRebind(LambdaExpression path, Expression sourceParameter)
    {
        var body = path.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            body = unary.Operand;

        return new ReplaceParameterVisitor(path.Parameters[0], sourceParameter).Visit(body);
    }

    private static MemberInfo GetDestinationMember(LambdaExpression selector)
    {
        var body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            body = unary.Operand;

        return body is MemberExpression member
            ? member.Member
            : throw new InvalidOperationException($"Expected a direct member access, got '{selector}'.");
    }
}

internal sealed class ReplaceParameterVisitor(ParameterExpression from, Expression to) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) =>
        node == from ? to : base.VisitParameter(node);
}

/// <summary>Whether a lambda's body references one of its parameters (e.g. whether it needs a context).</summary>
internal sealed class ParameterUsageVisitor : ExpressionVisitor
{
    private readonly ParameterExpression _parameter;
    private bool _found;

    private ParameterUsageVisitor(ParameterExpression parameter) => _parameter = parameter;

    public static bool Uses(LambdaExpression lambda, ParameterExpression parameter)
    {
        var visitor = new ParameterUsageVisitor(parameter);
        visitor.Visit(lambda.Body);
        return visitor._found;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        _found |= node == _parameter;
        return node;
    }
}
