using MapperR.Core.Abstractions;
using MapperR.Core.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MapperR.Core.Tests.UnitTests;

public class InternalMapperTests
{
    private sealed class Person
    {
        public string Name { get; set; }
        public int Age { get; set; }
    }

    private sealed class PersonDto
    {
        public string Name { get; set; }
        public long Age { get; set; }
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

    private static RegistryProvider BuildRegistryProvider(params Action<DelegateProfile>[] configurations)
    {
        var services = new ServiceCollection();
        foreach (var configure in configurations)
            services.AddSingleton<IProfile>(new DelegateProfile(configure));

        return new RegistryProvider(services.BuildServiceProvider());
    }

    [Fact]
    public void Map_returns_the_mapped_destination_instance()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<Person, PersonDto>());
        var internalMapper = new InternalMapper<Person, PersonDto>(registryProvider);

        var result = internalMapper.Map(new Person { Name = "Alice", Age = 30 });

        result.Name.ShouldBe("Alice");
        result.Age.ShouldBe(30L);
    }

    [Fact]
    public void Map_throws_when_source_is_null()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<Person, PersonDto>());
        var internalMapper = new InternalMapper<Person, PersonDto>(registryProvider);

        Should.Throw<ArgumentNullException>(() => internalMapper.Map(null!));
    }

    [Fact]
    public void MapInternal_delegates_to_the_typed_Map_method()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<Person, PersonDto>());
        AbstractInternalMapper<PersonDto> internalMapper = new InternalMapper<Person, PersonDto>(registryProvider);

        var result = internalMapper.MapInternal(new Person { Name = "Bob", Age = 40 });

        result.Name.ShouldBe("Bob");
        result.Age.ShouldBe(40L);
    }

    [Fact]
    public void Map_handles_multiple_calls_with_different_instances_correctly()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<Person, PersonDto>());
        var internalMapper = new InternalMapper<Person, PersonDto>(registryProvider);

        var first = internalMapper.Map(new Person { Name = "Alice", Age = 30 });
        var second = internalMapper.Map(new Person { Name = "Carl", Age = 50 });

        first.Name.ShouldBe("Alice");
        first.Age.ShouldBe(30L);
        second.Name.ShouldBe("Carl");
        second.Age.ShouldBe(50L);
    }

    [Fact]
    public void Constructor_throws_immediately_when_no_profile_is_registered_for_the_type_pair()
    {
        var registryProvider = BuildRegistryProvider();

        Should.Throw<InvalidOperationException>(() => new InternalMapper<Person, PersonDto>(registryProvider))
            .Message.ShouldContain("No mapping profile registered");
    }
}
