using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using MapperR.Core.Abstractions;
using MapperR.Core.Entities;
using MapperR.Core.Exceptions;
using MapperR.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Core.Implementations;

internal sealed class WireProfileProvider : IWireProfileProvider
{
    // Member -> the profile its value is mapped with (nested object, or the element pair of a collection).
    // Members that need no other profile (simple, assignable) have no entry.
    private readonly Dictionary<IMemberProfile, IWireProfile> _memberDependencies = [];

    // Profile -> the distinct profiles its members use. Direct dependencies only, so cycles (Node.Next, Order <-> Line)
    // are plain data here: nothing recurses. Whoever walks the graph (the expression builder) owns cycle handling.
    private readonly Dictionary<IWireProfile, IWireProfile[]> _profileDependencies = [];

    private readonly ConcurrentDictionary<(Type SourceType, Type DestinationType), IWireProfile> _profileLookup = [];

    public WireProfileProvider(IServiceProvider serviceProvider)
    {
        var profiles = serviceProvider.GetServices<IProfile>();
        WireProfiles =
        [
            .. profiles.Select(p =>
                {
                    if (p is not Profile profile) return [];
                    p.AddProfiles();
                    return profile.WireProfiles;
                }).SelectMany(profile => profile)
                .Select(profile =>
                {
                    if (profile is not IWireProfileBuilder builder) return profile;
                    builder.BuildWireProfiles();
                    return profile;
                })
        ];
        // Only once every profile is built: a profile can depend on one declared after it, or on itself.
        ResolveDependencies();
    }

    public IWireProfile[] WireProfiles { get; }

    /// <summary>The profile <paramref name="member"/> is mapped with, or null when it needs none.</summary>
    public IWireProfile GetDependency(IMemberProfile member) =>
        _memberDependencies.GetValueOrDefault(member);

    /// <summary>The distinct profiles used directly by the members of <paramref name="profile"/>.</summary>
    public IReadOnlyList<IWireProfile> GetDependencies(IWireProfile profile) =>
        _profileDependencies.GetValueOrDefault(profile) ?? [];

    public IWireProfile Resolve<TSource, TDestination>() => Resolve(typeof(TSource), typeof(TDestination));

    public IWireProfile Resolve(Type sourceType, Type destinationType)
    {
        var wireProfile = _profileLookup.GetOrAdd((sourceType, destinationType), lookup =>
        {
            // 1. A profile registered for exactly this pair. The last registration wins.
            var profile = WireProfiles
                .LastOrDefault(x => x.SourceType == lookup.SourceType && x.DestinationType == lookup.DestinationType);
            if (profile is not null) return profile;

            // 2. Both sides are collections -> resolve through the element pair.
            var elementProfile = ResolveThroughElements(lookup.SourceType, lookup.DestinationType);
            if (elementProfile is not null) return elementProfile;

            throw MapperException.ProfileNotFound(lookup.SourceType, lookup.DestinationType);
        });
        return wireProfile;
    }

    private void ResolveDependencies()
    {
        // Same rule as the resolver: when a pair is registered twice, the last one wins.
        var byPair = new Dictionary<(Type Source, Type Destination), IWireProfile>();
        foreach (var profile in WireProfiles) byPair[(profile.SourceType, profile.DestinationType)] = profile;

        foreach (var profile in WireProfiles)
        {
            var dependencies = new List<IWireProfile>();
            foreach (var member in profile.MemberProfiles)
            {
                // Simple members need no profile, Invalid ones are for validation to report.
                if (member.MemberMapInfo.Classifier is not (MapClassifier.Candidate or MapClassifier.Collection))
                    continue;
                var dependency = FindDependency(profile, member, byPair);
                if (dependency is null) continue;
                _memberDependencies[member] = dependency;
                if (!dependencies.Contains(dependency)) dependencies.Add(dependency);
            }

            _profileDependencies[profile] = [.. dependencies];
        }
    }

    private static IWireProfile FindDependency(IWireProfile owner, IMemberProfile member,
        Dictionary<(Type Source, Type Destination), IWireProfile> byPair)
    {
        var destination = Unwrap(member.Selector.GetBodyType());
        var source = Unwrap(member.MemberMapInfo.MemberPath.GetBodyType());

        // A profile for the exact pair always wins (also for a type of your own that derives from a collection).
        // Otherwise peel one collection level off both sides: List<List<A>> -> B[][] ends at A -> B.
        while (true)
        {
            if (byPair.TryGetValue((source, destination), out var found)) return found;
            if (!TryGetElementType(source, out var sourceElement) ||
                !TryGetElementType(destination, out var destinationElement)) break;
            source = Unwrap(sourceElement);
            destination = Unwrap(destinationElement);
        }

        if (!NeedsProfile(source, destination)) return null;
        throw MapperException.MissingProfile(owner.SourceType, owner.DestinationType, MemberName(member), source,
            destination);
    }

    /// <summary>
    /// No profile is needed when the value can be assigned as it is (same type, a base type, <c>object</c>) or when
    /// a scalar or string is involved (<c>int</c> -> <c>long</c>, <c>int</c> -> <c>string</c>, <c>string</c> -> <c>int</c>).
    /// </summary>
    private static bool NeedsProfile(Type source, Type destination) =>
        !destination.IsAssignableFrom(source) &&
        !Helpers.GeneralHelpers.IsScalar(source) && !Helpers.GeneralHelpers.IsScalar(destination) &&
        source != typeof(string) && destination != typeof(string);

    private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static string MemberName(IMemberProfile member) =>
        (member.Selector.Body is UnaryExpression { Operand: var operand } ? operand : member.Selector.Body) is
        MemberExpression memberExpression
            ? memberExpression.Member.Name
            : member.Selector.ToString();


    private IWireProfile ResolveThroughElements(Type source, Type destination)
    {
        while (true)
        {
            // Both must be collections: a collection mapped to a single object (or the reverse) is not resolvable.
            if (!TryGetElementType(source, out var sourceElement)) return null;
            if (!TryGetElementType(destination, out var destinationElement)) return null;

            var profile = WireProfiles.LastOrDefault(p =>
                p.SourceType == sourceElement && p.DestinationType == destinationElement);
            if (profile is not null) return profile;

            // Collection of collections: List<List<A>> -> B[][] resolves A -> B.
            source = sourceElement;
            destination = destinationElement;
        }
    }

    /// <summary>
    /// Edge cases to decide:
    /// <list type="number">
    /// <item><c>string</c> is <c>IEnumerable&lt;char&gt;</c>: must NOT count as a collection (excluded below).</item>
    /// <item><c>Dictionary&lt;K,V&gt;</c> is <c>IEnumerable&lt;KeyValuePair&lt;K,V&gt;&gt;</c>: treated as a plain
    /// collection of pairs here, which is probably not what you want. Exclude it or give it its own rule.</item>
    /// <item>Type implementing several <c>IEnumerable&lt;T&gt;</c> (e.g. a class that is both
    /// <c>IEnumerable&lt;A&gt;</c> and <c>IEnumerable&lt;B&gt;</c>): ambiguous, treated as "not a collection" here.</item>
    /// <item>Non-generic <c>IEnumerable</c> / <c>ArrayList</c>: no element type, treated as "not a collection".</item>
    /// <item>Multi-dimensional array (<c>A[,]</c>): <c>GetElementType()</c> works but it is not a sequence of
    /// one-dimensional elements; only rank 1 is accepted.</item>
    /// <item>Interface itself (<c>IEnumerable&lt;A&gt;</c>, <c>IList&lt;A&gt;</c>) has no entry in
    /// <c>GetInterfaces()</c> for itself or for a base interface of the right shape; both are handled below.</item>
    /// <item>Nullable wrappers: <c>List&lt;int&gt;</c> -&gt; <c>List&lt;int?&gt;</c> gives element pair
    /// <c>int -&gt; int?</c>, which has no profile. See the note in <see cref="Resolve"/>.</item>
    /// </list>
    /// </summary>
    private static bool TryGetElementType(Type type, out Type elementType)
    {
        elementType = null!;
        if (type == typeof(string)) return false;

        if (type.IsArray)
        {
            if (type.GetArrayRank() != 1) return false;
            elementType = type.GetElementType()!;
            return true;
        }

        if (!typeof(IEnumerable).IsAssignableFrom(type)) return false;

        // The type itself may already be IEnumerable<T> (or IList<T>, ...): look at it too.
        var enumerables = type.GetInterfaces()
            .Append(type)
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(i => i.GetGenericArguments()[0])
            .Distinct()
            .ToArray();
        if (enumerables.Length != 1) return false;

        elementType = enumerables[0];
        return true;
    }
}