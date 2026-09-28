using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using MapperR.Core.Abstractions;
using MapperR.Core.Implementations;
using MapperR.Core.Registries;
using MapperR.Generator.Printing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MapperR.Generator;

internal sealed record SkippedPair(Type Source, Type Destination, string Reason);

internal sealed record GenerationResult(string Code, int GeneratedCount, IReadOnlyList<SkippedPair> Skipped);

/// <summary>
/// Turns every type pair of a <see cref="WireProfileRegistry"/> into a generated
/// <see cref="IInternalMapper{TSource,TDestination}"/> class plus an <c>AddGeneratedMappers()</c> extension.
/// Pairs that cannot be generated are skipped (they keep using the runtime engine).
/// </summary>
internal static class MapperCodeGenerator
{
    public const string FileName = "MapperR.Generated.g.cs";

    public static GenerationResult Generate(WireProfileRegistry registry, Assembly targetAssembly, string @namespace)
    {
        var mappers = new List<(string ClassName, string SourceType, string DestinationType, string Code)>();
        var skipped = new List<SkippedPair>();
        var usedClassNames = new HashSet<string>();

        // Deterministic order, so re-generating unchanged profiles yields byte-identical output (maprgen verify).
        var profiles = registry.Profiles
            .OrderBy(profile => profile.SourceType.FullName, StringComparer.Ordinal)
            .ThenBy(profile => profile.DestinationType.FullName, StringComparer.Ordinal);

        foreach (var profile in profiles)
        {
            try
            {
                var expression = BuildExpression(profile, registry);
                var printer = new CSharpExpressionPrinter(targetAssembly);
                var sourceType = printer.PrintType(profile.SourceType);
                var destinationType = printer.PrintType(profile.DestinationType);
                var body = printer.PrintBody(expression);
                var parameter = printer.GetParameterName(expression.Parameters[0]);

                var className = UniqueClassName(profile, usedClassNames);
                mappers.Add((className, sourceType, destinationType,
                    MapperClass(className, sourceType, destinationType, parameter, body)));
            }
            catch (UnsupportedExpressionException exception)
            {
                skipped.Add(new SkippedPair(profile.SourceType, profile.DestinationType, exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                skipped.Add(new SkippedPair(profile.SourceType, profile.DestinationType, exception.Message));
            }
        }

        return new GenerationResult(Format(FileText(@namespace, mappers)), mappers.Count, skipped);
    }

    /// <summary>Invokes <c>TypeMapExpressionBuilder&lt;TSource,TDestination&gt;.Build</c> for runtime-known types.</summary>
    private static LambdaExpression BuildExpression(IWireProfile profile, WireProfileRegistry registry)
    {
        Type builderType;
        try
        {
            builderType = typeof(TypeMapExpressionBuilder<,>).MakeGenericType(profile.SourceType, profile.DestinationType);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                $"{profile.DestinationType.Name} has no public parameterless constructor");
        }

        try
        {
            return (LambdaExpression)builderType.GetMethod(nameof(TypeMapExpressionBuilder<object, object>.Build))!
                .Invoke(null, [registry])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException inner)
        {
            throw inner;
        }
    }

    private static string UniqueClassName(IWireProfile profile, HashSet<string> usedClassNames)
    {
        var baseName = $"{Sanitize(profile.SourceType.Name)}_To_{Sanitize(profile.DestinationType.Name)}_Mapper";
        var name = baseName;
        for (var suffix = 2; !usedClassNames.Add(name); suffix++) name = $"{baseName}{suffix}";
        return name;
    }

    private static string Sanitize(string name) =>
        new([.. name.Select(character => char.IsLetterOrDigit(character) ? character : '_')]);

    private static string MapperClass(string className, string sourceType, string destinationType, string parameter,
        string body) =>
        $$"""
          internal sealed class {{className}}
              : global::MapperR.Core.Abstractions.AbstractInternalMapper<{{destinationType}}>,
                global::MapperR.Core.Abstractions.IInternalMapper<{{sourceType}}, {{destinationType}}>
          {
              public {{destinationType}} Map({{sourceType}} {{parameter}})
              {
                  global::System.ArgumentNullException.ThrowIfNull({{parameter}});
                  return {{body}};
              }

              public override {{destinationType}} MapInternal(object {{parameter}}) => Map(({{sourceType}}){{parameter}});
          }
          """;

    private static string FileText(string @namespace,
        List<(string ClassName, string SourceType, string DestinationType, string Code)> mappers)
    {
        var builder = new StringBuilder()
            .AppendLine("// <auto-generated>")
            .AppendLine("//     Generated by maprgen from MapperR profiles. Do not edit by hand: re-run")
            .AppendLine("//     `maprgen generate` after changing a profile (`maprgen verify` checks this in CI).")
            .AppendLine("// </auto-generated>")
            .AppendLine("#nullable disable")
            .AppendLine($"namespace {@namespace};");

        foreach (var mapper in mappers) builder.AppendLine(mapper.Code);

        const string services = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
        builder.AppendLine("public static class MapperRGeneratedExtensions")
            .AppendLine("{")
            .AppendLine("/// <summary>Registers the generated mappers; pairs not listed here keep using the runtime engine.</summary>")
            .AppendLine($"public static {services} AddGeneratedMappers(this {services} services)")
            .AppendLine("{");

        foreach (var mapper in mappers)
            builder.AppendLine(
                "global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<" +
                $"global::MapperR.Core.Abstractions.IInternalMapper<{mapper.SourceType}, {mapper.DestinationType}>, " +
                $"global::{@namespace}.{mapper.ClassName}>(services);");

        return builder.AppendLine("return services;").AppendLine("}").AppendLine("}").ToString();
    }

    private static string Format(string code) =>
        CSharpSyntaxTree.ParseText(code).GetRoot().NormalizeWhitespace(indentation: "    ", eol: "\n").ToFullString()
        + "\n";
}
