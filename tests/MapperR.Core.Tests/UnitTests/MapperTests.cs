using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MapperR.Core.Tests.UnitTests;

public class MapperTests
{
    private sealed class Person
    {
        public string Name { get; set; }
    }

    private sealed class PersonDto
    {
        public string Name { get; set; }
    }

    private sealed class SuffixProfile(string suffix) : Profile
    {
        public override void AddProfiles() =>
            CreateMap<Person, PersonDto>().ForMember(d => d.Name, s => s.Name + suffix);
    }

    private static (ServiceProvider Provider, IMapper Mapper) BuildMapper(params Profile[] profiles)
    {
        var services = new ServiceCollection();
        services.AddMapR(_ => { });
        foreach (var profile in profiles) services.AddSingleton<IProfile>(profile);

        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<IMapper>());
    }

    [Fact]
    public void Repeated_calls_keep_returning_correct_results_through_both_overloads()
    {
        var (provider, mapper) = BuildMapper(new SuffixProfile("!"));
        using var _ = provider;

        for (var i = 0; i < 3; i++)
        {
            mapper.Map<Person, PersonDto>(new Person { Name = $"A{i}" }).Name.ShouldBe($"A{i}!");
            mapper.Map<PersonDto>(new Person { Name = $"B{i}" }).Name.ShouldBe($"B{i}!");
        }
    }

    [Fact]
    public void Mappers_from_different_providers_never_use_each_others_cached_mapper()
    {
        var (firstProvider, first) = BuildMapper(new SuffixProfile("-first"));
        var (secondProvider, second) = BuildMapper(new SuffixProfile("-second"));
        using var _ = firstProvider;
        using var __ = secondProvider;
        var person = new Person { Name = "x" };

        // Interleaved on purpose: each call finds the shared typed slot owned by the other mapper.
        for (var i = 0; i < 3; i++)
        {
            first.Map<Person, PersonDto>(person).Name.ShouldBe("x-first");
            second.Map<Person, PersonDto>(person).Name.ShouldBe("x-second");
            first.Map<PersonDto>(person).Name.ShouldBe("x-first");
            second.Map<PersonDto>(person).Name.ShouldBe("x-second");
        }
    }

    [Fact]
    public void A_failed_resolution_is_not_cached()
    {
        var (provider, mapper) = BuildMapper();
        using var _ = provider;
        var person = new Person { Name = "x" };

        for (var i = 0; i < 2; i++)
        {
            Should.Throw<InvalidOperationException>(() => mapper.Map<Person, PersonDto>(person))
                .Message.ShouldContain("No mapping profile registered");
            Should.Throw<InvalidOperationException>(() => mapper.Map<PersonDto>(person))
                .Message.ShouldContain("No mapping profile registered");
        }
    }
}
