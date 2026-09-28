using MapperR.Core.Abstractions;

namespace MapperR.Core.Registries;

/// <summary>
/// Holds all discovered <see cref="IWireProfile"/> instances, indexed by their (Source, Destination)
/// type pair, and tracks the dependency graph between them — a profile depends on another profile
/// when one of its members maps a complex type that itself has a registered type pair.
/// </summary>
internal sealed class WireProfileRegistry
{
    private readonly Dictionary<(Type Source, Type Destination), IWireProfile> _profilesByTypePair;
    private readonly Dictionary<IWireProfile, IWireProfile[]> _dependenciesByProfile;

    public WireProfileRegistry(IEnumerable<IWireProfile> wireProfiles)
    {
        var profiles = wireProfiles as IWireProfile[] ?? [.. wireProfiles];
        _profilesByTypePair = profiles.ToDictionary(profile => (profile.SourceType, profile.DestinationType));
        _dependenciesByProfile = profiles.ToDictionary(profile => profile, ComputeDependenciesOf);

        EnsureNoCycles();
    }

    public IReadOnlyCollection<IWireProfile> Profiles => _profilesByTypePair.Values;

    public IWireProfile Find(Type source, Type destination) =>
        _profilesByTypePair.GetValueOrDefault((source, destination));

    public IReadOnlyCollection<IWireProfile> GetDependenciesOf(IWireProfile profile) =>
        _dependenciesByProfile.TryGetValue(profile, out var dependencies) ? dependencies : [];

    private IWireProfile[] ComputeDependenciesOf(IWireProfile profile) =>
    [
        .. profile.MemberProfiles
            .Select(member =>
                _profilesByTypePair.GetValueOrDefault((member.SourceMemberType, member.DestinationMemberType)))
            .Where(dependency => dependency is not null && dependency != profile)
            .Distinct()
    ];

    private void EnsureNoCycles()
    {
        var visited = new HashSet<IWireProfile>();
        var path = new List<IWireProfile>();

        foreach (var profile in _dependenciesByProfile.Keys) Visit(profile, visited, path);
    }

    private void Visit(IWireProfile profile, HashSet<IWireProfile> visited, List<IWireProfile> path)
    {
        if (visited.Contains(profile)) return;

        var cycleStart = path.IndexOf(profile);
        if (cycleStart >= 0)
        {
            var cycle = path.Skip(cycleStart).Append(profile).Select(Describe);
            throw new InvalidOperationException(
                $"Circular mapping dependency detected: {string.Join(" -> ", cycle)}");
        }

        path.Add(profile);
        foreach (var dependency in _dependenciesByProfile[profile]) Visit(dependency, visited, path);
        path.RemoveAt(path.Count - 1);
        visited.Add(profile);
    }

    private static string Describe(IWireProfile profile) =>
        $"{profile.SourceType.Name}->{profile.DestinationType.Name}";
}