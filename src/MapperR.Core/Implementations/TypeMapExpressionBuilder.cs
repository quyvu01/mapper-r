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
        var profile = FindProfile(registry);
        var sourceParameter = Expression.Parameter(typeof(TSource), "source");

        MemberBinding[] bindings =
        [
            .. profile.MemberProfiles.Select(member => Expression.Bind(GetDestinationMember(member.Selector),
                GetValueExpression(profile, member, registry, sourceParameter)))
        ];

        var body = Expression.MemberInit(Expression.New(typeof(TDestination)), bindings);
        return Expression.Lambda<Func<TSource, TDestination>>(body, sourceParameter);
    }

    /// <summary>
    /// The "map onto an existing instance" form: each mapped member is assigned the same value expression
    /// <see cref="Build"/> uses, except nested objects, which are updated in place (their reference is kept, as
    /// AutoMapper does) — recursively, via the nested pair's own update. Collections are replaced. Members
    /// without a mapping keep their current value. Returns the destination so value-type destinations work too.
    /// </summary>
    public static Expression<Func<TSource, TDestination, TDestination>> BuildUpdate(WireProfileRegistry registry)
    {
        var source = Expression.Parameter(typeof(TSource), "source");
        var destination = Expression.Parameter(typeof(TDestination), "destination");

        var body = Expression.Block(BuildUpdateStatements(registry, source, destination), destination);
        return Expression.Lambda<Func<TSource, TDestination, TDestination>>(body, source, destination);
    }

    /// <summary>Statements (void) updating <paramref name="destination"/> from <paramref name="source"/>.</summary>
    private static Expression BuildUpdateStatements(WireProfileRegistry registry, Expression source,
        Expression destination)
    {
        var profile = FindProfile(registry);
        Expression[] statements =
        [
            .. profile.MemberProfiles.Select(member =>
                BuildMemberUpdate(profile, member, registry, source, destination))
        ];

        return statements.Length == 0 ? Expression.Empty() : Expression.Block(typeof(void), statements);
    }

    private static Expression BuildMemberUpdate(IWireProfile profile, IMemberProfile member,
        WireProfileRegistry registry, Expression source, Expression destination)
    {
        var destinationMember = GetDestinationMember(member.Selector);
        var target = Expression.MakeMemberAccess(destination, destinationMember);

        return IsNestedObject(profile, member, registry) &&
               CanUpdateInPlace(destinationMember, member.DestinationMemberType)
            ? NestedObjectUpdate(member, registry, source, target)
            : Expression.Assign(target, GetValueExpression(profile, member, registry, source));
    }

    /// <summary>
    /// <c>nested = &lt;source value&gt;; if (nested == null) target = null; else if (target == null)
    /// target = &lt;new mapped instance&gt;; else &lt;nested update onto target&gt;</c>. The temporary keeps the
    /// source value from being evaluated more than once (the update form is never translated to SQL).
    /// </summary>
    private static Expression NestedObjectUpdate(IMemberProfile member, WireProfileRegistry registry,
        Expression source, MemberExpression target)
    {
        var (sourceType, destinationType) = (member.SourceMemberType, member.DestinationMemberType);
        var nested = Expression.Variable(sourceType, "nested");

        var intoTarget = Expression.IfThenElse(
            Expression.Equal(target, Expression.Constant(null, destinationType)),
            Expression.Assign(target, GetNestedMapBody(sourceType, destinationType, registry, nested)),
            GetNestedUpdateStatements(sourceType, destinationType, registry, nested, target));

        var body = sourceType.IsValueType && Nullable.GetUnderlyingType(sourceType) is null
            ? intoTarget
            : Expression.IfThenElse(
                Expression.Equal(nested, Expression.Constant(null, sourceType)),
                Expression.Assign(target, Expression.Default(destinationType)),
                intoTarget);

        return Expression.Block(typeof(void), [nested],
            Expression.Assign(nested, UnwrapAndRebind(member.Path, source)), body);
    }

    /// <summary>Same classification as <see cref="GetValueExpression"/>: a registered, non-self, non-collection pair.</summary>
    private static bool IsNestedObject(IWireProfile profile, IMemberProfile member, WireProfileRegistry registry)
    {
        if (GetElementType(member.SourceMemberType) is not null &&
            GetElementType(member.DestinationMemberType) is not null) return false;

        var nestedProfile = registry.Find(member.SourceMemberType, member.DestinationMemberType);
        return nestedProfile is not null && nestedProfile != profile;
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

    private static Expression GetValueExpression(IWireProfile currentProfile, IMemberProfile member,
        WireProfileRegistry registry, Expression sourceParameter)
    {
        var rawValue = UnwrapAndRebind(member.Path, sourceParameter);

        var sourceElementType = GetElementType(member.SourceMemberType);
        var destinationElementType = GetElementType(member.DestinationMemberType);
        if (sourceElementType is not null && destinationElementType is not null)
            return BuildCollectionValueExpression(currentProfile, member, registry, rawValue, sourceElementType,
                destinationElementType);

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
    /// Builds <c>rawValue == null ? null : rawValue.Select(item => &lt;element map&gt;).ToList()/.ToArray()/...</c>
    /// for a member whose source and destination are both an enumerable of some element type.
    /// </summary>
    private static Expression BuildCollectionValueExpression(IWireProfile currentProfile, IMemberProfile member,
        WireProfileRegistry registry, Expression rawValue, Type sourceElementType, Type destinationElementType)
    {
        var elementParameter = Expression.Parameter(sourceElementType, "item");
        var elementBody = GetElementValueExpression(currentProfile, member, registry, elementParameter,
            sourceElementType, destinationElementType);
        var elementLambda = Expression.Lambda(elementBody, elementParameter);

        var selectCall = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select),
            [sourceElementType, destinationElementType], rawValue, elementLambda);

        var materialized = MaterializeCollection(selectCall, member.DestinationMemberType, destinationElementType);

        return Expression.Condition(
            Expression.Equal(rawValue, Expression.Constant(null, member.SourceMemberType)),
            Expression.Default(member.DestinationMemberType),
            materialized);
    }

    /// <summary>
    /// Value expression for a single element inside a mapped collection. Elements are assumed non-null
    /// (no per-element null guard) — only the collection reference itself is null-checked.
    /// </summary>
    private static Expression GetElementValueExpression(IWireProfile currentProfile, IMemberProfile member,
        WireProfileRegistry registry, ParameterExpression elementParameter, Type sourceElementType,
        Type destinationElementType)
    {
        if (sourceElementType == destinationElementType) return elementParameter;

        var elementProfile = registry.Find(sourceElementType, destinationElementType);
        if (elementProfile is null) return Expression.Convert(elementParameter, destinationElementType);

        if (elementProfile == currentProfile)
            throw new InvalidOperationException(
                $"Member '{member.MemberName}' on {currentProfile.DestinationType.Name} maps a collection of its " +
                $"own ({currentProfile.SourceType.Name} -> {currentProfile.DestinationType.Name}) type, which " +
                "would require unbounded recursion. Circular/self-referential mappings are not supported in " +
                "this version.");

        return GetNestedMapBody(sourceElementType, destinationElementType, registry, elementParameter);
    }

    /// <summary>
    /// Returns the element type of an enumerable type (array, <c>List&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>,
    /// custom collections implementing it, ...), or <c>null</c> if the type isn't a collection of something
    /// (strings are deliberately excluded, even though they implement <c>IEnumerable&lt;char&gt;</c>).
    /// </summary>
    private static Type GetElementType(Type type)
    {
        if (type == typeof(string)) return null;
        if (type.IsArray) return type.GetElementType();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];

        var enumerableInterface = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableInterface?.GetGenericArguments()[0];
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

    /// <summary>
    /// Recursively builds the nested pair's expression and beta-reduces it into <paramref name="value"/>
    /// (substituting the nested lambda's parameter), instead of invoking a compiled delegate — keeping the
    /// whole tree a single composed expression, so it stays translatable for a future <c>ProjectTo</c>.
    /// </summary>
    private static Expression GetNestedMapBody(Type sourceType, Type destinationType, WireProfileRegistry registry,
        Expression value)
    {
        var nestedLambda = (LambdaExpression)InvokeNested(sourceType, destinationType, nameof(Build), registry);
        return new ReplaceParameterVisitor(nestedLambda.Parameters[0], value).Visit(nestedLambda.Body);
    }

    private static Expression GetNestedUpdateStatements(Type sourceType, Type destinationType,
        WireProfileRegistry registry, Expression source, Expression destination) =>
        (Expression)InvokeNested(sourceType, destinationType, nameof(BuildUpdateStatements), registry, source,
            destination);

    /// <summary>
    /// Calls a static member of the builder closed over runtime-only types, rethrowing the original exception
    /// (e.g. a nested self-reference error) instead of a <see cref="TargetInvocationException"/> wrapper.
    /// </summary>
    private static object InvokeNested(Type sourceType, Type destinationType, string methodName,
        params object[] arguments)
    {
        var method = typeof(TypeMapExpressionBuilder<,>).MakeGenericType(sourceType, destinationType)
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return method.Invoke(null, arguments)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static Expression InlineNestedMap(Type sourceType, Type destinationType, WireProfileRegistry registry,
        Expression rawValue)
    {
        var substituted = GetNestedMapBody(sourceType, destinationType, registry, rawValue);

        if (sourceType.IsValueType) return substituted;

        return Expression.Condition(
            Expression.Equal(rawValue, Expression.Constant(null, sourceType)),
            Expression.Default(destinationType),
            substituted);
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
