using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using MapperR.Core.Registries;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Core.Implementations;

internal class RegistryProvider
{
    internal WireProfileRegistry ProfileRegistry { get; }

    public RegistryProvider(IServiceProvider serviceProvider)
    {
        ProfileRegistry = new Lazy<WireProfileRegistry>(() =>
        {
            var profiles = serviceProvider.GetServices<IProfile>();
            var wireProfiles = new List<IWireProfile>();
            profiles.ForEach(profile =>
            {
                profile.AddProfiles();
                wireProfiles.AddRange(((Profile)profile).WireProfiles);
            });
            return new WireProfileRegistry(wireProfiles);
        }).Value;
    }
}