using System.Collections.ObjectModel;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MapperR.Core.Tests.UnitTests;

/// <summary>CreateMap rejects pairs that have no meaning, at the call (DESIGN #24).</summary>
public class ProfileValidationTests
{
    public sealed class Src
    {
        public string Name { get; set; }
    }

    public sealed class Dst
    {
        public string Name { get; set; }
    }

    /// <summary>A collection type of our own with a member of its own: still a valid object pair.</summary>
    public sealed class PagedSrc : List<Src>
    {
        public int Total { get; set; }
    }

    public sealed class PagedDst : List<Dst>
    {
        public int Total { get; set; }
    }

    private sealed class Probe(Action<Probe> configure) : Profile
    {
        public override void AddProfiles() => configure(this);

        public void Map<TSource, TDestination>() => CreateMap<TSource, TDestination>();
    }

    private static InvalidOperationException Rejected<TSource, TDestination>() =>
        Should.Throw<InvalidOperationException>(() => new Probe(p => p.Map<TSource, TDestination>()).AddProfiles());

    private static void Accepted<TSource, TDestination>() =>
        Should.NotThrow(() => new Probe(p => p.Map<TSource, TDestination>()).AddProfiles());

    [Fact]
    public void A_list_to_a_list_is_rejected_and_the_message_names_the_element_pair()
    {
        var error = Rejected<List<Src>, List<Dst>>();

        error.Message.ShouldContain("CreateMap<List<Src>, List<Dst>>()");
        error.Message.ShouldContain("element pair");
        error.Message.ShouldContain("CreateMap<Src, Dst>()");
    }

    [Fact]
    public void Every_framework_collection_shape_is_rejected_on_either_side()
    {
        Rejected<Src[], Dst[]>();
        Rejected<List<Src>, Dst[]>();
        Rejected<Src[], List<Dst>>();
        Rejected<IEnumerable<Src>, IEnumerable<Dst>>();
        Rejected<IList<Src>, ICollection<Dst>>();
        Rejected<IReadOnlyList<Src>, IReadOnlyCollection<Dst>>();
        Rejected<HashSet<Src>, HashSet<Dst>>();
        Rejected<List<Src>, HashSet<Dst>>();
        Rejected<Queue<Src>, Stack<Dst>>();
        Rejected<Collection<Src>, ReadOnlyCollection<Dst>>();
        Rejected<Dictionary<int, Src>, Dictionary<int, Dst>>();
    }

    [Fact]
    public void A_collection_of_collections_is_rejected_and_names_the_element_types()
    {
        var error = Rejected<List<List<Src>>, List<Dst[]>>();

        error.Message.ShouldContain("CreateMap<List<Src>, Dst[]>()");
    }

    [Fact]
    public void A_single_object_to_a_collection_or_the_reverse_is_rejected()
    {
        foreach (var error in new[]
                 {
                     Rejected<Src, List<Dst>>(), Rejected<Src, Dst[]>(), Rejected<List<Src>, Dst>(),
                     Rejected<Src[], Dst>()
                 })
            error.Message.ShouldContain("a collection cannot be mapped to a single object");

        Rejected<Src, List<Dst>>().Message.ShouldContain("CreateMap<Src, List<Dst>>()");
    }

    [Fact]
    public void Types_that_derive_from_a_collection_but_are_defined_by_the_user_are_valid_pairs()
    {
        Accepted<PagedSrc, PagedDst>();
        Accepted<Src, PagedDst>();
        Accepted<PagedSrc, Dst>();
    }

    [Fact]
    public void Ordinary_pairs_and_strings_are_not_collections()
    {
        Accepted<Src, Dst>();
        Accepted<string, string>();    // string implements IEnumerable<char> but is a scalar
        Accepted<Src, string>();
    }

    [Fact]
    public void A_rejected_pair_is_reported_when_the_mapper_first_reads_its_profiles()
    {
        var services = new ServiceCollection();
        services.AddMapR(_ => { });
        services.AddSingleton<IProfile>(new Probe(p => p.Map<List<Src>, List<Dst>>()));
        using var provider = services.BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<IMapper>()
            .Map<List<Dst>>(new List<Src>())).Message.ShouldContain("element pair");
    }
}
