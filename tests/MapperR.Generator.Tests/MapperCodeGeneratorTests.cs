using System.Text.Json;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using MapperR.Core.Implementations;
using MapperR.Generator.Tests.Models;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MapperR.Generator.Tests;

public class MapperCodeGeneratorTests
{
    private static readonly Action<DelegateProfile> AddressMap = p => p.Map<Address, AddressDto>();

    private static readonly Action<DelegateProfile> PersonMap = p => p.Map<Person, PersonDto>()
        .ForMember(d => d.Name, s => $"{s.Name}-SomeOtherValue")
        .ForMember(d => d.Address, s => s.Address);

    [Fact]
    public void Generated_file_compiles_and_AddGeneratedMappers_overrides_the_runtime_mapper()
    {
        var registry = DelegateProfile.BuildRegistry(AddressMap, PersonMap);
        var result = MapperCodeGenerator.Generate(registry, targetAssembly: null, "Test.Generated");

        var generatedAssembly = InMemoryCompiler.CompileAssembly(result.Code,
            typeof(Profile).Assembly, typeof(IServiceCollection).Assembly);

        var services = new ServiceCollection();
        services.AddMapR(_ => { });
        services.AddSingleton<IProfile>(new DelegateProfile(AddressMap));
        services.AddSingleton<IProfile>(new DelegateProfile(PersonMap));
        generatedAssembly.GetType("Test.Generated.MapperRGeneratedExtensions")!
            .GetMethod("AddGeneratedMappers")!.Invoke(null, [services]);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IInternalMapper<Person, PersonDto>>().GetType().Assembly
            .ShouldBe(generatedAssembly);

        var person = new Person { Name = "Alice", Address = new Address { City = "Hanoi" } };
        var runtime = TypeMapExpressionBuilder<Person, PersonDto>.Build(registry).Compile();
        var mapper = provider.GetRequiredService<IMapper>();

        JsonSerializer.Serialize(mapper.Map<Person, PersonDto>(person)).ShouldBe(JsonSerializer.Serialize(runtime(person)));
        JsonSerializer.Serialize(mapper.Map<PersonDto>(person)).ShouldBe(JsonSerializer.Serialize(runtime(person)));
    }

    [Fact]
    public void Skips_pairs_that_cannot_be_generated_and_still_generates_the_rest()
    {
        var registry = DelegateProfile.BuildRegistry(
            AddressMap,
            p => p.Map<Person, PrivateSetterDto>(),
            p => p.Map<Node, NodeDto>().ForMember(d => d.Child, s => s.Child));

        var result = MapperCodeGenerator.Generate(registry, targetAssembly: null, "Test.Generated");

        result.GeneratedCount.ShouldBe(1);
        result.Skipped.Select(skipped => skipped.Destination).ShouldBe([typeof(NodeDto), typeof(PrivateSetterDto)],
            ignoreOrder: true);
        result.Skipped.Single(skipped => skipped.Destination == typeof(PrivateSetterDto)).Reason.ShouldContain("setter");
        result.Skipped.Single(skipped => skipped.Destination == typeof(NodeDto)).Reason.ShouldContain("unbounded recursion");
        result.Code.ShouldContain("Address_To_AddressDto_Mapper");
        result.Code.ShouldNotContain("PrivateSetterDto");
    }

    [Fact]
    public void Output_does_not_depend_on_profile_registration_order()
    {
        var forward = MapperCodeGenerator.Generate(
            DelegateProfile.BuildRegistry(AddressMap, PersonMap), targetAssembly: null, "Test.Generated");
        var reversed = MapperCodeGenerator.Generate(
            DelegateProfile.BuildRegistry(PersonMap, AddressMap), targetAssembly: null, "Test.Generated");

        reversed.Code.ShouldBe(forward.Code);
    }
}
