using System.Linq.Expressions;
using System.Reflection;
using MapperR.Core.Abstractions;
using MapperR.Core.Entities;
using MapperR.Core.Extensions;

namespace MapperR.Core.Implementations;

internal class WireProfileMap<TSource, TDestination> : IWireProfile<TSource, TDestination>
{
    private const BindingFlags MemberBindingFlags = BindingFlags.Public | BindingFlags.Instance;

    private readonly List<IMemberProfile> _memberProfiles = [];

    public Type SourceType => typeof(TSource);
    public Type DestinationType => typeof(TDestination);

    public IMemberProfile[] MemberProfiles => [.. _memberProfiles, .. GetConventionMemberProfiles()];

    public IWireProfile<TSource, TDestination> ForMember<TProp>(Expression<Func<TDestination, TProp>> selector,
        Expression<Func<TSource, object>> path)
    {
        var memberName = selector.GetMemberName();
        var memberProfile = new MemberProfile<TSource, TDestination, TProp>(memberName, selector, path);
        _memberProfiles.Add(memberProfile);
        return this;
    }

    /// <summary>
    /// Auto-matches destination members that were not explicitly configured via <see cref="ForMember{TProp}"/>
    /// against a source member with the same name — by exact/assignable type, or, for numbers and enums, by
    /// implicit/explicit convertibility (e.g. <c>int</c> → <c>long</c>, <c>MyEnum</c> → <c>int</c>).
    /// </summary>
    private IEnumerable<IMemberProfile> GetConventionMemberProfiles()
    {
        var configuredNames = new HashSet<string>(
            _memberProfiles.Select(member => member.MemberName), StringComparer.OrdinalIgnoreCase);

        var sourceProperties = typeof(TSource).GetProperties(MemberBindingFlags)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

        var destinationProperties = typeof(TDestination).GetProperties(MemberBindingFlags)
            .Where(property => property.CanWrite && property.GetIndexParameters().Length == 0);

        foreach (var destinationProperty in destinationProperties)
        {
            if (configuredNames.Contains(destinationProperty.Name)) continue;
            if (!sourceProperties.TryGetValue(destinationProperty.Name, out var sourceProperty)) continue;
            if (!IsCompatible(sourceProperty.PropertyType, destinationProperty.PropertyType)) continue;

            yield return CreateMemberProfile(sourceProperty, destinationProperty);
        }
    }

    private static bool IsCompatible(Type sourceType, Type destinationType)
    {
        if (sourceType is null) return false;
        if (destinationType.IsAssignableFrom(sourceType)) return true;
        if (!IsNumericOrEnum(sourceType) || !IsNumericOrEnum(destinationType)) return false;

        try
        {
            _ = Expression.Convert(Expression.Parameter(sourceType), destinationType);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsNumericOrEnum(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType.IsEnum || Type.GetTypeCode(underlyingType) is >= TypeCode.SByte and <= TypeCode.Decimal;
    }

    private static IMemberProfile CreateMemberProfile(PropertyInfo sourceProperty, PropertyInfo destinationProperty)
    {
        var destinationParameter = Expression.Parameter(typeof(TDestination), "d");
        var selector = Expression.Lambda(
            Expression.Property(destinationParameter, destinationProperty),
            destinationParameter);

        var sourceParameter = Expression.Parameter(typeof(TSource), "s");
        var path = Expression.Lambda(
            Expression.Convert(Expression.Property(sourceParameter, sourceProperty), typeof(object)),
            sourceParameter);

        var memberProfileType = typeof(MemberProfile<,,>)
            .MakeGenericType(typeof(TSource), typeof(TDestination), destinationProperty.PropertyType);

        return (IMemberProfile)Activator.CreateInstance(memberProfileType,
            destinationProperty.Name, selector, path)!;
    }
}
