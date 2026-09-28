using System.Linq.Expressions;
using System.Reflection;
using MapperR.Core.Abstractions;
using MapperR.Core.Registries;

namespace MapperR.Core.Implementations;

/// <summary>
/// Composes the single <see cref="Expression{TDelegate}"/> of shape <c>Func&lt;TSource,TDestination&gt;</c>
/// that is the sole source of truth for a type pair — compiled once for <c>Map()</c>, and (later) handed
/// directly to <c>IQueryable.Select()</c> for <c>ProjectTo</c>, so both paths always agree.
/// </summary>
internal static class TypeMapExpressionBuilder<TSource, TDestination>
    where TDestination : new()
{
    public static Expression<Func<TSource, TDestination>> Build(WireProfileRegistry registry)
    {
        var profile = registry.Find(typeof(TSource), typeof(TDestination));
        if (profile is null)
            throw new InvalidOperationException(
                $"No mapping profile registered for {typeof(TSource).Name} -> {typeof(TDestination).Name}. " +
                $"Register one via CreateMap<{typeof(TSource).Name}, {typeof(TDestination).Name}>().");

        var sourceParameter = Expression.Parameter(typeof(TSource), "source");

        MemberBinding[] bindings =
        [
            .. profile.MemberProfiles.Select(member => Expression.Bind(GetDestinationMember(member.Selector),
                GetValueExpression(profile, member, registry, sourceParameter)))
        ];

        var body = Expression.MemberInit(Expression.New(typeof(TDestination)), bindings);
        return Expression.Lambda<Func<TSource, TDestination>>(body, sourceParameter);
    }

    private static Expression GetValueExpression(IWireProfile currentProfile, IMemberProfile member,
        WireProfileRegistry registry, ParameterExpression sourceParameter)
    {
        var rawValue = UnwrapAndRebind(member.Path, sourceParameter);
        var nestedProfile = registry.Find(member.SourceMemberType, member.DestinationMemberType);

        if (nestedProfile is null)
            return member.SourceMemberType == member.DestinationMemberType
                ? rawValue
                : Expression.Convert(rawValue, member.DestinationMemberType);

        if (nestedProfile == currentProfile)
            throw new InvalidOperationException(
                $"Member '{member.MemberName}' on {currentProfile.DestinationType.Name} references its own " +
                $"({currentProfile.SourceType.Name} -> {currentProfile.DestinationType.Name}) mapping, which " +
                "would require unbounded recursion. Circular/self-referential mappings are not supported in " +
                "this version.");

        return InlineNestedMap(member.SourceMemberType, member.DestinationMemberType, registry, rawValue);
    }

    /// <summary>
    /// Recursively builds the nested pair's expression and beta-reduces it into <paramref name="rawValue"/>
    /// (substituting the nested lambda's parameter), instead of invoking a compiled delegate — keeping the
    /// whole tree a single composed expression, so it stays translatable for a future <c>ProjectTo</c>.
    /// </summary>
    private static Expression InlineNestedMap(Type sourceType, Type destinationType, WireProfileRegistry registry,
        Expression rawValue)
    {
        var builderType = typeof(TypeMapExpressionBuilder<,>).MakeGenericType(sourceType, destinationType);
        var buildMethod = builderType.GetMethod(nameof(Build))!;
        var nestedLambda = (LambdaExpression)buildMethod.Invoke(null, [registry])!;

        var substituted = new ReplaceParameterVisitor(nestedLambda.Parameters[0], rawValue).Visit(nestedLambda.Body);

        if (sourceType.IsValueType) return substituted;

        return Expression.Condition(
            Expression.Equal(rawValue, Expression.Constant(null, sourceType)),
            Expression.Default(destinationType),
            substituted);
    }

    private static Expression UnwrapAndRebind(LambdaExpression path, ParameterExpression sourceParameter)
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