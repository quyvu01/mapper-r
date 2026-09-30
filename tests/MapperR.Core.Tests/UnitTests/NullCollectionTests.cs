using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MapperR.Core.Tests.UnitTests;

/// <summary>
/// What a null source collection maps to (DESIGN #26): an empty collection of the destination shape by default,
/// <c>null</c> when <c>AllowNullCollections</c> is on. Applies to every collection shape a member can have.
/// </summary>
public class NullCollectionTests
{
    private sealed class Item
    {
        public string Name { get; set; }
    }

    private sealed class ItemDto
    {
        public string Name { get; set; }
    }

    private sealed class Source
    {
        public List<Item> Items { get; set; }
        public Item[] Array { get; set; }
        public List<Item> Set { get; set; }
        public IEnumerable<Item> Sequence { get; set; }
        public List<Item> ReadOnly { get; set; }
        public List<int> Numbers { get; set; }
        public List<string> Words { get; set; }
    }

    private sealed class Destination
    {
        public List<ItemDto> Items { get; set; }
        public ItemDto[] Array { get; set; }
        public HashSet<ItemDto> Set { get; set; }
        public IEnumerable<ItemDto> Sequence { get; set; }
        public IReadOnlyList<ItemDto> ReadOnly { get; set; }
        public long[] Numbers { get; set; }
        public HashSet<string> Words { get; set; }
    }

    // Not part of a cycle, so the runtime engine builds it into its parent's tree.
    private sealed class Parent
    {
        public Source Child { get; set; }
    }

    private sealed class ParentDto
    {
        public Destination Child { get; set; }
    }

    private sealed class NullCollectionProfile : Profile
    {
        public override void AddProfiles()
        {
            CreateMap<Item, ItemDto>();
            CreateMap<Source, Destination>();
            CreateMap<Parent, ParentDto>();
        }
    }

    private static (ServiceProvider Provider, IMapper Mapper) BuildMapper(bool allowNullCollections)
    {
        var services = new ServiceCollection();
        services.AddMapR(cfg => cfg.AllowNullCollections = allowNullCollections);
        services.AddSingleton<IProfile>(new NullCollectionProfile());
        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<IMapper>());
    }

    private static Destination Filled() => new()
    {
        Items = [new ItemDto { Name = "old" }], Array = [new ItemDto()], Set = [new ItemDto()],
        Sequence = new List<ItemDto> { new() }, ReadOnly = new List<ItemDto> { new() }, Numbers = [1L],
        Words = ["old"]
    };

    [Fact]
    public void By_default_a_null_collection_maps_to_an_empty_one_of_the_destination_shape()
    {
        var (provider, mapper) = BuildMapper(allowNullCollections: false);
        using var _ = provider;

        var result = mapper.Map<Source, Destination>(new Source());

        result.Items.ShouldBeOfType<List<ItemDto>>().ShouldBeEmpty();
        result.Array.ShouldBeOfType<ItemDto[]>().ShouldBeEmpty();
        result.Set.ShouldBeOfType<HashSet<ItemDto>>().ShouldBeEmpty();
        result.Sequence.ShouldNotBeNull().ShouldBeEmpty();
        result.ReadOnly.ShouldNotBeNull().ShouldBeEmpty();
        result.Numbers.ShouldBeOfType<long[]>().ShouldBeEmpty();
        result.Words.ShouldBeOfType<HashSet<string>>().ShouldBeEmpty();
    }

    [Fact]
    public void With_null_collections_allowed_every_null_collection_stays_null()
    {
        var (provider, mapper) = BuildMapper(allowNullCollections: true);
        using var _ = provider;

        var result = mapper.Map<Source, Destination>(new Source());

        result.Items.ShouldBeNull();
        result.Array.ShouldBeNull();
        result.Set.ShouldBeNull();
        result.Sequence.ShouldBeNull();
        result.ReadOnly.ShouldBeNull();
        result.Numbers.ShouldBeNull();
        result.Words.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_collection_that_has_items_or_is_empty_is_mapped_the_same_whatever_the_setting(bool allowNull)
    {
        var (provider, mapper) = BuildMapper(allowNull);
        using var _ = provider;

        var result = mapper.Map<Source, Destination>(new Source
        {
            Items = [new Item { Name = "a" }, null], Array = [], Numbers = [1, 2], Words = ["x", "x"]
        });

        result.Items.Select(item => item?.Name).ShouldBe(["a", null]);
        result.Array.ShouldBeEmpty();
        result.Numbers.ShouldBe([1L, 2L]);
        result.Words.ShouldBe(["x"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_update_replaces_an_existing_collection_with_the_empty_or_null_result(bool allowNull)
    {
        var (provider, mapper) = BuildMapper(allowNull);
        using var _ = provider;
        var destination = Filled();

        mapper.Map(new Source(), destination);

        if (allowNull)
        {
            destination.Items.ShouldBeNull();
            destination.Array.ShouldBeNull();
            destination.Set.ShouldBeNull();
            destination.Numbers.ShouldBeNull();
        }
        else
        {
            destination.Items.ShouldBeEmpty();
            destination.Array.ShouldBeEmpty();
            destination.Set.ShouldBeEmpty();
            destination.Numbers.ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_null_collection_inside_a_nested_object_follows_the_setting_too(bool allowNull)
    {
        var (provider, mapper) = BuildMapper(allowNull);
        using var _ = provider;

        var created = mapper.Map<Parent, ParentDto>(new Parent { Child = new Source() });
        var existing = new ParentDto { Child = Filled() };
        var existingChild = existing.Child;
        mapper.Map(new Parent { Child = new Source() }, existing);

        (created.Child.Items is null).ShouldBe(allowNull);
        (created.Child.Array is null).ShouldBe(allowNull);
        existing.Child.ShouldBeSameAs(existingChild);              // still updated in place
        (existing.Child.Items is null).ShouldBe(allowNull);
        (existing.Child.Set is null).ShouldBe(allowNull);
    }

    [Fact]
    public void An_empty_collection_is_a_fresh_instance_each_time_so_callers_can_add_to_it()
    {
        var (provider, mapper) = BuildMapper(allowNullCollections: false);
        using var _ = provider;

        var first = mapper.Map<Source, Destination>(new Source());
        var second = mapper.Map<Source, Destination>(new Source());
        first.Items.Add(new ItemDto { Name = "added" });
        first.Set.Add(new ItemDto());

        second.Items.ShouldBeEmpty();
        second.Set.ShouldBeEmpty();
    }
}
