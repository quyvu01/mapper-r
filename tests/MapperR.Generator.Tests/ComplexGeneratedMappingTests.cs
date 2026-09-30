using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using MapperR.Core.Implementations;
using MapperR.Generator.Tests.Models.Organisation;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MapperR.Generator.Tests;

/// <summary>
/// The organisation graph of <c>ComplexMappingTests</c> (MapperR.Core.Tests) run through <c>maprgen</c>: every pair
/// must be generated, and the generated mappers must produce the same graph as the runtime engine, including
/// which objects are shared (compared with <see cref="ReferenceHandler.Preserve"/>, so <c>$id</c>/<c>$ref</c>
/// positions have to match too).
/// </summary>
public class ComplexGeneratedMappingTests
{
    private static readonly Guid AcmeId = Guid.Parse("7f1c2b8e-3d4a-4e5f-9a6b-1c2d3e4f5a6b");

    private static readonly Action<DelegateProfile> Organisation = p =>
    {
        p.Map<Company, CompanyDto>()
            .ForMember(d => d.DepartmentCount, s => s.Departments == null ? 0 : s.Departments.Count);
        p.Map<Department, DepartmentDto>()
            .ForMember(d => d.Code, s => s.Name.ToUpperInvariant());
        p.Map<Employee, EmployeeDto>()
            .ForMember(d => d.FullName, s => s.FirstName + " " + s.LastName)
            .Ignore(d => d.Password);
        p.Map<Skill, SkillDto>();
        p.Map<Address, AddressDto>();
        p.Map<Country, CountryDto>();
        p.Map<Coordinates, CoordinatesDto>();
    };

    private static readonly JsonSerializerOptions GraphJson = new() { ReferenceHandler = ReferenceHandler.Preserve };

    private static readonly Lazy<(GenerationResult Result, Assembly Assembly)> Generated = new(() =>
    {
        var result = MapperCodeGenerator.Generate(DelegateProfile.BuildRegistry(Organisation), targetAssembly: null,
            "Test.Generated.Organisation");
        var assembly = InMemoryCompiler.CompileAssembly(result.Code,
            typeof(Profile).Assembly, typeof(IServiceCollection).Assembly);
        return (result, assembly);
    });

    private static ServiceProvider BuildGeneratedProvider()
    {
        var services = new ServiceCollection();
        services.AddMapR(_ => { });
        services.AddSingleton<IProfile>(new DelegateProfile(Organisation));
        Generated.Value.Assembly.GetType("Test.Generated.Organisation.MapperRGeneratedExtensions")!
            .GetMethod("AddGeneratedMappers")!.Invoke(null, [services]);
        return services.BuildServiceProvider();
    }

    private static IInternalMapper<TSource, TDestination> Runtime<TSource, TDestination>() =>
        new RegistryMapperResolver(DelegateProfile.BuildRegistry(Organisation)).Get<TSource, TDestination>();

    private static string Graph<T>(T value) => JsonSerializer.Serialize(value, GraphJson);

    private static Company CreateAcme()
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
            HireDate = new DateOnly(2005, 6, 7), Salary = null, Level = EmployeeLevel.Senior, Manager = ceo,
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
            Id = AcmeId, Name = "Acme", Founded = new DateTime(2001, 2, 3, 4, 5, 6), Rating = 4.5m,
            Status = CompanyStatus.Active, Tags = ["b2b", "saas"], Headquarters = headquarters, Ceo = ceo
        };
        var board = new Department
            { Name = "Board", Budget = null, Manager = ceo, Employees = [ceo], Company = company };
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

    private static CompanyDto ExistingDestination() => new()
    {
        Name = "old",
        Headquarters = new AddressDto { Street = "old", Country = new CountryDto { Code = "old" } },
        Ceo = new EmployeeDto { FullName = "old", Password = "kept-hash" },
        Departments = [new DepartmentDto { Name = "old" }]
    };

    [Fact]
    public void Every_pair_of_the_graph_is_generated()
    {
        var (result, _) = Generated.Value;

        result.Skipped.ShouldBeEmpty();
        result.GeneratedCount.ShouldBe(7);
    }

    [Fact]
    public void Nested_pairs_call_each_other_directly_instead_of_going_through_the_runtime()
    {
        var code = Generated.Value.Result.Code;

        code.ShouldNotContain("MapperRuntime.");
        code.ShouldContain("Company_To_CompanyDto_Mapper.MapNested");      // Department.Company back reference
        code.ShouldContain("Employee_To_EmployeeDto_Mapper.MapNested");    // Employee.Manager self reference
        code.ShouldContain("Coordinates_To_CoordinatesDto_Mapper.MapNested");
    }

    [Fact]
    public void Generated_mappers_replace_the_runtime_ones_for_every_pair()
    {
        using var provider = BuildGeneratedProvider();
        var generatedAssembly = Generated.Value.Assembly;

        provider.GetRequiredService<IInternalMapper<Company, CompanyDto>>().GetType().Assembly.ShouldBe(generatedAssembly);
        provider.GetRequiredService<IInternalMapper<Department, DepartmentDto>>().GetType().Assembly.ShouldBe(generatedAssembly);
        provider.GetRequiredService<IInternalMapper<Employee, EmployeeDto>>().GetType().Assembly.ShouldBe(generatedAssembly);
        provider.GetRequiredService<IInternalMapper<Coordinates, CoordinatesDto>>().GetType().Assembly.ShouldBe(generatedAssembly);
    }

    [Fact]
    public void Generated_and_runtime_produce_the_same_graph()
    {
        using var provider = BuildGeneratedProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var generated = mapper.Map<Company, CompanyDto>(CreateAcme());
        var runtime = Runtime<Company, CompanyDto>().Map(CreateAcme());

        Graph(generated).ShouldBe(Graph(runtime));
        Graph(mapper.Map<CompanyDto>((object)CreateAcme())).ShouldBe(Graph(runtime));
    }

    [Fact]
    public void Generated_mappers_keep_back_references_and_shared_employees()
    {
        using var provider = BuildGeneratedProvider();

        var dto = provider.GetRequiredService<IMapper>().Map<Company, CompanyDto>(CreateAcme());
        var board = dto.Departments[0];
        var engineering = dto.Departments[1];
        var cto = engineering.Manager;

        dto.Departments.ShouldAllBe(department => ReferenceEquals(department.Company, dto));
        board.Manager.ShouldBeSameAs(dto.Ceo);
        board.Employees[0].ShouldBeSameAs(dto.Ceo);
        cto.Manager.ShouldBeSameAs(dto.Ceo);
        dto.Ceo.Reports.Single().ShouldBeSameAs(cto);
        cto.Reports.ShouldBe([engineering.Employees[1], engineering.Employees[2]]);
        engineering.Employees[3].ShouldBeNull();
        dto.Ceo.Password.ShouldBeNull();
        dto.Headquarters.Location.Latitude.ShouldBe(21.02m);
        dto.Founded.ShouldBe("02/03/2001 04:05:06");
    }

    [Fact]
    public void Generated_and_runtime_agree_on_an_empty_source()
    {
        using var provider = BuildGeneratedProvider();

        var generated = provider.GetRequiredService<IMapper>().Map<Company, CompanyDto>(new Company());

        Graph(generated).ShouldBe(Graph(Runtime<Company, CompanyDto>().Map(new Company())));
    }

    [Fact]
    public void Generated_and_runtime_agree_on_an_update_of_an_existing_graph()
    {
        using var provider = BuildGeneratedProvider();
        var generatedDestination = ExistingDestination();
        var generatedCeo = generatedDestination.Ceo;
        var generatedHeadquarters = generatedDestination.Headquarters;
        var runtimeDestination = ExistingDestination();

        provider.GetRequiredService<IMapper>().Map(CreateAcme(), generatedDestination).ShouldBeSameAs(generatedDestination);
        Runtime<Company, CompanyDto>().Map(CreateAcme(), runtimeDestination);

        Graph(generatedDestination).ShouldBe(Graph(runtimeDestination));
        generatedDestination.Ceo.ShouldBeSameAs(generatedCeo);
        generatedDestination.Ceo.Password.ShouldBe("kept-hash");
        generatedDestination.Headquarters.ShouldBeSameAs(generatedHeadquarters);
        generatedDestination.Departments[0].Manager.ShouldBeSameAs(generatedCeo);
    }

    [Fact]
    public void Generated_code_has_no_depth_guard_yet()
    {
        // Documents DESIGN.md open question 15: the runtime throws beyond MappingContext.MaxDepth, generated code
        // calls the next mapper directly and keeps going. Flip this test when generated code counts depth.
        using var provider = BuildGeneratedProvider();
        var chain = new Employee { Id = 0 };
        for (var i = 1; i < MappingContext.MaxDepth + 10; i++) chain = new Employee { Id = i, Manager = chain };

        Should.Throw<InvalidOperationException>(() => Runtime<Employee, EmployeeDto>().Map(chain));
        Should.NotThrow(() => provider.GetRequiredService<IMapper>().Map<Employee, EmployeeDto>(chain));
    }
}
