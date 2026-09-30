using System.Diagnostics.CodeAnalysis;
using MapperR.Core.Abstractions;
using MapperR.Core.Implementations;
using MapperR.Core.Registries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MapperR.Core.Extensions;

public static class DependencyExtensions
{
    public static IServiceCollection AddMapR(this IServiceCollection services,
        [NotNull] Action<IMapperConfiguration> options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var configuration = new MapperConfiguration(services);
        options.Invoke(configuration);
        services.AddSingleton(new MapperSettings(configuration.AllowNullCollections));
        services.AddSingleton<IMapper, Mapper>();
        services.AddSingleton(typeof(IInternalMapper<,>), typeof(InternalMapper<,>));
        services.AddSingleton<RegistryProvider>();
        services.AddSingleton<IMapperResolver, MapperResolver>();
        // A factory, not an instance: TryAddSingleton(instance) only accepts reference types.
        services.TryAdd(ServiceDescriptor.Singleton(typeof(MapperOptimizations),
            _ => (object)MapperOptimizations.Default));
        return services;
    }
}