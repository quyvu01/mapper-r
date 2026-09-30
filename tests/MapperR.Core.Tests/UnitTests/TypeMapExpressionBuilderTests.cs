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

    private sealed class NullableSource
    {
        public int? Count { get; set; }
        public int? Total { get; set; }
        public List<int?> Values { get; set; }
    }

    private sealed class NullableTarget
    {
        public int Count { get; set; }
        public long Total { get; set; }
        public List<int> Values { get; set; }
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

    private sealed class Team
    {
        public string Name { get; set; }
        public List<Member> Members { get; set; }
    }

    private sealed class TeamDto
    {
        public string Name { get; set; }
        public List<MemberDto> Members { get; set; }
    }

    private sealed class Member
    {
        public string Name { get; set; }
        public Team Team { get; set; }
    }

    private sealed class MemberDto
    {
        public string Name { get; set; }
        public TeamDto Team { get; set; }
    }

    private sealed class Measure
    {
        public int Count { get; set; }
        public decimal Price { get; set; }
        public DayOfWeek Day { get; set; }
        public bool Flag { get; set; }
        public int? Missing { get; set; }
    }

    private sealed class MeasureDto
    {
        public string Count { get; set; }
        public string Price { get; set; }
        public string Day { get; set; }
        public string Flag { get; set; }
        public string Missing { get; set; }
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

    /// <summary>Compiles the map tree; nested pairs resolve through the registry, without DI.</summary>
    private static Func<TSource, TDestination> CompileMap<TSource, TDestination>(WireProfileRegistry registry)
        where TDestination : new()
    {
        var map = TypeMapExpressionBuilder<TSource, TDestination>.Build(registry).Compile();
        return source => map(source, new MappingContext(new RegistryMapperResolver(registry)));
    }

    private static Func<TSource, TDestination, TDestination> CompileUpdate<TSource, TDestination>(
        WireProfileRegistry registry) where TDestination : new()
    {
        var update = TypeMapExpressionBuilder<TSource, TDestination>.BuildUpdate(registry).Compile();
        return (source, destination) =>
            update(source, destination, new MappingContext(new RegistryMapperResolver(registry)));
    }

    [Fact]
    public void Build_maps_members_by_convention_including_numeric_widening()
    {
        var registry = BuildRegistry(p => p.Map<Person, PersonDto>());
        var map = CompileMap<Person, PersonDto>(registry);

        var result = map(new Person { Name = "Alice", Age = 30 });

        result.Name.ShouldBe("Alice");
        result.Age.ShouldBe(30L);
    }

    [Fact]
    public void Build_uses_the_explicitly_configured_computed_expression()
    {
        var registry = BuildRegistry(p => p.Map<Person, PersonDto>().ForMember(d => d.Name, s => $"{s.Name}!"));
        var map = CompileMap<Person, PersonDto>(registry);

        var result = map(new Person { Name = "Alice", Age = 30 });

        result.Name.ShouldBe("Alice!");
    }

    [Fact]
    public void Build_inlines_a_nested_dependency_mapping()
    {
        var registry = BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<Person, PersonDto>().ForMember(d => d.Address, s => s.Address));
        var map = CompileMap<Person, PersonDto>(registry);

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
        var map = CompileMap<Person, PersonDto>(registry);

        var result = map(new Person { Name = "Alice", Age = 30, Address = null });

        result.Address.ShouldBeNull();
    }

    [Fact]
    public void Build_maps_a_self_referencing_type_by_following_the_data()
    {
        var registry = BuildRegistry(p => p.Map<Self, SelfDto>());
        var map = CompileMap<Self, SelfDto>(registry);

        var result = map(new Self { Child = new Self { Child = new Self() }, Children = [new Self()] });

        result.Child.ShouldNotBeNull();
        result.Child.Child.ShouldNotBeNull();
        result.Child.Child.Child.ShouldBeNull();
        result.Children.Count.ShouldBe(1);
        result.Children[0].Children.ShouldBeNull();
    }

    [Fact]
    public void Map_keeps_the_shape_of_cyclic_data()
    {
        var registry = BuildRegistry(p => p.Map<Self, SelfDto>());
        var mapper = new RegistryMapperResolver(registry).Get<Self, SelfDto>();
        var loop = new Self();
        loop.Child = loop;
        loop.Children = [loop];

        var result = mapper.Map(loop);

        result.Child.ShouldBeSameAs(result);
        result.Children.Single().ShouldBeSameAs(result);
    }

    [Fact]
    public void Map_throws_a_catchable_exception_when_nesting_exceeds_the_maximum_depth()
    {
        var registry = BuildRegistry(p => p.Map<Self, SelfDto>());
        var mapper = new RegistryMapperResolver(registry).Get<Self, SelfDto>();
        var deep = new Self();
        for (var i = 0; i < MappingContext.MaxDepth + 10; i++) deep = new Self { Child = deep };

        Should.Throw<InvalidOperationException>(() => mapper.Map(deep))
            .Message.ShouldContain("maximum nesting depth");
    }

    [Fact]
    public void Build_handles_types_that_reference_each_other_through_a_collection()
    {
        // Used to overflow the stack while *building*, whatever the data: building Team inlined Member,
        // which inlined Team again.
        var registry = BuildRegistry(p => p.Map<Team, TeamDto>(), p => p.Map<Member, MemberDto>());
        var map = CompileMap<Team, TeamDto>(registry);

        var team = new Team { Name = "core" };
        team.Members = [new Member { Name = "a", Team = null }, new Member { Name = "b", Team = team }];
        var result = map(team);

        result.Members.Select(m => m.Name).ShouldBe(["a", "b"]);
        result.Members[0].Team.ShouldBeNull();
        result.Members[1].Team.Name.ShouldBe("core");
    }

    [Fact]
    public void Build_skips_a_convention_member_whose_pair_is_not_registered()
    {
        var registry = BuildRegistry(p => p.Map<Person, PersonDto>());
        var map = CompileMap<Person, PersonDto>(registry);

        var result = map(new Person { Name = "Alice", Address = new Address { City = "Hanoi" } });

        result.Name.ShouldBe("Alice");
        result.Address.ShouldBeNull();
    }

    [Fact]
    public void Build_throws_for_an_explicit_member_that_cannot_be_mapped()
    {
        var registry = BuildRegistry(p => p.Map<Person, PersonDto>().ForMember(d => d.Address, s => s.Address));

        Should.Throw<InvalidOperationException>(() => TypeMapExpressionBuilder<Person, PersonDto>.Build(registry))
            .Message.ShouldContain("'Address' cannot be mapped");
    }

    [Fact]
    public void Build_formats_scalars_into_strings_with_the_invariant_culture()
    {
        var registry = BuildRegistry(p => p.Map<Measure, MeasureDto>());
        var map = CompileMap<Measure, MeasureDto>(registry);
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var result = map(new Measure
                { Count = 3, Price = 1.5m, Day = DayOfWeek.Monday, Flag = true, Missing = null });

            result.Count.ShouldBe("3");
            result.Price.ShouldBe("1.5");
            result.Day.ShouldBe("Monday");
            result.Flag.ShouldBe("True");
            result.Missing.ShouldBeNull();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
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
        var map = CompileMap<PersonWithAddresses, PersonWithAddressesDto>(registry);

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
        var map = CompileMap<PersonWithAddresses, PersonWithAddressesDto>(registry);

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
        var map = CompileMap<PersonWithAddresses, PersonWithAddressesDto>(registry);

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
        var map = CompileMap<PersonWithAddresses, PersonWithAddressesDto>(registry);

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
        var map = CompileMap<PersonWithAddresses, PersonWithAddressesDto>(registry);

        var result = map(new PersonWithAddresses { Addresses = null });

        result.Addresses.ShouldBeNull();
    }

    [Fact]
    public void Build_maps_a_null_nullable_to_a_non_nullable_value_type_as_default_instead_of_throwing()
    {
        var registry = BuildRegistry(p => p.Map<NullableSource, NullableTarget>()
            .ForMember(d => d.Values, s => s.Values));
        var map = CompileMap<NullableSource, NullableTarget>(registry);

        var result = map(new NullableSource { Count = null, Total = null, Values = [1, null, 3] });

        result.Count.ShouldBe(0);
        result.Total.ShouldBe(0L);
        result.Values.ShouldBe([1, 0, 3]);
    }

    [Fact]
    public void Build_keeps_nullable_values_that_are_present()
    {
        var registry = BuildRegistry(p => p.Map<NullableSource, NullableTarget>());
        var map = CompileMap<NullableSource, NullableTarget>(registry);

        var result = map(new NullableSource { Count = 5, Total = 7 });

        result.Count.ShouldBe(5);
        result.Total.ShouldBe(7L);
    }

    [Fact]
    public void BuildUpdate_maps_a_null_nullable_to_a_non_nullable_value_type_as_default()
    {
        var registry = BuildRegistry(p => p.Map<NullableSource, NullableTarget>());
        var update = CompileUpdate<NullableSource, NullableTarget>(registry);
        var destination = new NullableTarget { Count = 9, Total = 9 };

        update(new NullableSource(), destination);

        destination.Count.ShouldBe(0);
        destination.Total.ShouldBe(0L);
    }

    [Fact]
    public void BuildUpdate_assigns_mapped_members_onto_the_existing_instance_and_keeps_unmapped_ones()
    {
        var registry = BuildRegistry(p => p.Map<Person, PersonDto>().ForMember(d => d.Name, s => $"{s.Name}!"));
        var update = CompileUpdate<Person, PersonDto>(registry);
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
        var update = CompileUpdate<Person, PersonDto>(registry);
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
        var update = CompileUpdate<Person, PersonDto>(registry);
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
        var update = CompileUpdate<Person, PersonDto>(registry);
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
        var update = CompileUpdate<Order, OrderDto>(registry);
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
        var update = CompileUpdate<PersonWithAddresses, PersonWithAddressesDto>(registry);
        var existingList = new List<AddressDto> { new() { City = "old" } };
        var destination = new PersonWithAddressesDto { Addresses = existingList };

        update(new PersonWithAddresses { Addresses = [new Address { City = "Hanoi" }] }, destination);

        destination.Addresses.ShouldNotBeSameAs(existingList);
        destination.Addresses.Single().City.ShouldBe("Hanoi");
    }

    [Fact]
    public void Map_leaves_an_ignored_member_at_its_default_value()
    {
        var registry = BuildRegistry(p => p.Map<Person, PersonDto>().Ignore(d => d.Name));
        var map = CompileMap<Person, PersonDto>(registry);

        var result = map(new Person { Name = "Alice", Age = 30 });

        result.Name.ShouldBeNull();
        result.Age.ShouldBe(30L);
    }

    [Fact]
    public void Update_keeps_the_existing_value_of_an_ignored_member()
    {
        var registry = BuildRegistry(p => p.Map<Person, PersonDto>().Ignore(d => d.Name));
        var update = CompileUpdate<Person, PersonDto>(registry);
        var destination = new PersonDto { Name = "kept", Age = 1 };

        update(new Person { Name = "Alice", Age = 30 }, destination);

        destination.Name.ShouldBe("kept");
        destination.Age.ShouldBe(30L);
    }

    [Fact]
    public void Ignore_wins_over_an_explicit_member_that_cannot_be_mapped()
    {
        // Without Ignore this ForMember throws when the pair is built (string cannot be mapped to AddressDto).
        var registry = BuildRegistry(p => p.Map<Person, PersonDto>()
            .ForMember(d => d.Address, s => s.Name)
            .Ignore(d => d.Address));

        var result = CompileMap<Person, PersonDto>(registry)(new Person { Name = "Alice" });

        result.Address.ShouldBeNull();
        result.Name.ShouldBe("Alice");
    }

    [Fact]
    public void Ignoring_the_self_reference_stops_following_it()
    {
        var registry = BuildRegistry(p => p.Map<Self, SelfDto>().Ignore(d => d.Child));
        var map = CompileMap<Self, SelfDto>(registry);

        var result = map(new Self { Child = new Self(), Children = [new Self()] });

        result.Child.ShouldBeNull();
        result.Children.Count.ShouldBe(1);
    }
}
