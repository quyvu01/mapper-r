using System.Linq.Expressions;
using MapperR.Core.Implementations;
using MapperR.Generator.Printing;
using MapperR.Generator.Tests.Models;
using Shouldly;
using Xunit;

namespace MapperR.Generator.Tests;

public class CSharpExpressionPrinterTests
{
    private sealed class PrivateNestedDto
    {
        public string Name { get; set; }
    }

    private static readonly CSharpExpressionPrinter Printer = new(targetAssembly: null);

    [Theory]
    [InlineData(typeof(List<int>), "global::System.Collections.Generic.List<int>")]
    [InlineData(typeof(Dictionary<string, int[]>), "global::System.Collections.Generic.Dictionary<string, int[]>")]
    [InlineData(typeof(int?), "int?")]
    [InlineData(typeof(int[,]), "int[,]")]
    [InlineData(typeof(Outer<int>.Inner<string>), "global::MapperR.Generator.Tests.Models.Outer<int>.Inner<string>")]
    public void PrintType_produces_valid_fully_qualified_source_names(Type type, string expected) =>
        Printer.PrintType(type).ShouldBe(expected);

    [Fact]
    public void Print_writes_literals_with_correct_suffixes_and_escaping()
    {
        Printer.Print(Expression.Constant(5L)).ShouldBe("5L");
        Printer.Print(Expression.Constant(1.5m)).ShouldBe("1.5M");
        Printer.Print(Expression.Constant((byte)3)).ShouldBe("((byte)(3))");
        Printer.Print(Expression.Constant("a\"b\n")).ShouldBe("\"a\\\"b\\n\"");
        Printer.Print(Expression.Constant(Status.Inactive))
            .ShouldBe("((global::MapperR.Generator.Tests.Models.Status)(2))");
    }

    [Fact]
    public void Throws_for_a_member_expression_that_captures_a_variable()
    {
        var suffix = "-X";
        var registry = DelegateProfile.BuildRegistry(p => p.Map<Person, PersonDto>()
            .ForMember(d => d.Name, s => s.Name + suffix));
        var expression = TypeMapExpressionBuilder<Person, PersonDto>.Build(registry);

        Should.Throw<UnsupportedExpressionException>(() => new CSharpExpressionPrinter(null).PrintBody(expression))
            .Message.ShouldContain("closure");
    }

    [Fact]
    public void Throws_when_a_destination_setter_is_private()
    {
        var registry = DelegateProfile.BuildRegistry(p => p.Map<Person, PrivateSetterDto>());
        var expression = TypeMapExpressionBuilder<Person, PrivateSetterDto>.Build(registry);

        Should.Throw<UnsupportedExpressionException>(() => new CSharpExpressionPrinter(null).PrintBody(expression))
            .Message.ShouldContain("setter");
    }

    [Fact]
    public void Throws_when_the_destination_type_is_not_accessible()
    {
        var registry = DelegateProfile.BuildRegistry(p => p.Map<Person, PrivateNestedDto>());
        var expression = TypeMapExpressionBuilder<Person, PrivateNestedDto>.Build(registry);

        Should.Throw<UnsupportedExpressionException>(() => new CSharpExpressionPrinter(null).PrintBody(expression))
            .Message.ShouldContain("not accessible");
    }

    [Fact]
    public void Throws_for_an_unsupported_expression_node()
    {
        var variable = Expression.Variable(typeof(int), "x");
        var block = Expression.Block([variable], Expression.Assign(variable, Expression.Constant(1)));

        Should.Throw<UnsupportedExpressionException>(() => Printer.Print(block))
            .Message.ShouldContain("Block");
    }
}
