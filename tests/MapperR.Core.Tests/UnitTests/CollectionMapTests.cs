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
/// <c>Map&lt;Dst[]&gt;(source)</c> and friends: a collection mapped to a collection with no profile of its own
/// (DESIGN #25). Only <c>CreateMap&lt;Src, Dst&gt;</c> is registered; the collection comes from the element pair.
/// </summary>
public class CollectionMapTests
{
    private enum Level
    {
        Low = 1,
        High = 2
    }

    private sealed class Src
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    private sealed class Dst
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    private sealed class Other
    {
        public string Title { get; set; }
    }

    private sealed class Wrapper
    {
        public string Label { get; set; }
        public Src Inner { get; set; }
    }

    private sealed class WrapperDto
    {
        public string Label { get; set; }
        public Dst Inner { get; set; }
    }

    // A cycle between elements: the pair is tracked, so the elements must share one context.
    private sealed class Node
    {
        public string Name { get; set; }
        public Node Next { get; set; }
    }

    private sealed class NodeDto
    {
        public string Name { get; set; }
        public NodeDto Next { get; set; }
    }

    private sealed class Holder
    {
        public List<Src> Items { get; set; }
    }

    private sealed class QueueHolder
    {
        public Queue<Dst> Items { get; set; }
    }

    private sealed class SuffixProfile(string suffix) : Profile
    {
        public override void AddProfiles()
        {
            CreateMap<Src, Dst>().ForMember(d => d.Name, s => s.Name + suffix);
            CreateMap<Wrapper, WrapperDto>();
            CreateMap<Node, NodeDto>();
        }
    }

    private sealed class HolderProfile : Profile
    {
        public override void AddProfiles()
        {
            CreateMap<Src, Dst>();
            CreateMap<Holder, QueueHolder>();
        }
    }

    private sealed class ExplicitQueueProfile : Profile
    {
        public override void AddProfiles()
        {
            CreateMap<Src, Dst>();
            CreateMap<Holder, QueueHolder>().ForMember(d => d.Items, s => s.Items);
        }
    }

    private static (ServiceProvider Provider, IMapper Mapper) Build(string suffix = "", bool allowNull = false,
        params Profile[] more)
    {
        var services = new ServiceCollection();
        services.AddMapR(cfg => cfg.AllowNullCollections = allowNull);
        services.AddSingleton<IProfile>(new SuffixProfile(suffix));
        foreach (var profile in more) services.AddSingleton<IProfile>(profile);
        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<IMapper>());
    }

    private static List<Src> Two() => [new() { Id = 1, Name = "a" }, new() { Id = 2, Name = "b" }];

    [Fact]
    public void Every_supported_destination_shape_is_built_from_the_element_pair()
    {
        var (provider, mapper) = Build();
        using var _ = provider;
        var source = Two();

        mapper.Map<Dst[]>(source).ShouldBeOfType<Dst[]>().Select(d => d.Name).ShouldBe(["a", "b"]);
        mapper.Map<List<Dst>>(source.ToArray()).ShouldBeOfType<List<Dst>>().Select(d => d.Id).ShouldBe([1, 2]);
        mapper.Map<HashSet<Dst>>(source).ShouldBeOfType<HashSet<Dst>>().Count.ShouldBe(2);

        // Interfaces come back as a List<T>, as with AutoMapper.
        mapper.Map<IEnumerable<Dst>>(source).ShouldBeOfType<List<Dst>>().Count.ShouldBe(2);
        mapper.Map<ICollection<Dst>>(source).ShouldBeOfType<List<Dst>>().Count.ShouldBe(2);
        mapper.Map<IList<Dst>>(source).ShouldBeOfType<List<Dst>>().Count.ShouldBe(2);
        mapper.Map<IReadOnlyCollection<Dst>>(source).ShouldBeOfType<List<Dst>>().Count.ShouldBe(2);
        mapper.Map<IReadOnlyList<Dst>>(source).ShouldBeOfType<List<Dst>>().Count.ShouldBe(2);
    }

    [Fact]
    public void Any_source_collection_works_including_a_lazy_iterator_and_the_untyped_overload()
    {
        var (provider, mapper) = Build();
        using var _ = provider;
        var source = Two();

        mapper.Map<Dst[]>((object)source).Length.ShouldBe(2);
        mapper.Map<Dst[]>(source.Where(s => s.Id > 1)).Select(d => d.Name).ShouldBe(["b"]);
        mapper.Map<Dst[]>(new HashSet<Src>(source)).Length.ShouldBe(2);
        mapper.Map<IEnumerable<Src>, List<Dst>>(source).Count.ShouldBe(2);          // declared source type
        mapper.Map<List<Src>, Dst[]>(source).Length.ShouldBe(2);
        mapper.Map<Dst[]>(new Dictionary<int, Src> { [1] = source[0] }.Values).Length.ShouldBe(1);
    }

    [Fact]
    public void Elements_that_are_scalars_are_converted_with_the_same_rules_as_members()
    {
        var (provider, mapper) = Build();
        using var _ = provider;
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            mapper.Map<long[]>(new List<int> { 1, 2 }).ShouldBe([1L, 2L]);
            mapper.Map<string[]>(new List<double> { 1.5, 2 }).ShouldBe(["1.5", "2"]);   // invariant culture
            mapper.Map<List<int>>(new List<int?> { 1, null, 3 }).ShouldBe([1, 0, 3]);
            mapper.Map<int[]>(new List<Level> { Level.High, Level.Low }).ShouldBe([2, 1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Null_elements_stay_null_and_an_empty_collection_is_an_empty_collection()
    {
        var (provider, mapper) = Build();
        using var _ = provider;

        var result = mapper.Map<List<Dst>>(new List<Src> { new() { Id = 1 }, null });

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[1].ShouldBeNull();
        mapper.Map<Dst[]>(new List<Src>()).ShouldBeEmpty();
    }

    [Fact]
    public void Elements_with_nested_objects_are_mapped_through_their_own_pairs()
    {
        var (provider, mapper) = Build("!");
        using var _ = provider;

        var result = mapper.Map<WrapperDto[]>(new[]
        {
            new Wrapper { Label = "x", Inner = new Src { Id = 7, Name = "n" } }, new Wrapper { Label = "y" }
        });

        result[0].Inner.Name.ShouldBe("n!");
        result[0].Inner.Id.ShouldBe(7);
        result[1].Inner.ShouldBeNull();
    }

    [Fact]
    public void A_collection_of_the_same_element_type_is_a_new_collection_with_the_same_elements()
    {
        var (provider, mapper) = Build();
        using var _ = provider;
        var dsts = new List<Dst> { new() { Id = 1 } };

        var result = mapper.Map<List<Dst>>(dsts);

        result.ShouldNotBeSameAs(dsts);
        result[0].ShouldBeSameAs(dsts[0]);
    }

    [Fact]
    public void A_null_source_collection_maps_to_an_empty_collection_by_default()
    {
        var (provider, mapper) = Build();
        using var _ = provider;

        mapper.Map<List<Src>, Dst[]>(null).ShouldBeEmpty();
        mapper.Map<Dst[]>((object)null).ShouldBeOfType<Dst[]>().ShouldBeEmpty();
        mapper.Map<List<Dst>>((object)null).ShouldBeOfType<List<Dst>>().ShouldBeEmpty();
        mapper.Map<HashSet<Dst>>((object)null).ShouldBeOfType<HashSet<Dst>>().ShouldBeEmpty();
    }

    [Fact]
    public void A_null_source_collection_maps_to_null_when_null_collections_are_allowed()
    {
        var (provider, mapper) = Build(allowNull: true);
        using var _ = provider;

        mapper.Map<List<Src>, Dst[]>(null).ShouldBeNull();
        mapper.Map<Dst[]>((object)null).ShouldBeNull();
    }

    [Fact]
    public void A_null_source_for_a_destination_that_is_not_a_collection_still_throws()
    {
        var (provider, mapper) = Build();
        using var _ = provider;

        Should.Throw<ArgumentNullException>(() => mapper.Map<Dst>((object)null));
    }

    [Fact]
    public void A_collection_and_a_single_object_are_never_mapped_to_each_other()
    {
        var (provider, mapper) = Build();
        using var _ = provider;

        Should.Throw<InvalidOperationException>(() => mapper.Map<Dst>(Two()))
            .Message.ShouldContain("a collection cannot be mapped to a single object");
        Should.Throw<InvalidOperationException>(() => mapper.Map<List<Dst>>(new Src()))
            .Message.ShouldContain("a collection cannot be mapped to a single object");
    }

    [Fact]
    public void A_destination_collection_type_that_is_not_supported_is_reported_clearly()
    {
        var (provider, mapper) = Build();
        using var _ = provider;

        var error = Should.Throw<InvalidOperationException>(() => mapper.Map<Queue<Dst>>(Two()));

        error.Message.ShouldContain("Queue<Dst>");
        error.Message.ShouldContain("is not supported");
    }

    [Fact]
    public void Nested_collections_are_reported_as_not_supported_yet()
    {
        var (provider, mapper) = Build();
        using var _ = provider;

        Should.Throw<InvalidOperationException>(() => mapper.Map<Dst[][]>(new List<List<Src>> { Two() }))
            .Message.ShouldContain("nested collections are not supported yet");
    }

    [Fact]
    public void Elements_that_have_no_profile_are_reported_with_the_missing_pair()
    {
        var (provider, mapper) = Build();
        using var _ = provider;

        var error = Should.Throw<InvalidOperationException>(() => mapper.Map<Other[]>(Two()));

        error.Message.ShouldContain("elements:");
        error.Message.ShouldContain("CreateMap<Src, Other>()");
    }

    [Fact]
    public void Mapping_onto_an_existing_collection_is_not_supported_yet()
    {
        var (provider, mapper) = Build();
        using var _ = provider;

        Should.Throw<NotSupportedException>(() => mapper.Map(Two(), new List<Dst>()))
            .Message.ShouldContain("existing List<Dst>");
        Should.Throw<NotSupportedException>(() => mapper.Map<List<Dst>>((object)Two(), new List<Dst>()));
        Should.Throw<NotSupportedException>(() => mapper.Map(Two().ToArray(), new Dst[1]));
    }

    [Fact]
    public void Elements_that_reference_each_other_share_one_context_so_shared_objects_stay_shared()
    {
        var (provider, mapper) = Build();
        using var _ = provider;
        var a = new Node { Name = "a" };
        var b = new Node { Name = "b" };
        a.Next = b;
        b.Next = a;

        var result = mapper.Map<NodeDto[]>(new[] { a, b });

        result[0].Next.ShouldBeSameAs(result[1]);
        result[1].Next.ShouldBeSameAs(result[0]);
    }

    [Fact]
    public void Repeated_calls_and_the_untyped_and_typed_overloads_agree()
    {
        var (provider, mapper) = Build("!");
        using var _ = provider;

        for (var i = 0; i < 3; i++)
        {
            mapper.Map<Dst[]>(Two()).Select(d => d.Name).ShouldBe(["a!", "b!"]);
            mapper.Map<List<Src>, Dst[]>(Two()).Select(d => d.Name).ShouldBe(["a!", "b!"]);
            mapper.Map<Dst[]>((object)Two().ToArray()).Select(d => d.Name).ShouldBe(["a!", "b!"]);
        }
    }

    [Fact]
    public void Providers_with_different_profiles_never_share_collection_mappers()
    {
        var (firstProvider, first) = Build("-first");
        var (secondProvider, second) = Build("-second");
        using var _ = firstProvider;
        using var __ = secondProvider;

        for (var i = 0; i < 3; i++)
        {
            first.Map<Dst[]>(Two()).Select(d => d.Name).ShouldBe(["a-first", "b-first"]);
            second.Map<Dst[]>(Two()).Select(d => d.Name).ShouldBe(["a-second", "b-second"]);
        }
    }

    [Fact]
    public void A_convention_member_with_an_unsupported_collection_destination_is_skipped()
    {
        var (provider, mapper) = Build(more: new HolderProfile());
        using var _ = provider;

        var result = mapper.Map<Holder, QueueHolder>(new Holder { Items = Two() });

        result.Items.ShouldBeNull();       // skipped, like any convention member that cannot be mapped
    }

    [Fact]
    public void An_explicit_member_with_an_unsupported_collection_destination_throws_with_the_reason()
    {
        var (provider, mapper) = Build(more: new ExplicitQueueProfile());
        using var _ = provider;

        var error = Should.Throw<InvalidOperationException>(() =>
            mapper.Map<Holder, QueueHolder>(new Holder { Items = Two() }));

        error.Message.ShouldContain("member 'Items' cannot be mapped");
        error.Message.ShouldContain("Queue<Dst>");
        error.Message.ShouldContain("is not supported");
    }

    // The flags enum is internal, so the theory passes its numeric value.
    public static TheoryData<int, bool> OptimizationCombinations()
    {
        var data = new TheoryData<int, bool>();
        foreach (var flags in Enumerable.Range(0, (int)MapperOptimizations.All + 1))
        foreach (var allowNull in new[] { false, true })
            data.Add(flags, allowNull);

        return data;
    }

    [Theory]
    [MemberData(nameof(OptimizationCombinations))]
    public void Every_combination_of_optimization_steps_gives_the_same_collection(int flags, bool allowNull)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProfile>(new SuffixProfile("!"));
        var registry = new RegistryProvider(services.BuildServiceProvider()).ProfileRegistry;
        var json = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve };

        CollectionMapper<IEnumerable<TSource>, TDestination> New<TSource, TDestination>(MapperOptimizations o) =>
            new(registry, new RegistryMapperResolver(registry, o, allowNull), o);

        var a = new Node { Name = "a" };
        var b = new Node { Name = "b", Next = a };
        a.Next = b;
        var wrappers = new[] { new Wrapper { Label = "x", Inner = Two()[0] }, null, new Wrapper() };

        string Map<TSource, TDestination>(MapperOptimizations o, IEnumerable<TSource> source) =>
            JsonSerializer.Serialize(New<TSource, TDestination>(o).Map(source), json);

        foreach (var (plain, optimized) in new[]
                 {
                     (Map<Node, NodeDto[]>(MapperOptimizations.None, [a, b, null]),
                         Map<Node, NodeDto[]>((MapperOptimizations)flags, [a, b, null])),
                     (Map<Wrapper, List<WrapperDto>>(MapperOptimizations.None, wrappers),
                         Map<Wrapper, List<WrapperDto>>((MapperOptimizations)flags, wrappers)),
                     (Map<int, HashSet<string>>(MapperOptimizations.None, null),       // a null source collection
                         Map<int, HashSet<string>>((MapperOptimizations)flags, null))
                 })
            optimized.ShouldBe(plain);
    }
}
