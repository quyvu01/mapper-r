using System.Reflection;
using System.Runtime.Loader;
using MapperR.Core.Abstractions;
using MapperR.Core.Implementations;
using MapperR.Core.Registries;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Generator;

/// <summary>
/// Loads the user's built assembly (with its dependencies resolved from its .deps.json) in an isolated
/// context, while sharing the generator's own MapperR.Core — otherwise the user's <see cref="Profile"/>
/// subclasses would derive from a different <see cref="Profile"/> type and discovery would find nothing.
/// </summary>
internal sealed class TargetAssemblyLoader(string assemblyPath) : AssemblyLoadContext("maprgen-target")
{
    private static readonly AssemblyName CoreAssemblyName = typeof(Profile).Assembly.GetName();

    private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

    public static Assembly LoadTarget(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath)) throw new GeneratorException($"assembly not found: {fullPath}");

        var assembly = new TargetAssemblyLoader(fullPath).LoadFromAssemblyPath(fullPath);

        // The generator reads MapperR.Core internals, so it only works against the exact version it was built with.
        var referencedCore = assembly.GetReferencedAssemblies().FirstOrDefault(name => name.Name == CoreAssemblyName.Name)
                             ?? throw new GeneratorException($"{assembly.GetName().Name} does not reference {CoreAssemblyName.Name}");
        if (referencedCore.Version != CoreAssemblyName.Version)
            throw new GeneratorException(
                $"{assembly.GetName().Name} references {CoreAssemblyName.Name} {referencedCore.Version}, but this maprgen " +
                $"was built against {CoreAssemblyName.Version}; use the maprgen version matching your MapperR.Core package");

        return assembly;
    }

    /// <summary>Discovers profiles exactly like <c>AddMapR(cfg => cfg.AddProfilesFromAssembly(...))</c> does at runtime.</summary>
    public static WireProfileRegistry BuildRegistry(Assembly assembly)
    {
        var services = new ServiceCollection();
        new MapperConfiguration(services).AddProfilesFromAssembly(assembly);
        services.AddSingleton<RegistryProvider>();

        using var provider = services.BuildServiceProvider();
        try
        {
            return provider.GetRequiredService<RegistryProvider>().ProfileRegistry;
        }
        catch (InvalidOperationException exception)
        {
            throw new GeneratorException(
                $"could not create the profiles in {assembly.GetName().Name} (profiles must be constructible without " +
                $"application services): {exception.Message}");
        }
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == CoreAssemblyName.Name) return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}

internal sealed class GeneratorException(string message) : Exception(message);
