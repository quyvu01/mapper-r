#nullable disable
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Benchmark.Models.Organisation;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using IMapperRMapper = MapperR.Core.Abstractions.IMapper;

namespace Benchmark;

public sealed class OrganisationProfile : MapperR.Core.Abstractions.Profile
{
    public override void AddProfiles()
    {
        CreateMap<Company, CompanyDto>()
            .ForMember(d => d.DepartmentCount, s => s.Departments == null ? 0 : s.Departments.Count);
        CreateMap<Department, DepartmentDto>()
            .ForMember(d => d.Code, s => s.Name.ToUpperInvariant());
        CreateMap<Employee, EmployeeDto>()
            .ForMember(d => d.FullName, s => s.FirstName + " " + s.LastName)
            .Ignore(d => d.Password);
        CreateMap<Skill, SkillDto>();
        CreateMap<Address, AddressDto>();
        CreateMap<Country, CountryDto>();
        CreateMap<Coordinates, CoordinatesDto>();
    }
}

public static class OrganisationSetup
{
    /// <summary>
    /// Configured to do the same work as MapperR: invariant-culture text (AutoMapper's default is the current
    /// culture), same computed and ignored members. Null collections need no setting: both default to mapping a
    /// null collection to an empty one. Circular maps: AutoMapper turns on reference preservation by itself.
    /// </summary>
    public static AutoMapper.IMapper CreateAutoMapper() =>
        new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Company, CompanyDto>()
                .ForMember(d => d.Founded, o => o.MapFrom(s => s.Founded.ToString(null, CultureInfo.InvariantCulture)))
                .ForMember(d => d.DepartmentCount, o => o.MapFrom(s => s.Departments == null ? 0 : s.Departments.Count));
            cfg.CreateMap<Department, DepartmentDto>()
                .ForMember(d => d.Code, o => o.MapFrom(s => s.Name.ToUpperInvariant()));
            cfg.CreateMap<Employee, EmployeeDto>()
                .ForMember(d => d.FullName, o => o.MapFrom(s => s.FirstName + " " + s.LastName))
                .ForMember(d => d.HireDate, o => o.MapFrom(s => s.HireDate.ToString(null, CultureInfo.InvariantCulture)))
                .ForMember(d => d.Password, o => o.Ignore());
            cfg.CreateMap<Skill, SkillDto>();
            cfg.CreateMap<Address, AddressDto>();
            cfg.CreateMap<Country, CountryDto>();
            cfg.CreateMap<Coordinates, CoordinatesDto>();
        }).CreateMapper();

    private static readonly JsonSerializerOptions GraphJson = new() { ReferenceHandler = ReferenceHandler.Preserve };

    /// <summary>The CEO/CTO/two-developer graph of the test suites, including nulls and shared references.</summary>
    public static Company Small()
    {
        var vietnam = new Country { Code = "VN", Name = "Viet Nam" };
        var headquarters = new Address
        {
            Street = "1 Trang Tien", City = "Hanoi", Country = vietnam,
            Location = new Coordinates { Latitude = 21.02, Longitude = 105.85 }
        };
        var ceo = new Employee
        {
            Id = 1, FirstName = "Lan", LastName = "Nguyen", Email = "lan@acme.test", Password = "secret-1",
            Age = 50, HireDate = new DateOnly(2001, 2, 3), Salary = 1234.5m, Level = EmployeeLevel.Executive,
            Address = headquarters, Skills = [new Skill { Name = "Leadership", Years = 20 }]
        };
        var cto = new Employee
        {
            Id = 2, FirstName = "Minh", LastName = "Tran", Password = "secret-2", Age = 40,
            HireDate = new DateOnly(2005, 6, 7), Level = EmployeeLevel.Senior, Manager = ceo,
            Address = new Address { City = "Da Nang", Country = vietnam },
            Skills = [new Skill { Name = "C#", Years = 15 }, new Skill { Name = "SQL", Years = 12 }]
        };
        var developer1 = new Employee
            { Id = 3, FirstName = "An", LastName = "Le", Age = 25, Level = EmployeeLevel.Junior, Manager = cto };
        var developer2 = new Employee
            { Id = 4, FirstName = "Binh", LastName = "Pham", Age = 27, Level = EmployeeLevel.Junior, Manager = cto };
        ceo.Reports = [cto];
        cto.Reports = new List<Employee> { developer1, developer2 };

        var company = new Company
        {
            Id = Guid.Parse("7f1c2b8e-3d4a-4e5f-9a6b-1c2d3e4f5a6b"), Name = "Acme",
            Founded = new DateTime(2001, 2, 3, 4, 5, 6), Rating = 4.5m, Status = CompanyStatus.Active,
            Tags = ["b2b", "saas"], Headquarters = headquarters, Ceo = ceo
        };
        var board = new Department { Name = "Board", Manager = ceo, Employees = [ceo], Company = company };
        var engineering = new Department
        {
            Name = "Engineering", Budget = 500_000, Manager = cto,
            Employees = [cto, developer1, developer2, null], Company = company
        };
        company.Departments = [board, engineering];
        ceo.Department = board;
        cto.Department = developer1.Department = developer2.Department = engineering;
        return company;
    }

    /// <summary>
    /// A CEO plus <paramref name="departments"/> departments of one manager (reporting to the CEO) and
    /// <paramref name="employeesPerDepartment"/> employees each (reporting to that manager). Every employee has
    /// their own address and two skills; all addresses share one country.
    /// </summary>
    public static Company Large(int departments = 10, int employeesPerDepartment = 20)
    {
        var vietnam = new Country { Code = "VN", Name = "Viet Nam" };
        var nextId = 1;
        Employee NewEmployee(EmployeeLevel level, Employee manager) => new()
        {
            Id = nextId, FirstName = $"First{nextId}", LastName = $"Last{nextId}", Email = $"e{nextId}@acme.test",
            Password = "secret", Age = 20 + nextId % 40, HireDate = new DateOnly(2010, 1, 1).AddDays(nextId++),
            Salary = 1000m + nextId, Level = level, Manager = manager,
            Address = new Address
            {
                Street = $"{nextId} Street", City = "Hanoi", Country = vietnam,
                Location = new Coordinates { Latitude = 21 + nextId / 1000.0, Longitude = 105 }
            },
            Skills = [new Skill { Name = "C#", Years = nextId % 10 }, new Skill { Name = "SQL", Years = nextId % 7 }]
        };

        var ceo = NewEmployee(EmployeeLevel.Executive, null);
        var company = new Company
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555555555"), Name = "Large Corp",
            Founded = new DateTime(1999, 9, 9), Rating = 4.1m, Status = CompanyStatus.Active,
            Tags = ["enterprise", "global", "b2b"], Headquarters = ceo.Address, Ceo = ceo, Departments = []
        };
        var board = new Department { Name = "Board", Budget = 1_000_000, Manager = ceo, Employees = [ceo], Company = company };
        ceo.Department = board;
        company.Departments.Add(board);

        var ceoReports = new List<Employee>();
        for (var d = 0; d < departments; d++)
        {
            var manager = NewEmployee(EmployeeLevel.Senior, ceo);
            var members = Enumerable.Range(0, employeesPerDepartment)
                .Select(_ => NewEmployee(EmployeeLevel.Junior, manager)).ToList();
            manager.Reports = members;
            var department = new Department
            {
                Name = $"Department {d}", Budget = 100_000 * (d + 1), Manager = manager,
                Employees = [manager, .. members], Company = company
            };
            foreach (var employee in department.Employees) employee.Department = department;
            company.Departments.Add(department);
            ceoReports.Add(manager);
        }

        ceo.Reports = ceoReports;
        return company;
    }

    /// <summary>What an update targets: an existing DTO graph with nested objects and an ignored value.</summary>
    public static CompanyDto ExistingDestination() => new()
    {
        Name = "old",
        Headquarters = new AddressDto { Street = "old", Country = new CountryDto { Code = "old" } },
        Ceo = new EmployeeDto { FullName = "old", Password = "kept-hash" },
        Departments = [new DepartmentDto { Name = "old" }]
    };

    /// <summary>
    /// Fails fast unless AutoMapper, MapperR runtime and MapperR generated build the same graph, including which
    /// objects are shared, and unless the generated provider really uses the maprgen classes.
    /// </summary>
    public static void EnsureComparable(AutoMapper.IMapper autoMapper, ServiceProvider runtime, ServiceProvider generated)
    {
        var runtimeType = runtime.GetRequiredService<IInternalMapper<Company, CompanyDto>>().GetType();
        var generatedType = generated.GetRequiredService<IInternalMapper<Company, CompanyDto>>().GetType();
        if (generatedType == runtimeType)
            throw new InvalidOperationException(
                "Company -> CompanyDto is not generated; run `maprgen generate` for the Benchmark assembly and rebuild");

        var runtimeMapper = runtime.GetRequiredService<IMapperRMapper>();
        var generatedMapper = generated.GetRequiredService<IMapperRMapper>();
        foreach (var (scenario, create) in new (string, Func<Company>)[] { ("Small", Small), ("Large", () => Large()) })
        {
            EnsureSame($"Map {scenario}",
                autoMapper.Map<Company, CompanyDto>(create()),
                runtimeMapper.Map<Company, CompanyDto>(create()),
                generatedMapper.Map<Company, CompanyDto>(create()));
            EnsureSame($"Update {scenario}",
                autoMapper.Map(create(), ExistingDestination()),
                runtimeMapper.Map(create(), ExistingDestination()),
                generatedMapper.Map(create(), ExistingDestination()));
        }
    }

    private static void EnsureSame(string scenario, params CompanyDto[] results)
    {
        var graphs = results.Select(result => JsonSerializer.Serialize(result, GraphJson)).ToArray();
        if (graphs.Distinct().Count() != 1)
            throw new InvalidOperationException(
                $"{scenario}: mappers disagree, benchmark results would be meaningless.{Environment.NewLine}" +
                string.Join(Environment.NewLine + Environment.NewLine, graphs));
    }
}

/// <summary>
/// Steady-state cost of mapping the organisation graph (cycles, shared references, nested value types, several
/// collection shapes, computed/ignored members). No hand-written baseline: a correct manual version would need its
/// own reference tracking.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class OrganisationBenchmarks
{
    private Company _small;
    private Company _large;
    private AutoMapper.IMapper _autoMapper;
    private ServiceProvider _runtimeProvider;
    private ServiceProvider _generatedProvider;
    private IMapperRMapper _runtime;
    private IMapperRMapper _generated;

    [GlobalSetup]
    public void Setup()
    {
        _small = OrganisationSetup.Small();
        _large = OrganisationSetup.Large();
        _autoMapper = OrganisationSetup.CreateAutoMapper();
        _runtimeProvider = MapperFactory.CreateMapperRRuntime();
        _generatedProvider = MapperFactory.CreateMapperRGenerated();
        _runtime = _runtimeProvider.GetRequiredService<IMapperRMapper>();
        _generated = _generatedProvider.GetRequiredService<IMapperRMapper>();

        OrganisationSetup.EnsureComparable(_autoMapper, _runtimeProvider, _generatedProvider);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _runtimeProvider.Dispose();
        _generatedProvider.Dispose();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Map small (4 employees)")]
    public CompanyDto MapSmall_AutoMapper() => _autoMapper.Map<Company, CompanyDto>(_small);

    [Benchmark, BenchmarkCategory("Map small (4 employees)")]
    public CompanyDto MapSmall_MapperR_Runtime() => _runtime.Map<Company, CompanyDto>(_small);

    [Benchmark, BenchmarkCategory("Map small (4 employees)")]
    public CompanyDto MapSmall_MapperR_Generated() => _generated.Map<Company, CompanyDto>(_small);

    [Benchmark(Baseline = true), BenchmarkCategory("Map large (211 employees)")]
    public CompanyDto MapLarge_AutoMapper() => _autoMapper.Map<Company, CompanyDto>(_large);

    [Benchmark, BenchmarkCategory("Map large (211 employees)")]
    public CompanyDto MapLarge_MapperR_Runtime() => _runtime.Map<Company, CompanyDto>(_large);

    [Benchmark, BenchmarkCategory("Map large (211 employees)")]
    public CompanyDto MapLarge_MapperR_Generated() => _generated.Map<Company, CompanyDto>(_large);

    // A fresh destination per call: updating the same one repeatedly would measure a different (already-filled) case.
    [Benchmark(Baseline = true), BenchmarkCategory("Update small")]
    public CompanyDto UpdateSmall_AutoMapper() => _autoMapper.Map(_small, OrganisationSetup.ExistingDestination());

    [Benchmark, BenchmarkCategory("Update small")]
    public CompanyDto UpdateSmall_MapperR_Runtime() => _runtime.Map(_small, OrganisationSetup.ExistingDestination());

    [Benchmark, BenchmarkCategory("Update small")]
    public CompanyDto UpdateSmall_MapperR_Generated() => _generated.Map(_small, OrganisationSetup.ExistingDestination());
}
