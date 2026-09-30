#nullable disable
using Benchmark.Models;
using Benchmark.Models.Organisation;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using MapperR.Core.Abstractions;
using MapperR.Core.Implementations;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmark;

/// <summary>
/// One table for the runtime engine's optimization steps, cumulative: S0 is the plain builder output, each next
/// row turns one more <see cref="MapperOptimizations"/> flag on. Every MapperR row calls the internal mapper
/// directly (no <c>IMapper</c> in front), so rows differ only in the steps, and the generated and AutoMapper rows
/// are measured the same way. Compare a row with the one above it for the effect of that step alone.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class OptimizationLadderBenchmarks
{
    private const string OrderGroup = "Order + 10 lines (no cycles)";
    private const string SmallGroup = "Organisation small (4 employees)";
    private const string LargeGroup = "Organisation large (211 employees)";

    private Order _order;
    private Company _small;
    private Company _large;
    private AutoMapper.IMapper _autoMapper;
    private AutoMapper.IMapper _autoMapperOrder;
    private ServiceProvider _runtimeProvider;
    private ServiceProvider _generatedProvider;
    private List<ServiceProvider> _providers;

    private IInternalMapper<Order, OrderDto> _order0, _order1, _order12, _order123, _orderGenerated;
    private IInternalMapper<Company, CompanyDto> _org0, _org1, _org12, _org123, _orgGenerated;

    [GlobalSetup]
    public void Setup()
    {
        _order = SampleData.Order();
        _small = OrganisationSetup.Small();
        _large = OrganisationSetup.Large();
        _autoMapper = OrganisationSetup.CreateAutoMapper();
        _autoMapperOrder = MapperFactory.CreateAutoMapper();
        _runtimeProvider = MapperFactory.CreateMapperRRuntime();
        _generatedProvider = MapperFactory.CreateMapperRGenerated();

        const MapperOptimizations s1 = MapperOptimizations.HoistCollectionLambdas;
        const MapperOptimizations s12 = s1 | MapperOptimizations.SharedContextWhenAcyclic;
        const MapperOptimizations s123 = s12 | MapperOptimizations.InlineAcyclicPairs;

        // Every step gets its own service provider, so nested pairs resolve through the real MapperResolver
        // (the per-pair slot cache), exactly as in an application.
        _providers = [_runtimeProvider, _generatedProvider];
        IInternalMapper<TS, TD> With<TS, TD>(MapperOptimizations optimizations)
        {
            var provider = MapperFactory.CreateMapperRRuntime(optimizations);
            _providers.Add(provider);
            return provider.GetRequiredService<IInternalMapper<TS, TD>>();
        }

        (_order0, _order1, _order12, _order123) =
            (With<Order, OrderDto>(MapperOptimizations.None), With<Order, OrderDto>(s1),
                With<Order, OrderDto>(s12), With<Order, OrderDto>(s123));
        (_org0, _org1, _org12, _org123) =
            (With<Company, CompanyDto>(MapperOptimizations.None), With<Company, CompanyDto>(s1),
                With<Company, CompanyDto>(s12), With<Company, CompanyDto>(s123));
        _orderGenerated = _generatedProvider.GetRequiredService<IInternalMapper<Order, OrderDto>>();
        _orgGenerated = _generatedProvider.GetRequiredService<IInternalMapper<Company, CompanyDto>>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var provider in _providers) provider.Dispose();
    }

    [Benchmark(Baseline = true), BenchmarkCategory(OrderGroup)]
    public OrderDto Order_S0_Plain() => _order0.Map(_order);

    [Benchmark, BenchmarkCategory(OrderGroup)]
    public OrderDto Order_S1_HoistCollections() => _order1.Map(_order);

    [Benchmark, BenchmarkCategory(OrderGroup)]
    public OrderDto Order_S2_SharedContext() => _order12.Map(_order);

    [Benchmark, BenchmarkCategory(OrderGroup)]
    public OrderDto Order_S3_InlineAcyclic() => _order123.Map(_order);

    [Benchmark, BenchmarkCategory(OrderGroup)]
    public OrderDto Order_Generated() => _orderGenerated.Map(_order);

    [Benchmark, BenchmarkCategory(OrderGroup)]
    public OrderDto Order_AutoMapper() => _autoMapperOrder.Map<Order, OrderDto>(_order);

    [Benchmark(Baseline = true), BenchmarkCategory(SmallGroup)]
    public CompanyDto Small_S0_Plain() => _org0.Map(_small);

    [Benchmark, BenchmarkCategory(SmallGroup)]
    public CompanyDto Small_S1_HoistCollections() => _org1.Map(_small);

    [Benchmark, BenchmarkCategory(SmallGroup)]
    public CompanyDto Small_S2_SharedContext() => _org12.Map(_small);

    [Benchmark, BenchmarkCategory(SmallGroup)]
    public CompanyDto Small_S3_InlineAcyclic() => _org123.Map(_small);

    [Benchmark, BenchmarkCategory(SmallGroup)]
    public CompanyDto Small_Generated() => _orgGenerated.Map(_small);

    [Benchmark, BenchmarkCategory(SmallGroup)]
    public CompanyDto Small_AutoMapper() => _autoMapper.Map<Company, CompanyDto>(_small);

    [Benchmark(Baseline = true), BenchmarkCategory(LargeGroup)]
    public CompanyDto Large_S0_Plain() => _org0.Map(_large);

    [Benchmark, BenchmarkCategory(LargeGroup)]
    public CompanyDto Large_S1_HoistCollections() => _org1.Map(_large);

    [Benchmark, BenchmarkCategory(LargeGroup)]
    public CompanyDto Large_S2_SharedContext() => _org12.Map(_large);

    [Benchmark, BenchmarkCategory(LargeGroup)]
    public CompanyDto Large_S3_InlineAcyclic() => _org123.Map(_large);

    [Benchmark, BenchmarkCategory(LargeGroup)]
    public CompanyDto Large_Generated() => _orgGenerated.Map(_large);

    [Benchmark, BenchmarkCategory(LargeGroup)]
    public CompanyDto Large_AutoMapper() => _autoMapper.Map<Company, CompanyDto>(_large);
}
