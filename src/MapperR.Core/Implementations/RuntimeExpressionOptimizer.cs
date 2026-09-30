using System.Linq.Expressions;
using System.Reflection;
using MapperR.Core.Abstractions;
using MapperR.Core.Registries;

namespace MapperR.Core.Implementations;

/// <summary>
/// Rewrites the trees <see cref="TypeMapExpressionBuilder{TSource,TDestination}"/> produces, for the runtime
/// engine only. The builder's trees stay the single source of truth that <c>maprgen</c> prints.
/// </summary>
internal static class RuntimeExpressionOptimizer
{
    /// <summary>Deepest chain of pairs built into one tree; beyond it a nested pair stays a call.</summary>
    private const int MaxInlineDepth = 6;

    /// <summary>Cheap, tree-only rewrites. Run eagerly, because they decide whether a context is needed.</summary>
    public static Expression<TDelegate> Inline<TDelegate>(Expression<TDelegate> lambda, MapperOptimizations options,
        WireProfileRegistry registry)
    {
        if (!options.HasFlag(MapperOptimizations.InlineAcyclicPairs)) return lambda;

        var context = lambda.Parameters[^1];
        var body = new InlineVisitor(registry, context, depth: 0).Visit(lambda.Body);
        return Expression.Lambda<TDelegate>(body, lambda.Parameters);
    }

    /// <summary>Rewrites that compile delegates; run lazily, right before the tree itself is compiled.</summary>
    public static Expression<TDelegate> Hoist<TDelegate>(Expression<TDelegate> lambda, MapperOptimizations options)
    {
        if (!options.HasFlag(MapperOptimizations.HoistCollectionLambdas)) return lambda;

        var context = lambda.Parameters[^1];
        var body = new HoistVisitor(context, compile: true).Visit(lambda.Body);
        return Expression.Lambda<TDelegate>(body, lambda.Parameters);
    }

    /// <summary>
    /// The same rewrite for <c>maprgen</c>: the element mapper stays a lambda (printed as a C# lambda that takes the
    /// context as a parameter, so it captures nothing) instead of being compiled into a constant delegate.
    /// </summary>
    public static Expression<TDelegate> HoistForPrinting<TDelegate>(Expression<TDelegate> lambda) =>
        (Expression<TDelegate>)HoistForPrinting((LambdaExpression)lambda);

    public static LambdaExpression HoistForPrinting(LambdaExpression lambda)
    {
        var context = lambda.Parameters[^1];
        var body = new HoistVisitor(context, compile: false).Visit(lambda.Body);
        return Expression.Lambda(lambda.Type, body, lambda.Parameters);
    }

    private static bool CanBeNull(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    /// <summary>Replaces <c>MapperRuntime.MapNested/MapNestedInto</c> calls of acyclic pairs with the child's tree.</summary>
    private sealed class InlineVisitor(WireProfileRegistry registry, ParameterExpression context, int depth)
        : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(MapperRuntime) && depth < MaxInlineDepth &&
                node.Method.GetGenericArguments() is [var sourceType, var destinationType] &&
                registry.Find(sourceType, destinationType) is { } profile &&
                !registry.IsCyclic(profile) && Nullable.GetUnderlyingType(sourceType) is null &&
                node.Arguments[^1] == context)
            {
                var inlined = node.Method.Name == nameof(MapperRuntime.MapNested)
                    ? InlineMap(sourceType, destinationType, Visit(node.Arguments[0]))
                    : InlineUpdate(sourceType, destinationType, Visit(node.Arguments[0]), Visit(node.Arguments[1]));
                if (inlined is not null) return inlined;
            }

            return base.VisitMethodCall(node);
        }

        private BlockExpression InlineMap(Type sourceType, Type destinationType, Expression value)
        {
            if (Build(sourceType, destinationType, "Build") is not { } build) return null;

            var source = Expression.Variable(sourceType, "inlinedSource");
            var body = Child(build, source, destination: null);
            return Expression.Block(destinationType, [source],
                Expression.Assign(source, value),
                CanBeNull(sourceType)
                    ? Expression.Condition(Expression.Equal(source, Expression.Constant(null, sourceType)),
                        Expression.Default(destinationType), body)
                    : body);
        }

        private BlockExpression InlineUpdate(Type sourceType, Type destinationType, Expression value, Expression target)
        {
            if (Build(sourceType, destinationType, "Build") is not { } build ||
                Build(sourceType, destinationType, "BuildUpdate") is not { } update) return null;

            var source = Expression.Variable(sourceType, "inlinedSource");
            var destination = Expression.Variable(destinationType, "inlinedDestination");
            // MapInto semantics: null source clears the target, null target creates, otherwise update in place.
            Expression body = Expression.Condition(
                Expression.Equal(destination, Expression.Constant(null, destinationType)),
                Child(build, source, destination: null),
                Child(update, source, destination));
            if (CanBeNull(sourceType))
                body = Expression.Condition(Expression.Equal(source, Expression.Constant(null, sourceType)),
                    Expression.Default(destinationType), body);

            return Expression.Block(destinationType, [source, destination],
                Expression.Assign(source, value),
                Expression.Assign(destination, target),
                body);
        }

        /// <summary>The child's tree with its parameters bound to the parent's variables, itself inlined.</summary>
        private Expression Child(LambdaExpression lambda, ParameterExpression source, ParameterExpression destination)
        {
            var body = lambda.Body;
            body = new ReplaceParameterVisitor(lambda.Parameters[0], source).Visit(body);
            if (destination is not null)
                body = new ReplaceParameterVisitor(lambda.Parameters[1], destination).Visit(body);
            body = new ReplaceParameterVisitor(lambda.Parameters[^1], context).Visit(body);
            return new InlineVisitor(registry, context, depth + 1).Visit(body);
        }

        private LambdaExpression Build(Type sourceType, Type destinationType, string method)
        {
            try
            {
                return (LambdaExpression)typeof(TypeMapExpressionBuilder<,>)
                    .MakeGenericType(sourceType, destinationType)
                    .GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, [registry])!;
            }
            catch (ArgumentException)
            {
                return null; // destination without a parameterless constructor: keep the call
            }
        }
    }

    /// <summary>
    /// <c>Select(value, item =&gt; element).ToList()/ToArray()/ToHashSet()</c> becomes a call to
    /// <see cref="MapperRuntime.MapToList{TSource,TDestination}"/> (or the array/set form). The runtime compiles the
    /// element once, as a constant delegate (<paramref name="compile"/>); <c>maprgen</c> keeps it as a lambda.
    /// </summary>
    private sealed class HoistVisitor(ParameterExpression context, bool compile) : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Enumerable) &&
                node.Method.Name is nameof(Enumerable.ToList) or nameof(Enumerable.ToArray)
                    or nameof(Enumerable.ToHashSet) &&
                node.Arguments is
                [
                    MethodCallExpression
                    {
                        Method.Name: nameof(Enumerable.Select),
                        Arguments: [var collection, LambdaExpression { Parameters.Count: 1 } element]
                    } select
                ] &&
                UsesOnly(element, element.Parameters[0], context))
            {
                var elementContext = Expression.Parameter(typeof(MappingContext), "context");
                var elementBody = new ReplaceParameterVisitor(context, elementContext).Visit(element.Body);
                var types = select.Method.GetGenericArguments();
                var elementLambda = Expression.Lambda(
                    typeof(Func<,,>).MakeGenericType(types[0], typeof(MappingContext), types[1]),
                    elementBody, element.Parameters[0], elementContext);

                var helper = typeof(MapperRuntime).GetMethod("Map" + node.Method.Name)!.MakeGenericMethod(types);
                return Expression.Call(helper, Visit(collection),
                    compile ? Expression.Constant(elementLambda.Compile(), elementLambda.Type) : elementLambda,
                    context);
            }

            return base.VisitMethodCall(node);
        }

        /// <summary>Whether the lambda refers to no variable besides its own item and the context.</summary>
        private static bool UsesOnly(LambdaExpression lambda, ParameterExpression item, ParameterExpression context)
        {
            var scan = new FreeParameterScan();
            scan.Visit(lambda.Body);
            return scan.Free.All(parameter => parameter == item || parameter == context);
        }
    }

    private sealed class FreeParameterScan : ExpressionVisitor
    {
        private readonly HashSet<ParameterExpression> _declared = [];
        private readonly HashSet<ParameterExpression> _used = [];

        public IEnumerable<ParameterExpression> Free => _used.Where(parameter => !_declared.Contains(parameter));

        protected override Expression VisitBlock(BlockExpression node)
        {
            _declared.UnionWith(node.Variables);
            return base.VisitBlock(node);
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            _declared.UnionWith(node.Parameters);
            return base.VisitLambda(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            _used.Add(node);
            return node;
        }
    }
}