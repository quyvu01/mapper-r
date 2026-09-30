using MapperR.Core.Abstractions;
using MapperR.Core.Registries;
using Shouldly;
using Xunit;

namespace MapperR.Core.Tests.UnitTests;

public class WireProfileRegistryTests
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
        public Address Address { get; set; }
        public Person Self { get; set; }
    }

    private sealed class PersonDto
    {
        public string Name { get; set; }
        public AddressDto Address { get; set; }
        public PersonDto Self { get; set; }
    }

    private sealed class A
    {
        public B B { get; set; }
    }

    private sealed class ADto
    {
        public BDto B { get; set; }
    }

    private sealed class B
    {
        public A A { get; set; }
    }

    private sealed class BDto
    {
        public ADto A { get; set; }
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

    private static IWireProfile[] BuildProfiles(params Action<DelegateProfile>[] configurations)
    {
        var wireProfiles = new List<IWireProfile>();
        foreach (var configure in configurations)
        {
            var profile = new DelegateProfile(configure);
            profile.AddProfiles();
            wireProfiles.AddRange(profile.WireProfiles);
        }

        return [.. wireProfiles];
    }

    [Fact]
    public void Find_returns_the_registered_profile_for_a_type_pair()
    {
        var profiles = BuildProfiles(p => p.Map<Person, PersonDto>());
        var registry = new WireProfileRegistry(profiles);

        var found = registry.Find(typeof(Person), typeof(PersonDto));

        found.ShouldNotBeNull();
        found.SourceType.ShouldBe(typeof(Person));
        found.DestinationType.ShouldBe(typeof(PersonDto));
    }

    [Fact]
    public void Find_returns_null_for_an_unregistered_type_pair()
    {
        var profiles = BuildProfiles(p => p.Map<Person, PersonDto>());
        var registry = new WireProfileRegistry(profiles);

        registry.Find(typeof(int), typeof(string)).ShouldBeNull();
    }

    [Fact]
    public void GetDependenciesOf_resolves_a_nested_type_pair_as_a_dependency()
    {
        var profiles = BuildProfiles(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<Person, PersonDto>().ForMember(d => d.Address, s => s.Address));
        var registry = new WireProfileRegistry(profiles);

        var personProfile = registry.Find(typeof(Person), typeof(PersonDto));
        var addressProfile = registry.Find(typeof(Address), typeof(AddressDto));

        var dependencies = registry.GetDependenciesOf(personProfile);

        dependencies.Count.ShouldBe(1);
        dependencies.ShouldContain(addressProfile);
    }

    [Fact]
    public void GetDependenciesOf_ignores_members_whose_type_pair_is_not_registered()
    {
        var profiles = BuildProfiles(
            p => p.Map<Person, PersonDto>().ForMember(d => d.Name, s => s.Name));
        var registry = new WireProfileRegistry(profiles);

        var personProfile = registry.Find(typeof(Person), typeof(PersonDto));

        registry.GetDependenciesOf(personProfile).ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_does_not_treat_a_member_referencing_its_own_type_pair_as_a_dependency()
    {
        // Person.Self : Person and PersonDto.Self : PersonDto happen to match the profile's own
        // (Source, Destination) pair — this must not be reported as a dependency, and must not be
        // mistaken for a circular dependency between two *different* profiles.
        var profiles = BuildProfiles(
            p => p.Map<Person, PersonDto>().ForMember(d => d.Self, s => s.Self));

        var registry = new WireProfileRegistry(profiles);
        var personProfile = registry.Find(typeof(Person), typeof(PersonDto));

        registry.GetDependenciesOf(personProfile).ShouldBeEmpty();
    }

    [Fact]
    public void Mutually_dependent_profiles_are_accepted_and_track_references()
    {
        var profiles = BuildProfiles(
            p => p.Map<A, ADto>().ForMember(d => d.B, s => s.B),
            p => p.Map<B, BDto>().ForMember(d => d.A, s => s.A),
            p => p.Map<Address, AddressDto>());

        var registry = new WireProfileRegistry(profiles);

        registry.TracksReferences(registry.Find(typeof(A), typeof(ADto))).ShouldBeTrue();
        registry.TracksReferences(registry.Find(typeof(B), typeof(BDto))).ShouldBeTrue();
        registry.TracksReferences(registry.Find(typeof(Address), typeof(AddressDto))).ShouldBeFalse();
    }

    [Fact]
    public void The_last_profile_wins_when_the_same_type_pair_is_registered_twice()
    {
        var profiles = BuildProfiles(
            p => p.Map<Person, PersonDto>(),
            p => p.Map<Person, PersonDto>());

        var registry = new WireProfileRegistry(profiles);

        registry.Find(typeof(Person), typeof(PersonDto)).ShouldBeSameAs(profiles[1]);
    }

    [Fact]
    public void Ignoring_the_member_that_closes_a_cycle_turns_reference_tracking_off()
    {
        var profiles = BuildProfiles(
            p => p.Map<A, ADto>().ForMember(d => d.B, s => s.B),
            p => p.Map<B, BDto>().ForMember(d => d.A, s => s.A).Ignore(d => d.A));

        var registry = new WireProfileRegistry(profiles);

        registry.TracksReferences(registry.Find(typeof(A), typeof(ADto))).ShouldBeFalse();
        registry.TracksReferences(registry.Find(typeof(B), typeof(BDto))).ShouldBeFalse();
    }
}
