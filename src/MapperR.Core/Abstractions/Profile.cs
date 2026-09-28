using MapperR.Core.Implementations;

namespace MapperR.Core.Abstractions;

public abstract class Profile : IProfile
{
    public abstract void AddProfiles();
    private readonly List<IWireProfile> _wireProfiles = [];
    internal IReadOnlyCollection<IWireProfile> WireProfiles => _wireProfiles;

    protected IWireProfile<TSource, TDestination> CreateMap<TSource, TDestination>()
    {
        var profileMap = new WireProfileMap<TSource, TDestination>();
        _wireProfiles.Add(profileMap);
        return profileMap;
    }
}