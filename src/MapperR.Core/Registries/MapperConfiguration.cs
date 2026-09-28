using System.Reflection;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Core.Registries;

internal sealed class MapperConfiguration(IServiceCollection services) : IMapperConfiguration
{
    public IMapperConfiguration AddProfilesFromAssembly(Assembly assembly)
    {
        var profileTypes = assembly.DefinedTypes
            .Where(x => typeof(Profile).IsAssignableFrom(x) && x.IsClosedConcreteType());
        profileTypes.ForEach(AddProfile);

        return this;
    }

    private void AddProfile(Type profileType) => services.AddSingleton(typeof(IProfile), profileType);
}