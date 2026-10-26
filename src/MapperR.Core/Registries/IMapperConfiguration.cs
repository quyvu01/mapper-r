using System.Reflection;

namespace MapperR.Core.Registries;

public interface IMapperConfiguration
{
    bool AllowNullCollections { set; }
    IMapperConfiguration AddProfilesFromAssembly(Assembly assembly);
}