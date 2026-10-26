using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;

namespace MapperR.Core.Implementations;

internal class WireProfile<TSource, TDestination> : IWireProfile<TSource, TDestination>, IWireProfileBuilder
{
    public Type SourceType => typeof(TSource);
    public Type DestinationType => typeof(TDestination);
    private readonly Dictionary<string, IMemberProfile> _memberProfiles = [];
    public IMemberProfile[] MemberProfiles { get; private set; } = [];
    public ParameterExpression SourceParam { get; } = Expression.Parameter(typeof(TSource), "source");

    public ParameterExpression DestinationParam { get; } =
        Expression.Parameter(typeof(TDestination), "destination");

    public IWireProfile<TSource, TDestination> ForMember<TProp>(Expression<Func<TDestination, TProp>> selector,
        [NotNull] Action<IMemberOptions<TSource, TDestination>> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var path = new MemberOptions<TSource, TDestination>(selector);
        options.Invoke(path);
        path.ClassifyMap();
        var selectorName = selector.GetMemberName();
        _memberProfiles[selectorName] = new MemberProfile(selector, path);
        return this;
    }

    /// <summary>
    /// Fill missing map
    /// </summary>
    public void BuildWireProfiles()
    {
        var sourceProperties = typeof(TSource)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .ToHashSet();

        var destinationProperties = typeof(TDestination)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Where(p => !_memberProfiles.ContainsKey(p.Name));

        var mappableProperties = destinationProperties
            .Aggregate(new List<IMemberProfile>(), (acc, destination) =>
            {
                var matchedSourceProperty = sourceProperties.FirstOrDefault(source => source.Name == destination.Name);
                if (matchedSourceProperty is null) return acc;
                var selector = BuildMemberProfile(DestinationParam, destination);
                var memberMapInfo = new MemberOptions<TSource, TDestination>(selector);
                memberMapInfo.MapFromInternal(BuildMemberProfile(SourceParam, matchedSourceProperty));
                memberMapInfo.ClassifyMap();
                acc.Add(new MemberProfile(selector, memberMapInfo));
                return acc;
            });
        var memberProfiles = _memberProfiles.Select(x => x.Value)
            .Concat(mappableProperties).Where(x => !x.MemberMapInfo.IsIgnored);
        MemberProfiles = [.. memberProfiles];
    }

    private static LambdaExpression BuildMemberProfile(ParameterExpression parameter, PropertyInfo propertyInfo)
    {
        var prop = Expression.Property(parameter, propertyInfo);
        return Expression.Lambda(prop, parameter);
    }
}