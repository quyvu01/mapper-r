using MapperR.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Core.Implementations;

internal sealed class WireProfilesProvider : IWireProfilesProvider
{
    public WireProfilesProvider(IServiceProvider serviceProvider)
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
        ];
    }

    public IWireProfile[] WireProfiles { get; }
}