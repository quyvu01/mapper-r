using System.Reflection;

namespace MapperR.Core.Registries;

public interface IMapperConfiguration
{
    /// <summary>
    /// What a <c>null</c> source collection maps to. <c>false</c> (the default, as in AutoMapper): an empty
    /// collection of the destination type — an empty array, list or set, never <c>null</c>. <c>true</c>: the
    /// destination collection stays <c>null</c>.
    /// </summary>
    bool AllowNullCollections { get; set; }

    IMapperConfiguration AddProfilesFromAssembly(Assembly assembly);
}