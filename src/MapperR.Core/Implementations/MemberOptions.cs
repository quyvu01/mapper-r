using System.Collections;
using System.Linq.Expressions;
using MapperR.Core.Abstractions;
using MapperR.Core.Entities;
using MapperR.Core.Exceptions;
using MapperR.Core.Extensions;
using MapperR.Core.Helpers;

namespace MapperR.Core.Implementations;

internal sealed class MemberOptions<TSource, TDestination>(LambdaExpression selector)
    : IMemberOptions<TSource, TDestination>, IMemberMapInfo
{
    public bool IsIgnored { get; private set; }
    public LambdaExpression MemberPath { get; private set; }
    public LambdaExpression PreConditionPredicate { get; private set; }
    public MapClassifier Classifier { get; private set; }

    public IMemberOptions<TSource, TDestination> PreCondition(Expression<Func<TSource, bool>> predicate)
    {
        PreConditionPredicate = predicate;
        return this;
    }

    public IMemberOptions<TSource, TDestination> PreCondition(Expression<Func<TSource, TDestination, bool>> predicate)
    {
        PreConditionPredicate = predicate;
        return this;
    }

    public IMemberOptions<TSource, TDestination> MapFrom(Expression<Func<TSource, object>> path)
    {
        MemberPath = path.UnwrapBoxing();
        return this;
    }

    public IMemberOptions<TSource, TDestination> MapFrom(Expression<Func<TSource, TDestination, object>> path)
    {
        MemberPath = path.UnwrapBoxing();
        return this;
    }

    public IMemberOptions<TSource, TDestination> Ignore()
    {
        IsIgnored = true;
        return this;
    }

    internal void ClassifyMap()
    {
        if (IsIgnored) return;
        // ForMember without MapFrom (e.g. only PreCondition): fall back to the same-name source member.
        MemberPath ??= BuildConventionPath();
        Classifier = Classify(selector.GetBodyType(), MemberPath.GetBodyType());
        // A collection cannot be written into a string; dropped like an unmatched member.
        if (Classifier == MapClassifier.None) IsIgnored = true;
    }

    private static MapClassifier Classify(Type destination, Type source)
    {
        var destinationType = Nullable.GetUnderlyingType(destination) ?? destination;
        var sourceType = Nullable.GetUnderlyingType(source) ?? source;

        // Into a string: only string -> string for now. A collection cannot be written into a string (dropped).
        // TODO: formatting (int -> string, object -> ToString()) once ConvertExpression supports it.
        if (destinationType == typeof(string))
            return sourceType == typeof(string) ? MapClassifier.Simple
                : IsCollection(sourceType) ? MapClassifier.None
                : MapClassifier.Invalid;

        var destinationIsScalar = GeneralHelpers.IsScalar(destinationType);
        var sourceIsScalar = GeneralHelpers.IsScalar(sourceType);

        // TODO: parsing a string into a scalar (int, enum, DateTime, Guid, ...) once ConvertExpression supports it.
        if (sourceType == typeof(string) && destinationIsScalar) return MapClassifier.Invalid;

        // Simple must be exactly what ConvertExpression can build: the same type or an implicit conversion
        // (int -> long, int -> int?, int? -> long?). Narrowing, enum <-> number and T? -> T are Invalid for now.
        if (destinationIsScalar && sourceIsScalar)
            return source == destination || source.CanImplicitlyConvert(destination)
                ? MapClassifier.Simple
                : MapClassifier.Invalid;

        var destinationIsCollection = IsCollection(destinationType);
        var sourceIsCollection = IsCollection(sourceType);
        if (destinationIsCollection && sourceIsCollection) return MapClassifier.Collection;
        if (destinationIsCollection || sourceIsCollection) return MapClassifier.Invalid;

        // A scalar on one side only (int <- Address, Address <- int); `object <- int` is still assignable.
        if ((destinationIsScalar || sourceIsScalar) && !destination.IsAssignableFrom(source))
            return MapClassifier.Invalid;

        return MapClassifier.Candidate;
    }

    private static bool IsCollection(Type type) => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    private LambdaExpression BuildConventionPath()
    {
        var body = selector.Body is UnaryExpression
        {
            NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
        } unary
            ? unary.Operand
            : selector.Body;
        var name = (body as MemberExpression)?.Member.Name
                   ?? throw MapperException.InvalidSelector(selector, nameof(selector));
        var property = typeof(TSource).GetProperty(name)
                       ?? throw MapperException.NoSourceMember(typeof(TSource), typeof(TDestination), name);
        var parameter = Expression.Parameter(typeof(TSource), "source");
        return Expression.Lambda(Expression.Property(parameter, property), parameter);
    }

    internal void MapFromInternal(LambdaExpression path) => MemberPath = path;
}