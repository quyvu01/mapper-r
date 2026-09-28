using Benchmark.Models;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using IMapperRMapper = MapperR.Core.Abstractions.IMapper;

namespace Benchmark;

/// <summary>Steady-state cost of a single Map call, after everything is configured and warmed up.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class MappingBenchmarks
{
    private Customer _customer = null!;
    private Order _order = null!;
    private AutoMapper.IMapper _autoMapper = null!;
    private ServiceProvider _runtimeProvider = null!;
    private ServiceProvider _generatedProvider = null!;
    private IMapperRMapper _runtime = null!;
    private IMapperRMapper _generated = null!;

    [GlobalSetup]
    public void Setup()
    {
        _customer = SampleData.Customer();
        _order = SampleData.Order();
        _autoMapper = MapperFactory.CreateAutoMapper();
        _runtimeProvider = MapperFactory.CreateMapperRRuntime();
        _generatedProvider = MapperFactory.CreateMapperRGenerated();
        _runtime = _runtimeProvider.GetRequiredService<IMapperRMapper>();
        _generated = _generatedProvider.GetRequiredService<IMapperRMapper>();

        MapperFactory.EnsureComparable(_autoMapper, _runtimeProvider, _generatedProvider);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _runtimeProvider.Dispose();
        _generatedProvider.Dispose();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Flat")]
    public CustomerDto Flat_Manual() => ManualMapper.Map(_customer);

    [Benchmark, BenchmarkCategory("Flat")]
    public CustomerDto Flat_AutoMapper() => _autoMapper.Map<Customer, CustomerDto>(_customer);

    [Benchmark, BenchmarkCategory("Flat")]
    public CustomerDto Flat_MapperR_Runtime() => _runtime.Map<Customer, CustomerDto>(_customer);

    [Benchmark, BenchmarkCategory("Flat")]
    public CustomerDto Flat_MapperR_Generated() => _generated.Map<Customer, CustomerDto>(_customer);

    [Benchmark(Baseline = true), BenchmarkCategory("Nested+Collection")]
    public OrderDto Complex_Manual() => ManualMapper.Map(_order);

    [Benchmark, BenchmarkCategory("Nested+Collection")]
    public OrderDto Complex_AutoMapper() => _autoMapper.Map<Order, OrderDto>(_order);

    [Benchmark, BenchmarkCategory("Nested+Collection")]
    public OrderDto Complex_MapperR_Runtime() => _runtime.Map<Order, OrderDto>(_order);

    [Benchmark, BenchmarkCategory("Nested+Collection")]
    public OrderDto Complex_MapperR_Generated() => _generated.Map<Order, OrderDto>(_order);
}
