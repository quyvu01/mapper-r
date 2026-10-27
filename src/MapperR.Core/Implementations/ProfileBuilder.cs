using System.Linq.Expressions;
using MapperR.Core.Abstractions;
using MapperR.Core.Entities;
using MapperR.Core.Exceptions;
using MapperR.Core.Extensions;

namespace MapperR.Core.Implementations;

internal sealed class ProfileBuilder<TSource, TDestination> : IProfileBuilder<TSource, TDestination>
{
    private readonly IWireProfileProvider _provider;
    private readonly Lazy<LambdaExpression> _profileLazy;

    public ProfileBuilder(IWireProfileProvider provider)
    {
        _provider = provider;
        _profileLazy = new Lazy<LambdaExpression>(BuildProfile);
    }

    // Temporary wrap tho `Expression<Func<TSource, TDestination, TDestination>>`
    public LambdaExpression GetProfile() => _profileLazy.Value;

    private LambdaExpression BuildProfile() => BuildProfile(_provider.Resolve<TSource, TDestination>());

    private LambdaExpression BuildProfile(IWireProfile wireProfile)
    {
        if (wireProfile is null)
            throw new Exception("TBD");
        var sourceParam = wireProfile.SourceParam;
        var destinationParam = wireProfile.DestinationParam;
        MemberBinding[] memberExpressions =
        [
            .. wireProfile.MemberProfiles.Select(x => BuildExpression(x, wireProfile))
                .Where(expr => expr is not null)
        ];
        var body = Expression.MemberInit(Expression.New(wireProfile.DestinationType), memberExpressions);
        var parametersCountVisitor = new ParameterCountVisitor();
        parametersCountVisitor.Visit(body);
        var parameterCount = parametersCountVisitor.ParametersCount;
        return parameterCount switch
        {
            0 => Expression.Lambda(body),
            1 => Expression.Lambda(body, sourceParam),
            2 => Expression.Lambda(body, sourceParam, destinationParam),
            _ => throw new Exception("Cannot have enough params...")
        };
    }

    private MemberAssignment BuildExpression(IMemberProfile memberProfile, IWireProfile wireProfile)
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
            var expr = BindExpressionParameter(memberPath, wireProfile.SourceParam, wireProfile.DestinationParam);
            var assignedExpr = ConvertExpression(expr.Body, selectorExpression.Type, selectorExpression.Member.Name);

            if (memberProfile.MemberMapInfo.PreConditionPredicate is { } precondition)
            {
                var preConditionLambda =
                    BindExpressionParameter(precondition, wireProfile.SourceParam, wireProfile.DestinationParam);
                assignedExpr = Expression.Condition(preConditionLambda.Body, assignedExpr,
                    Expression.Default(selectorExpression.Type));
            }

            return Expression.Bind(selectorExpression.Member, assignedExpr);
        }

        var dependencyProfile = _provider.GetDependency(memberProfile);
        if (dependencyProfile is null)
            throw MapperException.NotImplemented(
                $"Mapping the collection member '{selectorExpression.Member.Name}' without a profile");
        if (memberProfile.MemberMapInfo is { Classifier: MapClassifier.Candidate })
        {
            var memberReplacement = memberProfile.MemberMapInfo.MemberPath.Body;
            var dependencyExpression = BuildProfile(dependencyProfile);
            var childVisitor = new ReplaceChildParameter(dependencyProfile.SourceParam, memberReplacement);
            var childExpression = childVisitor.Visit(dependencyExpression.Body);
            return Expression.Bind(selectorExpression.Member, childExpression);
        }

        // It means we touch to `Collection` type

        return null;
    }

    private static Expression ConvertExpression(Expression expression, Type targetType, string memberName)
    {
        var pathType = expression.Type;
        if (pathType == targetType) return expression;

        if (!pathType.CanImplicitlyConvert(targetType))
            throw MapperException.UnsupportedConversion(typeof(TSource), typeof(TDestination), memberName, pathType,
                targetType);

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

    private sealed class ParameterCountVisitor : ExpressionVisitor
    {
        private readonly HashSet<Type> _parameterTypes = [];
        internal int ParametersCount => _parameterTypes.Count;

        protected override Expression VisitParameter(ParameterExpression node)
        {
            _parameterTypes.Add(node.Type);
            return base.VisitParameter(node);
        }
    }

    private sealed class ReplaceChildParameter(ParameterExpression srcExpression, Expression parentExpression)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == srcExpression ? parentExpression : base.VisitParameter(node);
    }

    #endregion
}