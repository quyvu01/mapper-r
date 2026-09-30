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
        var runtimeResolver = new RegistryMapperResolver(registry);
        var runtimeMap = runtimeResolver.Get<Person, PersonDto>();
        PersonDto Runtime(Person source) => runtimeMap.Map(source);
        var mapper = provider.GetRequiredService<IMapper>();

        JsonSerializer.Serialize(mapper.Map<Person, PersonDto>(person)).ShouldBe(JsonSerializer.Serialize(Runtime(person)));
        JsonSerializer.Serialize(mapper.Map<PersonDto>(person)).ShouldBe(JsonSerializer.Serialize(Runtime(person)));

        // Update form, generated vs runtime: the existing nested Address must be updated in place rather than
        // replaced. (Scores, List<int> → HashSet<long>, is mapped by convention, so it follows the source.)
        PersonDto RuntimeUpdate(Person source, PersonDto destination) => runtimeMap.Map(source, destination);
        PersonDto Target() => new() { Age = 99, Scores = [7], Address = new AddressDto { City = "old" } };
        var expected = JsonSerializer.Serialize(RuntimeUpdate(person, Target()));
        var typedTarget = Target();
        var untypedTarget = Target();
        var typedAddress = typedTarget.Address;
        var untypedAddress = untypedTarget.Address;

        mapper.Map(person, typedTarget).ShouldBeSameAs(typedTarget);
        mapper.Map<PersonDto>((object)person, untypedTarget).ShouldBeSameAs(untypedTarget);
        JsonSerializer.Serialize(typedTarget).ShouldBe(expected);
        JsonSerializer.Serialize(untypedTarget).ShouldBe(expected);
        typedTarget.Scores.ShouldBeNull();
        typedTarget.Address.ShouldBeSameAs(typedAddress);
        untypedTarget.Address.ShouldBeSameAs(untypedAddress);
        typedAddress.City.ShouldBe("Hanoi");

        // Source without an address: both engines clear the nested object.
        var withoutAddress = new Person { Name = "Bob" };
        mapper.Map(withoutAddress, typedTarget).Address.ShouldBeNull();
        JsonSerializer.Serialize(typedTarget).ShouldBe(JsonSerializer.Serialize(RuntimeUpdate(withoutAddress, Target())));

        // Null destination: both engines map into a new object instead of throwing.
        var fromNull = mapper.Map(person, (PersonDto)null);
        fromNull.ShouldNotBeNull();
        JsonSerializer.Serialize(fromNull).ShouldBe(JsonSerializer.Serialize(RuntimeUpdate(person, null)));
        JsonSerializer.Serialize(fromNull).ShouldBe(JsonSerializer.Serialize(Runtime(person)));
    }

    [Fact]
    public void Skips_pairs_that_cannot_be_generated_and_still_generates_the_rest()
    {
        var registry = DelegateProfile.BuildRegistry(
            AddressMap,
            p => p.Map<Person, PrivateSetterDto>());

        var result = MapperCodeGenerator.Generate(registry, targetAssembly: null, "Test.Generated");

        result.GeneratedCount.ShouldBe(1);
        result.Skipped.Single().Destination.ShouldBe(typeof(PrivateSetterDto));
        result.Skipped.Single().Reason.ShouldContain("setter");
        result.Code.ShouldContain("Address_To_AddressDto_Mapper");
        result.Code.ShouldNotContain("PrivateSetterDto");
    }

    [Fact]
    public void Generated_mappers_follow_self_references_and_keep_cyclic_data_shape()
    {
        Action<DelegateProfile> nodeMap = p => p.Map<Node, NodeDto>();
        var result = MapperCodeGenerator.Generate(DelegateProfile.BuildRegistry(nodeMap), targetAssembly: null,
            "Test.Generated");
        result.Code.ShouldContain("Node_To_NodeDto_Mapper.MapNestedInto(");   // direct call, not through DI

        using var provider = BuildProviderWithGeneratedMappers(result, nodeMap, out var generatedAssembly);
        var mapper = provider.GetRequiredService<IInternalMapper<Node, NodeDto>>();
        mapper.GetType().Assembly.ShouldBe(generatedAssembly);

        var chain = mapper.Map(new Node { Child = new Node { Child = new Node() } });
        chain.Child.Child.ShouldNotBeNull();
        chain.Child.Child.Child.ShouldBeNull();

        var loop = new Node();
        loop.Child = loop;
        var mappedLoop = mapper.Map(loop);
        mappedLoop.Child.ShouldBeSameAs(mappedLoop);
    }

    [Fact]
    public void A_generated_mapper_falls_back_to_the_runtime_engine_for_a_nested_pair_that_was_not_generated()
    {
        Action<DelegateProfile> holderMap = p => p.Map<Holder, HolderDto>();
        Action<DelegateProfile> skippedChildMap = p => p.Map<Person, PrivateSetterDto>();   // private setter: not generated
        var result = MapperCodeGenerator.Generate(DelegateProfile.BuildRegistry(holderMap, skippedChildMap),
            targetAssembly: null, "Test.Generated");
        result.Code.ShouldContain("MapperRuntime.MapNested<");

        using var provider = BuildProviderWithGeneratedMappers(result, holderMap, out _, skippedChildMap);
        var dto = provider.GetRequiredService<IMapper>().Map<Holder, HolderDto>(
            new Holder { Person = new Person { Name = "Alice" } });

        dto.Person.Name.ShouldBe("Alice");
    }

    private static ServiceProvider BuildProviderWithGeneratedMappers(GenerationResult result,
        Action<DelegateProfile> profile, out System.Reflection.Assembly generatedAssembly,
        params Action<DelegateProfile>[] otherProfiles)
    {
        generatedAssembly = InMemoryCompiler.CompileAssembly(result.Code,
            typeof(Profile).Assembly, typeof(IServiceCollection).Assembly);

        var services = new ServiceCollection();
        services.AddMapR(_ => { });
        foreach (var configure in otherProfiles.Prepend(profile))
            services.AddSingleton<IProfile>(new DelegateProfile(configure));
        generatedAssembly.GetType("Test.Generated.MapperRGeneratedExtensions")!
            .GetMethod("AddGeneratedMappers")!.Invoke(null, [services]);
        return services.BuildServiceProvider();
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
