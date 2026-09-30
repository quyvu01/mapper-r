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

    // Self-referencing pair: reference tracking is on.
    private sealed class TreeNode
    {
        public string Name { get; set; }
        public TreeNode Left { get; set; }
        public TreeNode Right { get; set; }
    }

    private sealed class TreeNodeDto
    {
        public string Name { get; set; }
        public TreeNodeDto Left { get; set; }
        public TreeNodeDto Right { get; set; }
    }

    // Not part of any cycle: reference tracking is off.
    private sealed class Place
    {
        public string City { get; set; }
    }

    private sealed class PlaceDto
    {
        public string City { get; set; }
    }

    private sealed class Trip
    {
        public Place From { get; set; }
        public Place To { get; set; }
    }

    private sealed class TripDto
    {
        public PlaceDto From { get; set; }
        public PlaceDto To { get; set; }
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

    private static InternalMapper<TSource, TDestination> NewMapper<TSource, TDestination>(
        RegistryProvider registryProvider) where TDestination : new() =>
        new(registryProvider.ProfileRegistry, new RegistryMapperResolver(registryProvider.ProfileRegistry));

    [Fact]
    public void Map_returns_the_mapped_destination_instance()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<Person, PersonDto>());
        var internalMapper = NewMapper<Person, PersonDto>(registryProvider);

        var result = internalMapper.Map(new Person { Name = "Alice", Age = 30 });

        result.Name.ShouldBe("Alice");
        result.Age.ShouldBe(30L);
    }

    [Fact]
    public void Map_throws_when_source_is_null()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<Person, PersonDto>());
        var internalMapper = NewMapper<Person, PersonDto>(registryProvider);

        Should.Throw<ArgumentNullException>(() => internalMapper.Map(null!));
    }

    [Fact]
    public void MapInternal_delegates_to_the_typed_Map_method()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<Person, PersonDto>());
        AbstractInternalMapper<PersonDto> internalMapper = NewMapper<Person, PersonDto>(registryProvider);

        var result = internalMapper.MapInternal(new Person { Name = "Bob", Age = 40 });

        result.Name.ShouldBe("Bob");
        result.Age.ShouldBe(40L);
    }

    [Fact]
    public void Map_handles_multiple_calls_with_different_instances_correctly()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<Person, PersonDto>());
        var internalMapper = NewMapper<Person, PersonDto>(registryProvider);

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

        Should.Throw<InvalidOperationException>(() => NewMapper<Person, PersonDto>(registryProvider))
            .Message.ShouldContain("No mapping profile registered");
    }

    [Fact]
    public void Map_onto_an_existing_destination_updates_and_returns_that_instance()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<Person, PersonDto>());
        var internalMapper = NewMapper<Person, PersonDto>(registryProvider);
        var destination = new PersonDto { Name = "old", Age = 1 };

        var result = internalMapper.Map(new Person { Name = "Alice", Age = 30 }, destination);
        var viaBridge = ((AbstractInternalMapper<PersonDto>)internalMapper)
            .MapInternal(new Person { Name = "Bob", Age = 40 }, destination);

        result.ShouldBeSameAs(destination);
        viaBridge.ShouldBeSameAs(destination);
        destination.Name.ShouldBe("Bob");
        destination.Age.ShouldBe(40L);
    }

    [Fact]
    public void Map_onto_an_existing_destination_throws_when_source_is_null()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<Person, PersonDto>());
        var internalMapper = NewMapper<Person, PersonDto>(registryProvider);

        Should.Throw<ArgumentNullException>(() => internalMapper.Map(null!, new PersonDto()));
    }

    [Fact]
    public void Map_onto_a_null_destination_maps_into_a_new_object()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<Person, PersonDto>());
        var internalMapper = NewMapper<Person, PersonDto>(registryProvider);

        var result = internalMapper.Map(new Person { Name = "Alice", Age = 30 }, (PersonDto)null!);

        result.ShouldNotBeNull();
        result.Name.ShouldBe("Alice");
        result.Age.ShouldBe(30);
    }

    [Fact]
    public void Map_of_a_tracked_pair_maps_a_shared_child_once()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<TreeNode, TreeNodeDto>());
        var internalMapper = NewMapper<TreeNode, TreeNodeDto>(registryProvider);
        var shared = new TreeNode { Name = "shared" };

        var result = internalMapper.Map(new TreeNode { Name = "root", Left = shared, Right = shared });

        result.Left.Name.ShouldBe("shared");
        result.Right.ShouldBeSameAs(result.Left);
    }

    [Fact]
    public void Map_of_a_tracked_pair_keeps_a_cycle_that_goes_through_several_objects()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<TreeNode, TreeNodeDto>());
        var internalMapper = NewMapper<TreeNode, TreeNodeDto>(registryProvider);
        var root = new TreeNode { Name = "root" };
        root.Left = new TreeNode { Name = "child", Right = root };

        var result = internalMapper.Map(root);

        result.Left.Name.ShouldBe("child");
        result.Left.Right.ShouldBeSameAs(result);
    }

    [Fact]
    public void Map_onto_an_existing_destination_of_a_tracked_pair_reuses_that_destination_for_cycles()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<TreeNode, TreeNodeDto>());
        var internalMapper = NewMapper<TreeNode, TreeNodeDto>(registryProvider);
        var loop = new TreeNode { Name = "loop" };
        loop.Left = loop;
        var destination = new TreeNodeDto { Name = "old", Left = new TreeNodeDto { Name = "old child" } };

        var result = internalMapper.Map(loop, destination);

        result.ShouldBeSameAs(destination);
        destination.Name.ShouldBe("loop");
        destination.Left.ShouldBeSameAs(destination);
        destination.Right.ShouldBeNull();
    }

    [Fact]
    public void Each_top_level_call_starts_with_a_fresh_context()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<TreeNode, TreeNodeDto>());
        var internalMapper = NewMapper<TreeNode, TreeNodeDto>(registryProvider);
        var source = new TreeNode { Name = "same source" };

        var first = internalMapper.Map(source);
        var second = internalMapper.Map(source);

        second.ShouldNotBeSameAs(first);
    }

    [Fact]
    public void Map_onto_an_existing_destination_throws_a_catchable_exception_when_nesting_exceeds_the_maximum_depth()
    {
        var registryProvider = BuildRegistryProvider(p => p.Map<TreeNode, TreeNodeDto>());
        var internalMapper = NewMapper<TreeNode, TreeNodeDto>(registryProvider);
        var deep = new TreeNode();
        for (var i = 0; i < MappingContext.MaxDepth + 10; i++) deep = new TreeNode { Left = deep };

        Should.Throw<InvalidOperationException>(() => internalMapper.Map(deep, new TreeNodeDto()))
            .Message.ShouldContain("maximum nesting depth");
    }

    [Fact]
    public void Map_of_a_pair_outside_any_cycle_does_not_track_references()
    {
        // Tracking is only paid for by pairs in a type-level cycle: a shared child is mapped once per member.
        var registryProvider = BuildRegistryProvider(p => p.Map<Trip, TripDto>(), p => p.Map<Place, PlaceDto>());
        var internalMapper = NewMapper<Trip, TripDto>(registryProvider);
        var hanoi = new Place { City = "Hanoi" };

        var result = internalMapper.Map(new Trip { From = hanoi, To = hanoi });

        result.From.City.ShouldBe("Hanoi");
        result.To.City.ShouldBe("Hanoi");
        result.To.ShouldNotBeSameAs(result.From);
    }
}
