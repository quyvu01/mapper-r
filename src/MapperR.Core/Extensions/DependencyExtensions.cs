using System.Diagnostics.CodeAnalysis;
using MapperR.Core.Abstractions;
using MapperR.Core.Implementations;
using MapperR.Core.Registries;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<IMapper, Mapper>();
        services.AddSingleton(typeof(IInternalMapper<,>), typeof(InternalMapper<,>));
        services.AddSingleton<RegistryProvider>();
        return services;
    }
}