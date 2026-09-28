using Benchmark.Models;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using IMapperRMapper = MapperR.Core.Abstractions.IMapper;

namespace Benchmark;

/// <summary>
/// Configuration + first Map call, repeated from scratch each invocation: profile discovery, expression
/// building/compilation (runtime engine, AutoMapper) versus plain generated code. Runs inside a warm process,
/// so it measures setup work, not process cold start or JIT of the libraries themselves.
/// </summary>
[MemoryDiagnoser]
public class StartupBenchmarks
{
    private readonly Order _order = SampleData.Order();

    [Benchmark(Baseline = true)]
    public OrderDto AutoMapper() => MapperFactory.CreateAutoMapper().Map<Order, OrderDto>(_order);

    [Benchmark]
    public OrderDto MapperR_Runtime()
    {
        using var provider = MapperFactory.CreateMapperRRuntime();
        return provider.GetRequiredService<IMapperRMapper>().Map<Order, OrderDto>(_order);
    }

    [Benchmark]
    public OrderDto MapperR_Generated()
    {
        using var provider = MapperFactory.CreateMapperRGenerated();
        return provider.GetRequiredService<IMapperRMapper>().Map<Order, OrderDto>(_order);
    }
}
