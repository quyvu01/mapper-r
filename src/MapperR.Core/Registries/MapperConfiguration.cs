using System.Reflection;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Core.Registries;

internal sealed class MapperConfiguration(IServiceCollection services) : IMapperConfiguration
{
    public bool AllowNullCollections { get; set; }
    private readonly HashSet<Type> _profileTypes = [];

    public IMapperConfiguration AddProfilesFromAssembly(Assembly assembly)
    {
        var profileTypes = assembly.DefinedTypes
            .Where(x => typeof(Profile).IsAssignableFrom(x) && x.IsClosedConcreteType());
        profileTypes.ForEach(p => _profileTypes.Add(p));
        return this;
    }

    internal void AddProfilesToContainer() => _profileTypes.ForEach(p => services.AddSingleton(typeof(IProfile), p));
}