using System.Text.Json;
using Benchmark.Models;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using IMapperRMapper = MapperR.Core.Abstractions.IMapper;

namespace Benchmark;

public sealed class MapperRBenchmarkProfile : MapperR.Core.Abstractions.Profile
{
    public override void AddProfiles()
    {
        CreateMap<Customer, CustomerDto>();
        CreateMap<Address, AddressDto>();
        CreateMap<OrderLine, OrderLineDto>();
        CreateMap<Order, OrderDto>()
            .ForMember(d => d.Customer, s => s.Customer)
            .ForMember(d => d.ShippingAddress, s => s.ShippingAddress)
            .ForMember(d => d.Lines, s => s.Lines);
    }
}

public static class ManualMapper
{
    public static CustomerDto Map(Customer source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Email = source.Email,
        Age = source.Age,
        IsActive = source.IsActive,
        Balance = source.Balance,
        CreatedAt = source.CreatedAt
    };

    public static OrderDto Map(Order source) => new()
    {
        Id = source.Id,
        PlacedAt = source.PlacedAt,
        Customer = Map(source.Customer),
        ShippingAddress = new AddressDto
        {
            Street = source.ShippingAddress.Street,
            City = source.ShippingAddress.City,
            Country = source.ShippingAddress.Country
        },
        Lines = source.Lines.Select(line => new OrderLineDto
        {
            ProductId = line.ProductId,
            ProductName = line.ProductName,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice
        }).ToList()
    };
}

public static class MapperFactory
{
    public static AutoMapper.IMapper CreateAutoMapper() =>
        new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Customer, CustomerDto>();
            cfg.CreateMap<Address, AddressDto>();
            cfg.CreateMap<OrderLine, OrderLineDto>();
            cfg.CreateMap<Order, OrderDto>();
        }).CreateMapper();

    public static ServiceProvider CreateMapperRRuntime() =>
        new ServiceCollection()
            .AddMapR(cfg => cfg.AddProfilesFromAssembly(typeof(MapperRBenchmarkProfile).Assembly))
            .BuildServiceProvider();

    public static ServiceProvider CreateMapperRGenerated() =>
        new ServiceCollection()
            .AddMapR(cfg => cfg.AddProfilesFromAssembly(typeof(MapperRBenchmarkProfile).Assembly))
            .AddGeneratedMappers()
            .BuildServiceProvider();

    /// <summary>
    /// Fails fast if the benchmark would measure the wrong thing: every mapper must produce the same output,
    /// and the "generated" provider must really resolve the maprgen classes rather than falling back to runtime.
    /// </summary>
    public static void EnsureComparable(AutoMapper.IMapper autoMapper, ServiceProvider runtime, ServiceProvider generated)
    {
        EnsureGeneratedIsUsed<Customer, CustomerDto>(runtime, generated);
        EnsureGeneratedIsUsed<Order, OrderDto>(runtime, generated);

        var customer = SampleData.Customer();
        var order = SampleData.Order();
        var runtimeMapper = runtime.GetRequiredService<IMapperRMapper>();
        var generatedMapper = generated.GetRequiredService<IMapperRMapper>();

        EnsureSame("Customer", ManualMapper.Map(customer),
            autoMapper.Map<Customer, CustomerDto>(customer),
            runtimeMapper.Map<Customer, CustomerDto>(customer),
            generatedMapper.Map<Customer, CustomerDto>(customer));
        EnsureSame("Order", ManualMapper.Map(order),
            autoMapper.Map<Order, OrderDto>(order),
            runtimeMapper.Map<Order, OrderDto>(order),
            generatedMapper.Map<Order, OrderDto>(order));
    }

    private static void EnsureGeneratedIsUsed<TSource, TDestination>(ServiceProvider runtime, ServiceProvider generated)
    {
        var runtimeType = runtime.GetRequiredService<IInternalMapper<TSource, TDestination>>().GetType();
        var generatedType = generated.GetRequiredService<IInternalMapper<TSource, TDestination>>().GetType();
        if (generatedType == runtimeType)
            throw new InvalidOperationException(
                $"{typeof(TSource).Name} -> {typeof(TDestination).Name} is not generated (resolved {generatedType.Name}); " +
                "run `maprgen generate` for the Benchmark assembly and rebuild");
    }

    private static void EnsureSame<T>(string scenario, T expected, params T[] actual)
    {
        var expectedJson = JsonSerializer.Serialize(expected);
        if (actual.Any(result => JsonSerializer.Serialize(result) != expectedJson))
            throw new InvalidOperationException($"{scenario}: mappers disagree, benchmark results would be meaningless");
    }
}
