using System.Reflection;

namespace MapperR.Core.Registries;

public interface IMapperConfiguration
{
    IMapperConfiguration AddProfilesFromAssembly(Assembly assembly);
}