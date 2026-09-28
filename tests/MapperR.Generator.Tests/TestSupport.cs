using System.Linq.Expressions;
using MapperR.Core.Abstractions;
using MapperR.Core.Registries;
using MapperR.Generator.Printing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MapperR.Generator.Tests;

/// <summary>
/// Exposes <see cref="Profile.CreateMap{TSource,TDestination}"/> (protected) through a delegate,
/// so each test can declare its own set of maps without needing a dedicated Profile subclass.
/// </summary>
internal sealed class DelegateProfile(Action<DelegateProfile> configure) : Profile
{
    public override void AddProfiles() => configure(this);

    public IWireProfile<TSource, TDestination> Map<TSource, TDestination>() => CreateMap<TSource, TDestination>();

    public static WireProfileRegistry BuildRegistry(params Action<DelegateProfile>[] configurations)
    {
        var wireProfiles = new List<IWireProfile>();
        foreach (var configure in configurations)
        {
            var profile = new DelegateProfile(configure);
            profile.AddProfiles();
            wireProfiles.AddRange(profile.WireProfiles);
        }

        return new WireProfileRegistry(wireProfiles);
    }
}

/// <summary>
/// Prints an expression with <see cref="CSharpExpressionPrinter"/>, compiles the result with Roslyn into a
/// separate in-memory assembly, and returns it as a delegate — so tests exercise real generated code.
/// </summary>
internal static class InMemoryCompiler
{
    private static readonly MetadataReference[] References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(InMemoryCompiler).Assembly.Location)
    ];

    public static Func<TSource, TDestination> Compile<TSource, TDestination>(
        Expression<Func<TSource, TDestination>> expression)
    {
        var printer = new CSharpExpressionPrinter(targetAssembly: null);
        var body = printer.PrintBody(expression);
        var code = $$"""
                     public static class GeneratedMap
                     {
                         public static {{printer.PrintType(typeof(TDestination))}} Map({{printer.PrintType(typeof(TSource))}} {{printer.GetParameterName(expression.Parameters[0])}})
                         {
                             return {{body}};
                         }
                     }
                     """;

        var assembly = CompileAssembly(code);
        return assembly.GetType("GeneratedMap")!.GetMethod("Map")!.CreateDelegate<Func<TSource, TDestination>>();
    }

    public static System.Reflection.Assembly CompileAssembly(string code,
        params System.Reflection.Assembly[] additionalReferences)
    {
        var compilation = CSharpCompilation.Create(
            $"Generated_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(code)],
            [.. References, .. additionalReferences.Select(assembly => MetadataReference.CreateFromFile(assembly.Location))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            var errors = string.Join(Environment.NewLine,
                result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Generated code does not compile:{Environment.NewLine}{errors}" +
                                                $"{Environment.NewLine}{code}");
        }

        return System.Reflection.Assembly.Load(stream.ToArray());
    }
}
