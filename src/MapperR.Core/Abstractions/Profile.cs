using MapperR.Core.Helpers;
using MapperR.Core.Implementations;

namespace MapperR.Core.Abstractions;

public abstract class Profile : IProfile
{
    public abstract void AddProfiles();
    private readonly List<IWireProfile> _wireProfiles = [];
    internal IReadOnlyCollection<IWireProfile> WireProfiles => _wireProfiles;

    /// <exception cref="InvalidOperationException">
    /// Either type is a collection of the framework (an array, <c>List&lt;T&gt;</c>, ...): collections are mapped
    /// through their element pair, so register <c>CreateMap</c> for the elements instead.
    /// </exception>
    protected IWireProfile<TSource, TDestination> CreateMap<TSource, TDestination>()
    {
        ProfileValidator.ValidateMap(typeof(TSource), typeof(TDestination));
        var profileMap = new WireProfileMap<TSource, TDestination>();
        _wireProfiles.Add(profileMap);
        return profileMap;
    }
}