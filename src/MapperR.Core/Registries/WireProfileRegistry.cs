using MapperR.Core.Abstractions;
using MapperR.Core.Helpers;

namespace MapperR.Core.Registries;

/// <summary>
/// Holds all discovered <see cref="IWireProfile"/> instances, indexed by their (Source, Destination)
/// type pair, and tracks the dependency graph between them — a profile depends on another profile
/// when one of its members maps a complex type that itself has a registered type pair.
/// </summary>
internal sealed class WireProfileRegistry
{
    private readonly Dictionary<(Type, Type), IWireProfile> _profilesByTypePair;
    private readonly Lazy<HashSet<IWireProfile>> _cyclicProfiles;
    private readonly Lazy<HashSet<IWireProfile>> _reachesCycle;

    public WireProfileRegistry(IEnumerable<IWireProfile> wireProfiles)
    {
        var profiles = wireProfiles as IWireProfile[] ?? [.. wireProfiles];
        _profilesByTypePair = profiles.GroupBy(p => (p.SourceType, p.DestinationType))
            .ToDictionary(kv => (kv.Key.SourceType, kv.Key.DestinationType),
                kv => kv.Last());
        _cyclicProfiles = new Lazy<HashSet<IWireProfile>>(FindCyclicProfiles);
        _reachesCycle = new Lazy<HashSet<IWireProfile>>(FindProfilesReachingCycles);
    }

    public IReadOnlyCollection<IWireProfile> Profiles => _profilesByTypePair.Values;

    public IWireProfile Find(Type source, Type destination) =>
        _profilesByTypePair.GetValueOrDefault((source, destination));

    public IReadOnlyCollection<IWireProfile> GetDependenciesOf(IWireProfile profile) =>
        [.. ComputeDependenciesOf(profile)];

    /// <summary>
    /// Whether mapping this pair must remember already-mapped objects: only when an object of the pair can reach
    /// another object of the same pair through nested members (a cycle between types, including a self
    /// reference). Cycles between types are fine — nested members are resolved at runtime — but a cycle in the
    /// data (<c>a.Child.Child == a</c>) would recurse forever without tracking. Value types cannot form such
    /// cycles and are never tracked. A missed cycle is still safe: <see cref="MappingContext.MaxDepth"/> stops it.
    /// </summary>
    public bool TracksReferences(IWireProfile profile) =>
        !profile.SourceType.IsValueType && !profile.DestinationType.IsValueType &&
        _cyclicProfiles.Value.Contains(profile);

    /// <summary>Whether the pair is part of a type-level cycle (value types included).</summary>
    public bool IsCyclic(IWireProfile profile) => _cyclicProfiles.Value.Contains(profile);

    /// <summary>
    /// Whether mapping this pair can reach a pair that is part of a cycle (or is one). A pair that cannot needs
    /// neither reference tracking nor a depth guard anywhere below it.
    /// </summary>
    public bool ReachesCycle(IWireProfile profile) => _reachesCycle.Value.Contains(profile);

    private IWireProfile[] ComputeDependenciesOf(IWireProfile profile) =>
    [
        .. profile.MemberProfiles
            .Select(member =>
                _profilesByTypePair.GetValueOrDefault((member.SourceMemberType, member.DestinationMemberType)))
            .Where(dependency => dependency is not null && dependency != profile)
            .Distinct()
    ];

    /// <summary>
    /// Edges of the dependency graph, using the same classification the expression builder uses to emit nested
    /// calls (a nested object, or a collection whose elements are a nested pair).
    /// </summary>
    private Dictionary<IWireProfile, IWireProfile[]> BuildEdges() => _profilesByTypePair.Values
        .ToDictionary(profile => profile, profile => profile.MemberProfiles
            .Select(member => MemberClassifier
                .Classify(member.SourceMemberType, member.DestinationMemberType, Find).NestedProfile)
            .Where(nested => nested is not null)
            .Distinct()
            .ToArray());

    /// <summary>Profiles that can reach themselves.</summary>
    private HashSet<IWireProfile> FindCyclicProfiles()
    {
        var edges = BuildEdges();
        var cyclic = new HashSet<IWireProfile>();
        foreach (var start in edges.Keys)
        {
            var seen = new HashSet<IWireProfile>();
            var pending = new Stack<IWireProfile>(edges[start]);
            while (pending.TryPop(out var current))
            {
                if (current == start)
                {
                    cyclic.Add(start);
                    break;
                }

                if (!seen.Add(current)) continue;
                foreach (var next in edges[current]) pending.Push(next);
            }
        }

        return cyclic;
    }

    /// <summary>Profiles from which a cyclic profile (or the profile itself, if cyclic) can be reached.</summary>
    private HashSet<IWireProfile> FindProfilesReachingCycles()
    {
        var cyclic = _cyclicProfiles.Value;
        var edges = BuildEdges();
        var reaching = new HashSet<IWireProfile>();
        foreach (var start in edges.Keys)
        {
            var seen = new HashSet<IWireProfile> { start };
            var pending = new Stack<IWireProfile>([start]);
            while (pending.TryPop(out var current))
            {
                if (cyclic.Contains(current))
                {
                    reaching.Add(start);
                    break;
                }

                foreach (var next in edges[current])
                    if (seen.Add(next))
                        pending.Push(next);
            }
        }

        return reaching;
    }
}
