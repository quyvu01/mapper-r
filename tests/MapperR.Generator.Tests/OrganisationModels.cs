namespace MapperR.Generator.Tests.Models.Organisation;

// Public copy of the organisation graph used by MapperR.Core.Tests' ComplexMappingTests: generated code is compiled
// into a separate in-memory assembly and must be able to reference these types.

public enum CompanyStatus
{
    Active = 1,
    Suspended = 2
}

public enum CompanyStatusDto
{
    Active = 1,
    Suspended = 2
}

public enum EmployeeLevel
{
    Junior = 1,
    Senior = 2,
    Executive = 3
}

public struct Coordinates
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public struct CoordinatesDto
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}

public class Country
{
    public string Code { get; set; }
    public string Name { get; set; }
}

public class CountryDto
{
    public string Code { get; set; }
    public string Name { get; set; }
}

public class Address
{
    public string Street { get; set; }
    public string City { get; set; }
    public Country Country { get; set; }
    public Coordinates Location { get; set; }
}

public class AddressDto
{
    public string Street { get; set; }
    public string City { get; set; }
    public CountryDto Country { get; set; }
    public CoordinatesDto Location { get; set; }
}

public class Skill
{
    public string Name { get; set; }
    public int Years { get; set; }
}

public class SkillDto
{
    public string Name { get; set; }
    public double Years { get; set; }
}

public class Employee
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

public class EmployeeDto
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

public class Department
{
    public string Name { get; set; }
    public int? Budget { get; set; }
    public Employee Manager { get; set; }
    public List<Employee> Employees { get; set; }
    public Company Company { get; set; }
}

public class DepartmentDto
{
    public string Name { get; set; }
    public string Code { get; set; }
    public long Budget { get; set; }
    public EmployeeDto Manager { get; set; }
    public EmployeeDto[] Employees { get; set; }
    public CompanyDto Company { get; set; }
}

public class Company
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

public class CompanyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Founded { get; set; }
    public double Rating { get; set; }
    public CompanyStatusDto Status { get; set; }
    public List<string> Tags { get; set; }
    public AddressDto Headquarters { get; set; }
    public EmployeeDto Ceo { get; set; }   // before Departments on purpose, see DESIGN.md #22
    public List<DepartmentDto> Departments { get; set; }
    public int DepartmentCount { get; set; }
}
