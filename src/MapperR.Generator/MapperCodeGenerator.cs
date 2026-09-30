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
        var skipped = new List<SkippedPair>();
        var usedClassNames = new HashSet<string>();
        var plans = new List<MapperPlan>();

        // Deterministic order, so re-generating unchanged profiles yields byte-identical output (maprgen verify).
        var profiles = registry.Profiles
            .OrderBy(profile => profile.SourceType.FullName, StringComparer.Ordinal)
            .ThenBy(profile => profile.DestinationType.FullName, StringComparer.Ordinal);

        // Pass 1: find the pairs that can be generated at all (nested calls still go through MapperRuntime here).
        foreach (var profile in profiles)
        {
            try
            {
                var map = InvokeBuilder(profile, registry, nameof(TypeMapExpressionBuilder<object, object>.Build));
                var update = InvokeBuilder(profile, registry,
                    nameof(TypeMapExpressionBuilder<object, object>.BuildUpdate));

                // Whether the pair touches its context is decided on the builder's trees: the hoisted form below
                // always passes the context on to the collection helper, even when no element uses it.
                var usesContext = ParameterUsageVisitor.Uses(map, map.Parameters[1]) ||
                                  ParameterUsageVisitor.Uses(update, update.Parameters[2]);

                // The same collection rewrite the runtime engine applies: no LINQ iterator or closure per call.
                map = RuntimeExpressionOptimizer.HoistForPrinting(map);
                update = RuntimeExpressionOptimizer.HoistForPrinting(update);

                var probe = new CSharpExpressionPrinter(targetAssembly);
                probe.PrintType(profile.SourceType);
                probe.PrintType(profile.DestinationType);
                probe.PrintBody(map);
                new CSharpExpressionPrinter(targetAssembly).PrintUpdateBody(update);

                plans.Add(new MapperPlan(profile, UniqueClassName(profile, usedClassNames), map, update,
                    registry.TracksReferences(profile), registry.ReachesCycle(profile), usesContext));
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

        // Pass 2: print, with nested calls to generated pairs going straight to their class. A pair printable in
        // pass 1 stays printable: only the target of its nested calls changes.
        var generatedMappers = plans.ToDictionary(
            plan => (plan.Profile.SourceType, plan.Profile.DestinationType),
            plan => $"global::{@namespace}.{plan.ClassName}");
        var mappers = plans.Select(plan => PrintMapper(plan, targetAssembly, generatedMappers)).ToList();

        return new GenerationResult(Format(FileText(@namespace, mappers)), mappers.Count, skipped);
    }

    private sealed record MapperPlan(
        IWireProfile Profile,
        string ClassName,
        LambdaExpression Map,
        LambdaExpression Update,
        bool TracksReferences,
        bool ReachesCycle,
        bool UsesContext);

    private static (string ClassName, string SourceType, string DestinationType, string Code) PrintMapper(
        MapperPlan plan, Assembly targetAssembly,
        IReadOnlyDictionary<(Type Source, Type Destination), string> generatedMappers)
    {
        var printer = new CSharpExpressionPrinter(targetAssembly, generatedMappers);
        var sourceType = printer.PrintType(plan.Profile.SourceType);
        var destinationType = printer.PrintType(plan.Profile.DestinationType);
        var map = new PrintedLambda(printer.PrintBody(plan.Map),
            printer.GetParameterName(plan.Map.Parameters[0]),
            Context: printer.GetParameterName(plan.Map.Parameters[1]));

        var updatePrinter = new CSharpExpressionPrinter(targetAssembly, generatedMappers);
        var update = new PrintedLambda(updatePrinter.PrintUpdateBody(plan.Update),
            updatePrinter.GetParameterName(plan.Update.Parameters[0]),
            updatePrinter.GetParameterName(plan.Update.Parameters[1]),
            updatePrinter.GetParameterName(plan.Update.Parameters[2]));

        // Same rule as the runtime InternalMapper: a pair without nested members never needs a context, and a pair
        // that cannot reach a cycle needs no depth guard or tracking, so it uses the shared stateless context.
        var contextExpression = plan.TracksReferences || (plan.UsesContext && plan.ReachesCycle) ? "CreateContext()"
            : plan.UsesContext ? "SharedContext"
            : $"({ContextType})null"; // typed: a bare null is ambiguous between Map(source, destination) and Map(source, context)

        var code = MapperClass(plan, sourceType, destinationType, map, update, contextExpression);
        return (plan.ClassName, sourceType, destinationType, code);
    }

    /// <summary>
    /// Invokes <c>TypeMapExpressionBuilder&lt;TSource,TDestination&gt;.Build</c> or <c>.BuildUpdate</c> for
    /// runtime-known types.
    /// </summary>
    private static LambdaExpression InvokeBuilder(IWireProfile profile, WireProfileRegistry registry,
        string methodName)
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
            return (LambdaExpression)builderType.GetMethod(methodName)!.Invoke(null, [registry])!;
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

    /// <param name="Body">An expression for the map form; statements ending in a return for the update form.</param>
    private sealed record PrintedLambda(string Body, string Source, string Destination = null, string Context = null);

    private const string ContextType = "global::MapperR.Core.Abstractions.MappingContext";

    /// <summary>
    /// Mirrors the runtime <c>InternalMapper</c>: public entry points validate and create a context when needed, and
    /// an update onto a null destination maps into a new object;
    /// <c>MapNested</c>/<c>MapNestedInto</c> are what other generated mappers call for nested members (same null
    /// semantics as <c>MappingContext.Map</c>/<c>MapInto</c>); pairs that can form cycles register each new object
    /// before mapping its members (<c>MapCore</c> then uses the update form).
    /// </summary>
    private static string MapperClass(MapperPlan plan, string sourceType, string destinationType,
        PrintedLambda map, PrintedLambda update, string newContext)
    {
        var sourceCanBeNull = CanBeNull(plan.Profile.SourceType);
        var destinationCanBeNull = CanBeNull(plan.Profile.DestinationType);
        var nullSourceGuard = sourceCanBeNull ? $"source == null ? default({destinationType}) : " : "";
        var nullDestinationGuard = destinationCanBeNull ? "destination == null ? MapCore(source, context) : " : "";

        var mapCore = plan.TracksReferences
            ? $$"""
                if ({{map.Context}}.TryGetVisited({{map.Source}}, out {{destinationType}} existing)) return existing;
                return MapMembers({{map.Source}}, {{map.Context}}.Register({{map.Source}}, new {{destinationType}}()), {{map.Context}});
                """
            : $"return {map.Body};";
        var mapIntoCoreTracking = plan.TracksReferences
            ? $$"""
                if (context.TryGetVisited(source, out {{destinationType}} existing)) return existing;
                context.Register(source, destination);
                """
            : "";

        return $$"""
                 internal sealed class {{plan.ClassName}}
                     : global::MapperR.Core.Abstractions.AbstractInternalMapper<{{destinationType}}>,
                       global::MapperR.Core.Abstractions.IInternalMapper<{{sourceType}}, {{destinationType}}>
                 {
                     public {{plan.ClassName}}(global::System.IServiceProvider services) : base(services)
                     {
                     }

                     public {{destinationType}} Map({{sourceType}} source) => Map(source, {{newContext}});

                     public {{destinationType}} Map({{sourceType}} source, {{destinationType}} destination) =>
                         Map(source, destination, {{newContext}});

                     public {{destinationType}} Map({{sourceType}} source, {{ContextType}} context)
                     {
                         global::System.ArgumentNullException.ThrowIfNull(source);
                         return MapCore(source, context);
                     }

                     public {{destinationType}} Map({{sourceType}} source, {{destinationType}} destination, {{ContextType}} context)
                     {
                         global::System.ArgumentNullException.ThrowIfNull(source);
                         return {{nullDestinationGuard}}MapIntoCore(source, destination, context);
                     }

                     public override {{destinationType}} MapInternal(object source) => Map(({{sourceType}})source);

                     public override {{destinationType}} MapInternal(object source, {{destinationType}} destination) =>
                         Map(({{sourceType}})source, destination);

                     internal static {{destinationType}} MapNested({{sourceType}} source, {{ContextType}} context) =>
                         {{nullSourceGuard}}MapCore(source, context);

                     internal static {{destinationType}} MapNestedInto({{sourceType}} source, {{destinationType}} destination, {{ContextType}} context) =>
                         {{nullSourceGuard}}{{nullDestinationGuard}}MapIntoCore(source, destination, context);

                     private static {{destinationType}} MapCore({{sourceType}} {{map.Source}}, {{ContextType}} {{map.Context}})
                     {
                         {{mapCore}}
                     }

                     private static {{destinationType}} MapIntoCore({{sourceType}} source, {{destinationType}} destination, {{ContextType}} context)
                     {
                         {{mapIntoCoreTracking}}
                         return MapMembers(source, destination, context);
                     }

                     private static {{destinationType}} MapMembers({{sourceType}} {{update.Source}}, {{destinationType}} {{update.Destination}}, {{ContextType}} {{update.Context}})
                     {
                         {{update.Body}}
                     }
                 }
                 """;
    }

    private static bool CanBeNull(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

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
