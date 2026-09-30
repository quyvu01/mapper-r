using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using MapperR.Core.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MapperR.Core.Tests.UnitTests;

/// <summary>
/// End-to-end mapping of one large object graph through DI and <see cref="IMapper"/>: an organisation where
/// Company ↔ Department ↔ Employee reference each other (cycles, self-reference, shared references), with
/// nested value types, several collection shapes, text formatting, ignored members and computed members.
/// </summary>
public class ComplexMappingTests
{
    #region Model

    private enum CompanyStatus
    {
        Active = 1,
        Suspended = 2
    }

    private enum CompanyStatusDto
    {
        Active = 1,
        Suspended = 2
    }

    private enum EmployeeLevel
    {
        Junior = 1,
        Senior = 2,
        Executive = 3
    }

    private struct Coordinates
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    private struct CoordinatesDto
    {
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
    }

    private sealed class Country
    {
        public string Code { get; set; }
        public string Name { get; set; }
    }

    private sealed class CountryDto
    {
        public string Code { get; set; }
        public string Name { get; set; }
    }

    private sealed class Address
    {
        public string Street { get; set; }
        public string City { get; set; }
        public Country Country { get; set; }
        public Coordinates Location { get; set; }
    }

    private sealed class AddressDto
    {
        public string Street { get; set; }
        public string City { get; set; }
        public CountryDto Country { get; set; }
        public CoordinatesDto Location { get; set; }
    }

    private sealed class Skill
    {
        public string Name { get; set; }
        public int Years { get; set; }
    }

    private sealed class SkillDto
    {
        public string Name { get; set; }
        public double Years { get; set; }
    }

    private sealed class Employee
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public int Age { get; set; }
        public DateOnly HireDate { get; set; }
        public decimal? Salary { get; set; }
        public EmployeeLevel Level { get; set; }
        public Address Address { get; set; }
        public List<Skill> Skills { get; set; }
        public Employee Manager { get; set; }
        public IEnumerable<Employee> Reports { get; set; }
        public Department Department { get; set; }
    }

    private sealed class EmployeeDto
    {
        public long Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public long Age { get; set; }
        public string HireDate { get; set; }
        public decimal? Salary { get; set; }
        public int Level { get; set; }
        public AddressDto Address { get; set; }
        public IEnumerable<SkillDto> Skills { get; set; }
        public EmployeeDto Manager { get; set; }
        public List<EmployeeDto> Reports { get; set; }
        public DepartmentDto Department { get; set; }
    }

    private sealed class Department
    {
        public string Name { get; set; }
        public int? Budget { get; set; }
        public Employee Manager { get; set; }
        public List<Employee> Employees { get; set; }
        public Company Company { get; set; }
    }

    private sealed class DepartmentDto
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public long Budget { get; set; }
        public EmployeeDto Manager { get; set; }
        public EmployeeDto[] Employees { get; set; }
        public CompanyDto Company { get; set; }
    }

    private sealed class Company
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public DateTime Founded { get; set; }
        public decimal Rating { get; set; }
        public CompanyStatus Status { get; set; }
        public string[] Tags { get; set; }
        public Address Headquarters { get; set; }
        public Employee Ceo { get; set; }
        public List<Department> Departments { get; set; }
    }

    private sealed class CompanyDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Founded { get; set; }
        public double Rating { get; set; }
        public CompanyStatusDto Status { get; set; }
        public List<string> Tags { get; set; }

        public AddressDto Headquarters { get; set; }

        // Ceo before Departments on purpose: in an update, the first path that reaches an employee decides which
        // object it maps to. With Departments first, the CEO is already mapped to a new object through
        // Departments[0].Manager, and the existing Ceo is replaced instead of updated in place.
        public EmployeeDto Ceo { get; set; }
        public List<DepartmentDto> Departments { get; set; }
        public int DepartmentCount { get; set; }
    }

    private sealed class OrganisationProfile : Profile
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

    #endregion

    private static readonly Guid AcmeId = Guid.Parse("7f1c2b8e-3d4a-4e5f-9a6b-1c2d3e4f5a6b");

    private static (ServiceProvider Provider, IMapper Mapper) BuildMapper()
    {
        var services = new ServiceCollection();
        services.AddMapR(_ => { });
        services.AddSingleton<IProfile>(new OrganisationProfile());
        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<IMapper>());
    }

    /// <summary>
    /// CEO → CTO → two developers; the CEO runs "Board", the CTO runs "Engineering". Every department points back
    /// to the company, every employee to their department; the CEO and the company share one address object.
    /// </summary>
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

    [Fact]
    public void Maps_scalars_text_computed_members_and_nested_value_types()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;

        var dto = mapper.Map<Company, CompanyDto>(CreateAcme());

        dto.Id.ShouldBe(AcmeId);
        dto.Name.ShouldBe("Acme");
        dto.Founded.ShouldBe("02/03/2001 04:05:06");
        dto.Rating.ShouldBe(4.5d);
        dto.Status.ShouldBe(CompanyStatusDto.Active);
        dto.Tags.ShouldBe(["b2b", "saas"]);
        dto.DepartmentCount.ShouldBe(2);

        dto.Headquarters.Street.ShouldBe("1 Trang Tien");
        dto.Headquarters.Country.Code.ShouldBe("VN");
        dto.Headquarters.Location.Latitude.ShouldBe(21.02m);
        dto.Headquarters.Location.Longitude.ShouldBe(105.85m);

        var ceo = dto.Ceo;
        ceo.Id.ShouldBe(1L);
        ceo.FullName.ShouldBe("Lan Nguyen");
        ceo.Email.ShouldBe("lan@acme.test");
        ceo.Age.ShouldBe(50L);
        ceo.HireDate.ShouldBe("02/03/2001");
        ceo.Salary.ShouldBe(1234.5m);
        ceo.Level.ShouldBe((int)EmployeeLevel.Executive);
        ceo.Skills.Single().Name.ShouldBe("Leadership");
        ceo.Skills.Single().Years.ShouldBe(20d);

        dto.Departments.Select(d => d.Code).ShouldBe(["BOARD", "ENGINEERING"]);
        dto.Departments[0].Budget.ShouldBe(0L);
        dto.Departments[1].Budget.ShouldBe(500_000L);
    }

    [Fact]
    public void Ignored_members_are_never_copied_anywhere_in_the_graph()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;

        var dto = mapper.Map<Company, CompanyDto>(CreateAcme());

        var everyEmployee = dto.Departments.SelectMany(d => d.Employees).Where(e => e is not null).Append(dto.Ceo);
        everyEmployee.ShouldAllBe(e => e.Password == null);
    }

    [Fact]
    public void Back_references_to_the_company_resolve_to_the_root_object()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;

        var dto = mapper.Map<Company, CompanyDto>(CreateAcme());

        dto.Departments.ShouldAllBe(department => ReferenceEquals(department.Company, dto));
        dto.Ceo.Department.Company.ShouldBeSameAs(dto);
    }

    [Fact]
    public void An_employee_reached_through_several_paths_is_mapped_to_one_object()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;
        var company = CreateAcme();
        var dto = mapper.Map<Company, CompanyDto>(company);
        var board = dto.Departments[0];
        var engineering = dto.Departments[1];
        var cto = engineering.Manager;

        // CEO: company.Ceo, board.Manager, board.Employees[0], cto.Manager.
        board.Manager.ShouldBeSameAs(dto.Ceo);
        board.Employees[0].ShouldBeSameAs(dto.Ceo);
        cto.Manager.ShouldBeSameAs(dto.Ceo);

        // CTO: engineering.Manager, engineering.Employees[0], ceo.Reports[0].
        engineering.Employees[0].ShouldBeSameAs(cto);
        dto.Ceo?.Reports.Single().ShouldBeSameAs(cto);

        // Developers: engineering.Employees and cto.Reports hold the same objects, which point back up.
        cto.Reports.ShouldBe([engineering.Employees[1], engineering.Employees[2]]);
        cto.Reports.ShouldAllBe(developer => ReferenceEquals(developer.Manager, cto));
        cto.Reports.ShouldAllBe(developer => ReferenceEquals(developer.Department, engineering));
        dto.Ceo?.Department.ShouldBeSameAs(board);
    }

    [Fact]
    public void Nulls_inside_the_graph_stay_null()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;

        var dto = mapper.Map<Company, CompanyDto>(CreateAcme());
        var engineering = dto.Departments[1];
        var developer = engineering.Employees[1];

        engineering.Employees.Length.ShouldBe(4);
        engineering.Employees[3].ShouldBeNull();
        engineering.Manager.Salary.ShouldBeNull();
        developer.Address.ShouldBeNull();
        developer.Skills.ShouldBeNull();
        developer.Reports.ShouldBeNull();
        developer.Email.ShouldBeNull();
        engineering.Manager.Address.Street.ShouldBeNull();
        engineering.Manager.Address.Location.ShouldBe(default(CoordinatesDto));
        dto.Ceo.Manager.ShouldBeNull();
    }

    [Fact]
    public void An_empty_source_maps_to_an_empty_destination()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;

        var dto = mapper.Map<Company, CompanyDto>(new Company());

        dto.Name.ShouldBeNull();
        dto.Founded.ShouldBe("01/01/0001 00:00:00");
        dto.Tags.ShouldBeNull();
        dto.Headquarters.ShouldBeNull();
        dto.Ceo.ShouldBeNull();
        dto.Departments.ShouldBeNull();
        dto.DepartmentCount.ShouldBe(0);
    }

    [Fact]
    public void Objects_outside_any_cycle_are_mapped_once_per_reference_to_them()
    {
        // Address is not part of a type-level cycle, so it is not tracked: the headquarters object shared by the
        // company and the CEO becomes two equal, separate DTOs.
        var (provider, mapper) = BuildMapper();
        using var _ = provider;

        var dto = mapper.Map<Company, CompanyDto>(CreateAcme());

        dto.Ceo.Address.ShouldNotBeSameAs(dto.Headquarters);
        dto.Ceo.Address.Street.ShouldBe(dto.Headquarters.Street);
        dto.Ceo.Address.Country.ShouldNotBeSameAs(dto.Headquarters.Country);
    }

    [Fact]
    public void Text_members_use_the_invariant_culture_whatever_the_current_culture()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var dto = mapper.Map<Company, CompanyDto>(CreateAcme());

            dto.Founded.ShouldBe("02/03/2001 04:05:06");
            dto.Ceo.HireDate.ShouldBe("02/03/2001");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Each_call_produces_an_independent_graph()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;
        var company = CreateAcme();

        var first = mapper.Map<Company, CompanyDto>(company);
        var second = mapper.Map<Company, CompanyDto>(company);

        second.ShouldNotBeSameAs(first);
        second.Ceo.ShouldNotBeSameAs(first.Ceo);
        second.Departments[0].Company.ShouldBeSameAs(second);
    }

    [Fact]
    public void The_type_erased_overload_maps_the_same_graph()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;

        var dto = mapper.Map<CompanyDto>((object)CreateAcme());

        dto.Ceo.FullName.ShouldBe("Lan Nguyen");
        dto.Departments[1].Manager.Manager.ShouldBeSameAs(dto.Ceo);
        dto.Departments[0].Company.ShouldBeSameAs(dto);
    }

    [Fact]
    public void Update_keeps_existing_nested_objects_and_ignored_values_and_replaces_collections()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;
        var existingCountry = new CountryDto { Code = "old", Name = "old" };
        var existingHeadquarters = new AddressDto { Street = "old", Country = existingCountry };
        var existingCeo = new EmployeeDto { FullName = "old", Password = "kept-hash" };
        var existingDepartments = new List<DepartmentDto> { new() { Name = "old" } };
        var destination = new CompanyDto
        {
            Name = "old", Headquarters = existingHeadquarters, Ceo = existingCeo, Departments = existingDepartments
        };

        var result = mapper.Map(CreateAcme(), destination);

        result.ShouldBeSameAs(destination);
        destination.Name.ShouldBe("Acme");

        destination.Headquarters.ShouldBeSameAs(existingHeadquarters);
        existingHeadquarters.Street.ShouldBe("1 Trang Tien");
        existingHeadquarters.Country.ShouldBeSameAs(existingCountry);
        existingCountry.Code.ShouldBe("VN");

        destination.Ceo.ShouldBeSameAs(existingCeo);
        existingCeo.FullName.ShouldBe("Lan Nguyen");
        existingCeo.Password.ShouldBe("kept-hash");

        destination.Departments.ShouldNotBeSameAs(existingDepartments);
        destination.Departments.Select(d => d.Name).ShouldBe(["Board", "Engineering"]);

        // The graph stays consistent with the updated objects: back references land on them, not on copies.
        destination.Departments.ShouldAllBe(department => ReferenceEquals(department.Company, destination));
        destination.Departments[0].Manager.ShouldBeSameAs(existingCeo);
    }

    [Fact]
    public void A_long_management_chain_maps_and_a_too_deep_one_throws_a_catchable_exception()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;

        static Employee Chain(int length)
        {
            var employee = new Employee { Id = 0 };
            for (var i = 1; i < length; i++) employee = new Employee { Id = i, Manager = employee };
            return employee;
        }

        var dto = mapper.Map<Employee, EmployeeDto>(Chain(200));
        var depth = 0;
        for (var current = dto; current.Manager is not null; current = current.Manager) depth++;
        depth.ShouldBe(199);

        Should.Throw<InvalidOperationException>(() =>
                mapper.Map<Employee, EmployeeDto>(Chain(MappingContext.MaxDepth + 10)))
            .Message.ShouldContain("maximum nesting depth");
    }

    // The flags enum is internal, so the theory passes its numeric value.
    public static TheoryData<int> AllOptimizationCombinations() =>
        [.. Enumerable.Range(0, (int)MapperOptimizations.All + 1)];

    [Theory]
    [MemberData(nameof(AllOptimizationCombinations))]
    public void Every_combination_of_runtime_optimizations_produces_the_same_graph(int flags)
    {
        var optimizations = (MapperOptimizations)flags;
        var services = new ServiceCollection();
        services.AddSingleton<IProfile>(new OrganisationProfile());
        var registry = new RegistryProvider(services.BuildServiceProvider()).ProfileRegistry;
        var json = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve };

        var baseline = new RegistryMapperResolver(registry, MapperOptimizations.None).Get<Company, CompanyDto>();
        var candidate = new RegistryMapperResolver(registry, optimizations).Get<Company, CompanyDto>();

        JsonSerializer.Serialize(candidate.Map(CreateAcme()), json)
            .ShouldBe(JsonSerializer.Serialize(baseline.Map(CreateAcme()), json));
        JsonSerializer.Serialize(candidate.Map(new Company()), json)
            .ShouldBe(JsonSerializer.Serialize(baseline.Map(new Company()), json));

        static CompanyDto Existing() => new()
        {
            Name = "old", Headquarters = new AddressDto { Street = "old", Country = new CountryDto { Code = "old" } },
            Ceo = new EmployeeDto { FullName = "old", Password = "kept-hash" },
            Departments = [new DepartmentDto { Name = "old" }]
        };

        var updatedCandidate = Existing();
        var updatedBaseline = Existing();
        candidate.Map(CreateAcme(), updatedCandidate);
        baseline.Map(CreateAcme(), updatedBaseline);
        JsonSerializer.Serialize(updatedCandidate, json).ShouldBe(JsonSerializer.Serialize(updatedBaseline, json));
        updatedCandidate.Ceo.Password.ShouldBe("kept-hash");
    }
}
