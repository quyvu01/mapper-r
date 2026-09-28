using System.Text.Json;
using MapperR.Core.Implementations;
using MapperR.Core.Registries;
using MapperR.Generator.Tests.Models;
using Shouldly;
using Xunit;

namespace MapperR.Generator.Tests;

/// <summary>
/// For each scenario: the runtime engine (compiled expression) and the generated source (printed, compiled
/// by Roslyn) must produce identical results for the same inputs.
/// </summary>
public class EquivalenceTests
{
    private static void AssertEquivalent<TSource, TDestination>(WireProfileRegistry registry, params TSource[] inputs)
        where TDestination : new()
    {
        var expression = TypeMapExpressionBuilder<TSource, TDestination>.Build(registry);
        var runtime = expression.Compile();
        var generated = InMemoryCompiler.Compile(expression);

        foreach (var input in inputs)
            JsonSerializer.Serialize(generated(input)).ShouldBe(JsonSerializer.Serialize(runtime(input)));
    }

    [Fact]
    public void Convention_mapping_with_numeric_widening_and_enum_conversion()
    {
        var registry = DelegateProfile.BuildRegistry(p => p.Map<Person, PersonDto>());

        AssertEquivalent<Person, PersonDto>(registry,
            new Person { Name = "Alice", Age = 30, Status = Status.Inactive },
            new Person());
    }

    [Fact]
    public void Explicit_computed_member_expression()
    {
        var registry = DelegateProfile.BuildRegistry(p => p.Map<Person, PersonDto>()
            .ForMember(d => d.Name, s => $"{s.Name}-SomeOtherValue")
            .ForMember(d => d.Age, s => s.Age * 2 + 1));

        AssertEquivalent<Person, PersonDto>(registry,
            new Person { Name = "Alice", Age = 30 },
            new Person { Name = "Quote \" and \n newline", Age = -5 });
    }

    [Fact]
    public void Nested_object_including_null()
    {
        var registry = DelegateProfile.BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<Person, PersonDto>().ForMember(d => d.Address, s => s.Address));

        AssertEquivalent<Person, PersonDto>(registry,
            new Person { Address = new Address { City = "Hanoi" } },
            new Person { Address = null });
    }

    [Fact]
    public void Collections_of_every_supported_shape_including_null()
    {
        var registry = DelegateProfile.BuildRegistry(
            p => p.Map<Address, AddressDto>(),
            p => p.Map<Person, PersonDto>()
                .ForMember(d => d.Addresses, s => s.Addresses)
                .ForMember(d => d.AddressArray, s => s.AddressArray)
                .ForMember(d => d.AddressSequence, s => s.AddressSequence)
                .ForMember(d => d.Scores, s => s.Scores));

        AssertEquivalent<Person, PersonDto>(registry,
            new Person
            {
                Addresses = [new Address { City = "Hanoi" }, new Address { City = "Saigon" }],
                AddressArray = [new Address { City = "Hue" }],
                AddressSequence = new List<Address> { new() { City = "Danang" } },
                Scores = [1, 2, 3]
            },
            new Person());
    }
}
