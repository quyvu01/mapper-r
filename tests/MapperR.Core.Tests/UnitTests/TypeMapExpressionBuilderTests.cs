using MapperR.Core.Abstractions;
using MapperR.Core.Implementations;
using MapperR.Core.Registries;
using Shouldly;
using Xunit;

namespace MapperR.Core.Tests.UnitTests;

public class TypeMapExpressionBuilderTests
{
    private sealed class Address
    {
        public string City { get; set; }
    }

    private sealed class AddressDto
    {
        public string City { get; set; }
        public string Note { get; set; }
    }

    private sealed class Customer
    {
        public string Name { get; set; }
        public Address Address { get; set; }
    }

    private sealed class CustomerDto
    {
        public string Name { get; set; }
        public AddressDto Address { get; set; }
    }

    private sealed class Order
    {
        public Customer Customer { get; set; }
    }

    private sealed class OrderDto
    {
        public CustomerDto Customer { get; set; }
    }

    private sealed class Person
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public Address Address { get; set; }
    }

    private sealed class PersonDto
    {
        public string Name { get; set; }
        public long Age { get; set; }
        public AddressDto Address { get; set; }
        public string Nickname { get; set; }
    }

    private sealed class Self
    {
        public Self Child { get; set; }
        public List<Self> Children { get; set; }
    }

    private sealed class SelfDto
    {
        public SelfDto Child { get; set; }
        public List<SelfDto> Children { get; set; }
    }

    private sealed class PersonWithAddresses
    {
        public List<Address> Addresses { get; set; }
        public Address[] AddressArray { get; set; }
        public IEnumerable<Address> AddressSequence { get; set; }
        public List<int> Scores { get; set; }
    }

    private sealed class PersonWithAddressesDto
    {
        public List<AddressDto> Addresses { get; set; }
        public AddressDto[] AddressArray { get; set; }
        public IEnumerable<AddressDto> AddressSequence { get; set; }
        public HashSet<long> Scores { get; set; }
    }

    /// <summary>
    /// Exposes <see cref="Profile.CreateMap{TSource,TDestination}"/> (protected) through a delegate,
    /// so each test can declare its own set of maps without needing a dedicated Profile subclass.
    /// </summary>
    private sealed class DelegateProfile(Action<DelegateProfile> configure) : Profile
    {
        public override void AddProfiles() => configure(this);

        public IWireProfile<TSource, TDestination> Map<TSource, TDestination>() =>
            CreateMap<TSource, TDestination>();
    }

    private static WireProfileRegistry BuildRegistry(params Action<DelegateProfile>[] configurations)
    {
        var wireProfiles = new List<IWireProfile>();
        foreach (var configure in configurations)
        {
            var profile = new DelegateProfile(configure);
            profile.AddProfiles();
            wireProfiles.AddRange(profile.WireProfiles);
        }

        return new WireProfileRegistry(wireProfiles);
    }

    [Fact]
    public void Build_maps_members_by_convention_including_numeric_widening()
    {
        var registry = BuildRegistry(p => p.Map<Person, PersonDto>());
        var map = TypeMapExpressionBuilder<Person, PersonDto>.Build(registry).Compile();

        var result = map(new Person { Name = "Alice", Age = 30 });

        result.Name.ShouldBe("Alice");
        result.Age.ShouldBe(30L);
    }

    [Fact]
    public void Build_uses_the_explicitly_configured_computed_expression()
    {
        var registry = BuildRegistry(p => p.Map<Person, PersonDto>().ForMember(d => d.Name, s => $"{s.Name}!"));
        var map = TypeMapExpressionBuilder<Person, PersonDto>.Build(registry).Compile();

        var result = map(new Person { Name = "Alice", Age = 30 });

        result.Name.ShouldBe("Alice!");
    }

    [Fact]
    public void Build_inlines_a_nested_dependency_mapping()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<Person, PersonDto>().ForMember(d => d.Address, s => s.Address));
        var map = TypeMapExpressionBuilder<Person, PersonDto>.Build(registry).Compile();

        var result = map(new Person { Name = "Alice", Age = 30, Address = new Address { City = "Hanoi" } });

        result.Address.ShouldNotBeNull();
        result.Address.City.ShouldBe("Hanoi");
    }

    [Fact]
    public void Build_returns_null_for_a_null_nested_reference_instead_of_throwing()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<Person, PersonDto>().ForMember(d => d.Address, s => s.Address));
        var map = TypeMapExpressionBuilder<Person, PersonDto>.Build(registry).Compile();

        var result = map(new Person { Name = "Alice", Age = 30, Address = null });

        result.Address.ShouldBeNull();
    }

    [Fact]
    public void Build_throws_when_a_member_references_its_own_type_pair()
    {
        var registry = BuildRegistry(p => p.Map<Self, SelfDto>().ForMember(d => d.Child, s => s.Child));

        Should.Throw<InvalidOperationException>(() =>
                TypeMapExpressionBuilder<Self, SelfDto>.Build(registry))
            .Message.ShouldContain("unbounded recursion");
    }

    [Fact]
    public void Build_throws_a_clear_error_when_no_profile_is_registered()
    {
        var registry = BuildRegistry(p => p.Map<Address, AddressDto>());

        Should.Throw<InvalidOperationException>(() =>
                TypeMapExpressionBuilder<Person, PersonDto>.Build(registry))
            .Message.ShouldContain("No mapping profile registered");
    }

    [Fact]
    public void Build_maps_a_List_of_nested_objects_element_by_element()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<PersonWithAddresses, PersonWithAddressesDto>()
                .ForMember(d => d.Addresses, s => s.Addresses));
        var map = TypeMapExpressionBuilder<PersonWithAddresses, PersonWithAddressesDto>.Build(registry).Compile();

        var result = map(new PersonWithAddresses
        {
            Addresses = [new Address { City = "Hanoi" }, new Address { City = "Saigon" }]
        });

        result.Addresses.Select(a => a.City).ShouldBe(["Hanoi", "Saigon"]);
    }

    [Fact]
    public void Build_maps_an_array_of_nested_objects()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<PersonWithAddresses, PersonWithAddressesDto>()
                .ForMember(d => d.AddressArray, s => s.AddressArray));
        var map = TypeMapExpressionBuilder<PersonWithAddresses, PersonWithAddressesDto>.Build(registry).Compile();

        var result = map(new PersonWithAddresses { AddressArray = [new Address { City = "Hanoi" }] });

        result.AddressArray.ShouldBeOfType<AddressDto[]>();
        result.AddressArray.Single().City.ShouldBe("Hanoi");
    }

    [Fact]
    public void Build_maps_an_IEnumerable_of_nested_objects()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<PersonWithAddresses, PersonWithAddressesDto>()
                .ForMember(d => d.AddressSequence, s => s.AddressSequence));
        var map = TypeMapExpressionBuilder<PersonWithAddresses, PersonWithAddressesDto>.Build(registry).Compile();

        var result = map(new PersonWithAddresses
        {
            AddressSequence = new List<Address> { new() { City = "Hanoi" } }
        });

        result.AddressSequence.Single().City.ShouldBe("Hanoi");
    }

    [Fact]
    public void Build_maps_a_collection_with_numeric_element_conversion_into_a_HashSet()
    {
        var registry = BuildRegistry(
            p => p.Map<PersonWithAddresses, PersonWithAddressesDto>()
                .ForMember(d => d.Scores, s => s.Scores));
        var map = TypeMapExpressionBuilder<PersonWithAddresses, PersonWithAddressesDto>.Build(registry).Compile();

        var result = map(new PersonWithAddresses { Scores = [1, 2, 3] });

        result.Scores.ShouldBeOfType<HashSet<long>>();
        result.Scores.ShouldBe([1L, 2L, 3L], ignoreOrder: true);
    }

    [Fact]
    public void Build_returns_null_for_a_null_collection_instead_of_throwing()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<PersonWithAddresses, PersonWithAddressesDto>()
                .ForMember(d => d.Addresses, s => s.Addresses));
        var map = TypeMapExpressionBuilder<PersonWithAddresses, PersonWithAddressesDto>.Build(registry).Compile();

        var result = map(new PersonWithAddresses { Addresses = null });

        result.Addresses.ShouldBeNull();
    }

    [Fact]
    public void Build_throws_when_a_collection_element_references_its_own_type_pair()
    {
        var registry = BuildRegistry(
            p => p.Map<Self, SelfDto>().ForMember(d => d.Children, s => s.Children));

        Should.Throw<InvalidOperationException>(() =>
                TypeMapExpressionBuilder<Self, SelfDto>.Build(registry))
            .Message.ShouldContain("unbounded recursion");
    }

    [Fact]
    public void BuildUpdate_assigns_mapped_members_onto_the_existing_instance_and_keeps_unmapped_ones()
    {
        var registry = BuildRegistry(p => p.Map<Person, PersonDto>().ForMember(d => d.Name, s => $"{s.Name}!"));
        var update = TypeMapExpressionBuilder<Person, PersonDto>.BuildUpdate(registry).Compile();
        var destination = new PersonDto { Name = "old", Age = 1, Nickname = "keep me" };

        var result = update(new Person { Name = "Alice", Age = 30 }, destination);

        result.ShouldBeSameAs(destination);
        destination.Name.ShouldBe("Alice!");
        destination.Age.ShouldBe(30L);
        destination.Nickname.ShouldBe("keep me");
    }

    [Fact]
    public void BuildUpdate_updates_an_existing_nested_object_in_place()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<Person, PersonDto>().ForMember(d => d.Address, s => s.Address));
        var update = TypeMapExpressionBuilder<Person, PersonDto>.BuildUpdate(registry).Compile();
        var existingAddress = new AddressDto { City = "old", Note = "keep me" };
        var destination = new PersonDto { Address = existingAddress };

        update(new Person { Address = new Address { City = "Hanoi" } }, destination);

        destination.Address.ShouldBeSameAs(existingAddress);
        existingAddress.City.ShouldBe("Hanoi");
        existingAddress.Note.ShouldBe("keep me");
    }

    [Fact]
    public void BuildUpdate_creates_the_nested_object_when_the_destination_has_none()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<Person, PersonDto>().ForMember(d => d.Address, s => s.Address));
        var update = TypeMapExpressionBuilder<Person, PersonDto>.BuildUpdate(registry).Compile();
        var destination = new PersonDto { Address = null };

        update(new Person { Address = new Address { City = "Hanoi" } }, destination);

        destination.Address.ShouldNotBeNull();
        destination.Address.City.ShouldBe("Hanoi");
    }

    [Fact]
    public void BuildUpdate_sets_the_nested_object_to_null_when_the_source_has_none()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<Person, PersonDto>().ForMember(d => d.Address, s => s.Address));
        var update = TypeMapExpressionBuilder<Person, PersonDto>.BuildUpdate(registry).Compile();
        var destination = new PersonDto { Address = new AddressDto { City = "old" } };

        update(new Person { Address = null }, destination);

        destination.Address.ShouldBeNull();
    }

    [Fact]
    public void BuildUpdate_keeps_references_through_several_nesting_levels()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<Customer, CustomerDto>().ForMember(d => d.Address, s => s.Address),
            p => p.Map<Order, OrderDto>().ForMember(d => d.Customer, s => s.Customer));
        var update = TypeMapExpressionBuilder<Order, OrderDto>.BuildUpdate(registry).Compile();
        var existingAddress = new AddressDto { City = "old", Note = "keep me" };
        var existingCustomer = new CustomerDto { Name = "old", Address = existingAddress };
        var destination = new OrderDto { Customer = existingCustomer };

        update(new Order { Customer = new Customer { Name = "Alice", Address = new Address { City = "Hanoi" } } },
            destination);

        destination.Customer.ShouldBeSameAs(existingCustomer);
        existingCustomer.Name.ShouldBe("Alice");
        existingCustomer.Address.ShouldBeSameAs(existingAddress);
        existingAddress.City.ShouldBe("Hanoi");
        existingAddress.Note.ShouldBe("keep me");
    }

    [Fact]
    public void BuildUpdate_still_replaces_collections()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<PersonWithAddresses, PersonWithAddressesDto>()
                .ForMember(d => d.Addresses, s => s.Addresses));
        var update = TypeMapExpressionBuilder<PersonWithAddresses, PersonWithAddressesDto>.BuildUpdate(registry)
            .Compile();
        var existingList = new List<AddressDto> { new() { City = "old" } };
        var destination = new PersonWithAddressesDto { Addresses = existingList };

        update(new PersonWithAddresses { Addresses = [new Address { City = "Hanoi" }] }, destination);

        destination.Addresses.ShouldNotBeSameAs(existingList);
        destination.Addresses.Single().City.ShouldBe("Hanoi");
    }
}