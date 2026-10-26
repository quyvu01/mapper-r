using System.Linq.Expressions;
using MapperR.Core.Abstractions;
using MapperR.Core.Entities;
using MapperR.Core.Exceptions;
using MapperR.Core.Extensions;

namespace MapperR.Core.Implementations;

internal sealed class ProfileBuilder<TSource, TDestination> : IProfileBuilder<TSource, TDestination>
{
    private readonly IWireProfileProvider _provider;
    private readonly Lazy<Expression<Func<TSource, TDestination, TDestination>>> _profileLazy;

    public ProfileBuilder(IWireProfileProvider provider)
    {
        _provider = provider;
        _profileLazy = new Lazy<Expression<Func<TSource, TDestination, TDestination>>>(BuildProfile);
    }

    public Expression<Func<TSource, TDestination, TDestination>> GetProfile() => _profileLazy.Value;

    public Expression<Func<TSource, TDestination, TDestination>> BuildProfile()
    {
        var profile = _provider.Resolve<TSource, TDestination>();
        var sourceParam = profile.SourceParam;
        var destinationParam = profile.DestinationParam;
        MemberBinding[] memberExpressions =
        [
            .. profile.MemberProfiles.Select(x => BuildExpression(x, sourceParam, destinationParam))
                .Where(expr => expr is not null)
        ];
        var body = Expression.MemberInit(Expression.New(typeof(TDestination)), memberExpressions);
        return Expression.Lambda<Func<TSource, TDestination, TDestination>>(body, sourceParam, destinationParam);
    }

    private MemberAssignment BuildExpression(IMemberProfile memberProfile, ParameterExpression sourceParam,
        ParameterExpression destinationParam)
    {
        if (memberProfile.MemberMapInfo.IsIgnored ||
            memberProfile.MemberMapInfo.Classifier == MapClassifier.None) return null;
        var selector = memberProfile.Selector;
        if (selector.Body is not MemberExpression selectorExpression)
            throw MapperException.InvalidSelector(selector, nameof(selector));
        var memberPath = memberProfile.MemberMapInfo.MemberPath;
        if (memberProfile.MemberMapInfo.Classifier is MapClassifier.Invalid)
            throw MapperException.UnsupportedConversion(typeof(TSource), typeof(TDestination),
                selectorExpression.Member.Name, memberPath.GetBodyType(), selectorExpression.Type);
        if (memberProfile.MemberMapInfo is { Classifier: MapClassifier.Simple })
        {
            var expr = BindExpressionParameter(memberPath, sourceParam, destinationParam);
            var assignedExpr = ConvertExpression(expr.Body, selectorExpression.Type, selectorExpression.Member.Name);

            if (memberProfile.MemberMapInfo.PreConditionPredicate is { } precondition)
            {
                var preConditionLambda = BindExpressionParameter(precondition, sourceParam, destinationParam);
                assignedExpr = Expression.Condition(preConditionLambda.Body, assignedExpr,
                    Expression.Default(selectorExpression.Type));
            }

            return Expression.Bind(selectorExpression.Member, assignedExpr);
        }

        if (memberProfile.MemberMapInfo is { Classifier: MapClassifier.Candidate })
        {
        }

        if (memberProfile.MemberMapInfo is { Classifier: MapClassifier.Collection })
        {
            var dependencyProfile = _provider.GetDependency(memberProfile);
            if (dependencyProfile is null)
                throw MapperException.NotImplemented(
                    $"Mapping the collection member '{selectorExpression.Member.Name}' without a profile");
            // Maybe we havet to travel with recursive
        }

        // Temp
        return null;
    }

    private static Expression ConvertExpression(Expression expression, Type targetType, string memberName)
    {
        var pathType = expression.Type;
        if (pathType == targetType) return expression;

        if (!pathType.CanImplicitlyConvert(targetType))
            throw MapperException.UnsupportedConversion(typeof(TSource), typeof(TDestination), memberName, pathType,
                targetType);

        // Convert accepts any node (member, call, conditional, constant, ...). For T? -> U? it lifts null by itself,
        // so a nullable operand must not be unwrapped first.
        return Expression.Convert(expression, targetType);
    }

    private static LambdaExpression BindExpressionParameter(LambdaExpression expression,
        ParameterExpression sourceParam,
        ParameterExpression destinationParam)
    {
        var map = expression.Parameters.Count switch
        {
            1 => new Dictionary<ParameterExpression, ParameterExpression>
                { { expression.Parameters[0], sourceParam } },
            2 => new Dictionary<ParameterExpression, ParameterExpression>
            {
                { expression.Parameters[0], sourceParam },
                { expression.Parameters[1], destinationParam }
            },
            _ => throw MapperException.Unreachable(
                $"'{expression}' has {expression.Parameters.Count} parameters; only 1 (source) or 2 (source, destination) are allowed.")
        };
        var expr = new ReplacedVisitor(map).Visit(expression);
        return expr as LambdaExpression ??
               throw MapperException.Unreachable($"Replacing the parameters of '{expression}' did not give a lambda.");
    }


    #region Expression visitors

    private sealed class ReplacedVisitor(Dictionary<ParameterExpression, ParameterExpression> map)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => map.GetValueOrDefault(node, node);
    }

    #endregion
}