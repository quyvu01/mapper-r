using System.Linq.Expressions;
using System.Reflection;
using MapperR.Core.Abstractions;
using MapperR.Core.Entities;
using MapperR.Core.Extensions;
using MapperR.Core.Helpers;

namespace MapperR.Core.Implementations;

internal class WireProfileMap<TSource, TDestination> : IWireProfile<TSource, TDestination>
{
    private const BindingFlags MemberBindingFlags = BindingFlags.Public | BindingFlags.Instance;

    private readonly List<IMemberProfile> _memberProfiles = [];
    private readonly HashSet<string> _ignoreMembers = new(StringComparer.OrdinalIgnoreCase);

    public Type SourceType => typeof(TSource);
    public Type DestinationType => typeof(TDestination);

    public IMemberProfile[] MemberProfiles => GetMemberProfiles();

    private IMemberProfile[] GetMemberProfiles()
    {
        List<IMemberProfile> memberProfiles = [.. _memberProfiles, .. GetConventionMemberProfiles()];
        return [.. memberProfiles.Where(x => !_ignoreMembers.Contains(x.MemberName))];
    }

    public IWireProfile<TSource, TDestination> ForMember<TProp>(Expression<Func<TDestination, TProp>> selector,
        Expression<Func<TSource, object>> path)
    {
        var memberName = selector.GetMemberName();
        var sourceType = path.GetSourceType();
        var destinationType = typeof(TProp);
        var classify = MemberClassifier.ClassifyLocal(sourceType, destinationType);
        var memberProfile =
            new MemberProfile<TSource, TDestination, TProp>(memberName, selector, path, classify, IsExplicit: true);
        _memberProfiles.Add(memberProfile);
        return this;
    }

    public IWireProfile<TSource, TDestination> Ignore<TProp>(Expression<Func<TDestination, TProp>> selector)
    {
        var memberName = selector.GetMemberName();
        _ignoreMembers.Add(memberName);
        return this;
    }

    private IEnumerable<IMemberProfile> GetConventionMemberProfiles()
    {
        var configuredNames = new HashSet<string>(_memberProfiles.Select(member => member.MemberName),
            StringComparer.OrdinalIgnoreCase);

        var sourceProperties = typeof(TSource).GetProperties(MemberBindingFlags)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

        var destinationProperties = typeof(TDestination).GetProperties(MemberBindingFlags)
            .Where(property => property.CanWrite && property.GetIndexParameters().Length == 0);

        foreach (var destinationProperty in destinationProperties)
        {
            if (configuredNames.Contains(destinationProperty.Name)) continue;
            if (!sourceProperties.TryGetValue(destinationProperty.Name, out var sourceProperty)) continue;
            var classify = Classify(sourceProperty.PropertyType, destinationProperty.PropertyType);
            if (classify == MapClassify.Invalid) continue;

            yield return CreateMemberProfile(sourceProperty, destinationProperty, classify);
        }
    }

    private static MapClassify Classify(Type sourceType, Type destinationType) =>
        MemberClassifier.ClassifyLocal(sourceType, destinationType);

    private static IMemberProfile CreateMemberProfile(PropertyInfo sourceProperty, PropertyInfo destinationProperty,
        MapClassify classify)
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
            destinationProperty.Name, selector, path, classify, false)!;
    }
}