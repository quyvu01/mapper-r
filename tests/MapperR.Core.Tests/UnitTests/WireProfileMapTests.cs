using MapperR.Core.Implementations;
using Shouldly;
using Xunit;

namespace MapperR.Core.Tests.UnitTests;

public class WireProfileMapTests
{
    private enum SourceStatus
    {
        Active = 1,
        Inactive = 2
    }

    private enum DestinationStatus
    {
        Active = 1,
        Inactive = 2
    }

    private sealed class Source
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public int Score { get; set; }
        public SourceStatus Status { get; set; }
        public object Incompatible { get; set; }
        public string OnlyOnSource { get; set; }
    }

    private sealed class Destination
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public long Score { get; set; }
        public DestinationStatus Status { get; set; }
        public string Incompatible { get; set; }
        public string OnlyOnDestination { get; set; }
        public string NoSetter => "computed";
    }

    private sealed class CaseSource
    {
        public string Title { get; set; }
    }

    private sealed class CaseDestination
    {
        public string TITLE { get; set; }
    }

    private sealed class IndexedSource
    {
        public string Name { get; set; }
        public string this[int index] => index.ToString();
    }

    private sealed class IndexedDestination
    {
        public string Name { get; set; }

        public string this[int index]
        {
            get => index.ToString();
            set { }
        }
    }

    private sealed class NullableSource
    {
        public int? Count { get; set; }
    }

    private sealed class NullableDestination
    {
        public int Count { get; set; }
    }

    [Fact]
    public void MemberProfiles_auto_matches_members_with_the_same_name_and_exact_type()
    {
        var map = new WireProfileMap<Source, Destination>();

        var member = map.MemberProfiles.Single(m => m.MemberName == nameof(Destination.Name));

        member.SourceMemberType.ShouldBe(typeof(string));
        member.DestinationMemberType.ShouldBe(typeof(string));
    }

    [Fact]
    public void MemberProfiles_auto_matches_numeric_widening_conversions()
    {
        var map = new WireProfileMap<Source, Destination>();

        var member = map.MemberProfiles.Single(m => m.MemberName == nameof(Destination.Score));

        member.SourceMemberType.ShouldBe(typeof(int));
        member.DestinationMemberType.ShouldBe(typeof(long));
    }

    [Fact]
    public void MemberProfiles_auto_matches_convertible_enum_types()
    {
        var map = new WireProfileMap<Source, Destination>();

        var member = map.MemberProfiles.Single(m => m.MemberName == nameof(Destination.Status));

        member.SourceMemberType.ShouldBe(typeof(SourceStatus));
        member.DestinationMemberType.ShouldBe(typeof(DestinationStatus));
    }

    [Fact]
    public void MemberProfiles_skips_members_with_incompatible_non_numeric_types()
    {
        var map = new WireProfileMap<Source, Destination>();

        map.MemberProfiles.ShouldNotContain(m => m.MemberName == nameof(Destination.Incompatible));
    }

    [Fact]
    public void MemberProfiles_skips_destination_members_with_no_matching_source_name()
    {
        var map = new WireProfileMap<Source, Destination>();

        map.MemberProfiles.ShouldNotContain(m => m.MemberName == nameof(Destination.OnlyOnDestination));
    }

    [Fact]
    public void MemberProfiles_does_not_duplicate_a_member_explicitly_configured_via_ForMember()
    {
        var map = new WireProfileMap<Source, Destination>();
        map.ForMember(d => d.Name, s => s.Name);

        map.MemberProfiles.Count(m => m.MemberName == nameof(Destination.Name)).ShouldBe(1);
    }

    [Fact]
    public void SourceType_and_DestinationType_reflect_the_generic_arguments()
    {
        var map = new WireProfileMap<Source, Destination>();

        map.SourceType.ShouldBe(typeof(Source));
        map.DestinationType.ShouldBe(typeof(Destination));
    }

    [Fact]
    public void ForMember_returns_the_same_instance_for_fluent_chaining()
    {
        var map = new WireProfileMap<Source, Destination>();

        var result = map.ForMember(d => d.Name, s => s.Name);

        result.ShouldBeSameAs(map);
    }

    [Fact]
    public void ForMember_throws_when_the_destination_selector_is_a_nested_path()
    {
        var map = new WireProfileMap<Source, Destination>();

        Should.Throw<ArgumentException>(() => map.ForMember(d => d.Name.Length, s => s.Age));
    }

    [Fact]
    public void MemberProfiles_matches_names_case_insensitively()
    {
        var map = new WireProfileMap<CaseSource, CaseDestination>();

        var member = map.MemberProfiles.Single(m => m.MemberName == nameof(CaseDestination.TITLE));

        member.SourceMemberType.ShouldBe(typeof(string));
    }

    [Fact]
    public void MemberProfiles_skips_destination_members_without_a_public_setter()
    {
        var map = new WireProfileMap<Source, Destination>();

        map.MemberProfiles.ShouldNotContain(m => m.MemberName == nameof(Destination.NoSetter));
    }

    [Fact]
    public void MemberProfiles_does_not_throw_when_either_type_declares_an_indexer()
    {
        var map = new WireProfileMap<IndexedSource, IndexedDestination>();

        Should.NotThrow(() => map.MemberProfiles);
    }

    [Fact]
    public void MemberProfiles_treats_nullable_to_non_nullable_numeric_conversions_as_compatible()
    {
        var map = new WireProfileMap<NullableSource, NullableDestination>();

        var member = map.MemberProfiles.Single(m => m.MemberName == nameof(NullableDestination.Count));

        member.SourceMemberType.ShouldBe(typeof(int?));
        member.DestinationMemberType.ShouldBe(typeof(int));
    }
}
