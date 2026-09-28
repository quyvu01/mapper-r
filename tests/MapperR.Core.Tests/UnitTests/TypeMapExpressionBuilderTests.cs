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
    }

    private sealed class Self
    {
        public Self Child { get; set; }
    }

    private sealed class SelfDto
    {
        public SelfDto Child { get; set; }
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
}