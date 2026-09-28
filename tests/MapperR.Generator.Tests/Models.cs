namespace MapperR.Generator.Tests.Models;

// Public on purpose: generated code is compiled into a separate in-memory assembly and must be able to
// reference these types.

public enum Status
{
    Active = 1,
    Inactive = 2
}

public enum StatusDto
{
    Active = 1,
    Inactive = 2
}

public class Address
{
    public string City { get; set; }
}

public class AddressDto
{
    public string City { get; set; }
}

public class Person
{
    public string Name { get; set; }
    public int Age { get; set; }
    public Status Status { get; set; }
    public Address Address { get; set; }
    public List<Address> Addresses { get; set; }
    public Address[] AddressArray { get; set; }
    public IEnumerable<Address> AddressSequence { get; set; }
    public List<int> Scores { get; set; }
}

public class PersonDto
{
    public string Name { get; set; }
    public long Age { get; set; }
    public StatusDto Status { get; set; }
    public AddressDto Address { get; set; }
    public List<AddressDto> Addresses { get; set; }
    public AddressDto[] AddressArray { get; set; }
    public IEnumerable<AddressDto> AddressSequence { get; set; }
    public HashSet<long> Scores { get; set; }
}

public class PrivateSetterDto
{
    public string Name { get; private set; }
}

public class Node
{
    public Node Child { get; set; }
}

public class NodeDto
{
    public NodeDto Child { get; set; }
}

public class Outer<T>
{
    public class Inner<TInner>;
}
