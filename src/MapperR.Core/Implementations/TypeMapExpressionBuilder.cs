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
    private static readonly MethodInfo MapNestedIntoMethod =
        typeof(MapperRuntime).GetMethod(nameof(MapperRuntime.MapNestedInto))!;

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
            ? ValueExpressions.BuildCollection(value, destinationType, registry, context)
            : ValueExpressions.EmitValue(member.Resolution.Kind, value, destinationType, context);
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
