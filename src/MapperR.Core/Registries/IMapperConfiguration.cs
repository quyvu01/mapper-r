using System.Reflection;

namespace MapperR.Core.Registries;

public interface IMapperConfiguration
{
    bool AllowNullCollections { get; set; }
    IMapperConfiguration AddProfilesFromAssembly(Assembly assembly);
}