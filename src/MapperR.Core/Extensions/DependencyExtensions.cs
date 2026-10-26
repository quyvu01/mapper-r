using System.Diagnostics.CodeAnalysis;
using MapperR.Core.Abstractions;
using MapperR.Core.Implementations;
using MapperR.Core.Registries;
using Microsoft.Extensions.DependencyInjection;

namespace MapperR.Core.Extensions;

public static class DependencyExtensions
{
    public static IServiceCollection AddMapperR(this IServiceCollection services,
        [NotNull] Action<IMapperConfiguration> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configuration = new MapperConfiguration(services);
        options.Invoke(configuration);
        configuration.Configure();
        services.AddSingleton<IWireProfileProvider, WireProfileProvider>();
        services.AddSingleton(typeof(IProfileBuilder<,>), typeof(ProfileBuilder<,>));
        services.AddSingleton(typeof(IInternalMapper<,>), typeof(InternalMapper<,>));
        services.AddSingleton<IMapper, Mapper>();
        return services;
    }
}