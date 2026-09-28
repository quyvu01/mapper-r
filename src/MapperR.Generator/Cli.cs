using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;

namespace MapperR.Generator;

internal static class Cli
{
    public const int Success = 0;
    public const int VerifyFailed = 1;
    public const int Error = 2;

    private const string Usage = """
                                 Usage:
                                   maprgen generate --assembly <path.dll> --output <dir> [--namespace <ns>]
                                   maprgen verify   --assembly <path.dll> --output <dir> [--namespace <ns>]

                                   generate  writes <dir>/MapperR.Generated.g.cs for every mappable profile pair
                                   verify    exits 1 if <dir>/MapperR.Generated.g.cs differs from what generate would write
                                   --namespace defaults to the assembly name
                                 """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is not ("generate" or "verify") || !TryParseOptions(args[1..], out var options))
        {
            error.WriteLine(Usage);
            return Error;
        }

        try
        {
            var assembly = TargetAssemblyLoader.LoadTarget(options["--assembly"]);
            var registry = TargetAssemblyLoader.BuildRegistry(assembly);
            var @namespace = options.GetValueOrDefault("--namespace") ?? DefaultNamespace(assembly);
            if (!@namespace.Split('.').All(SyntaxFacts.IsValidIdentifier))
                throw new GeneratorException($"'{@namespace}' is not a valid namespace");

            var result = MapperCodeGenerator.Generate(registry, assembly, @namespace);
            foreach (var skipped in result.Skipped)
                error.WriteLine($"warning MAPRGEN001: {skipped.Source.Name} -> {skipped.Destination.Name} not generated " +
                                $"(falls back to the runtime engine): {skipped.Reason}");

            var filePath = Path.Combine(Path.GetFullPath(options["--output"]), MapperCodeGenerator.FileName);
            return args[0] == "generate"
                ? Write(filePath, result, output)
                : Verify(filePath, result, output, error);
        }
        catch (GeneratorException exception)
        {
            error.WriteLine($"error MAPRGEN000: {exception.Message}");
            return Error;
        }
    }

    private static int Write(string filePath, GenerationResult result, TextWriter output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, result.Code);
        output.WriteLine($"maprgen: generated {result.GeneratedCount} mapper(s), skipped {result.Skipped.Count} -> {filePath}");
        return Success;
    }

    private static int Verify(string filePath, GenerationResult result, TextWriter output, TextWriter error)
    {
        var existing = File.Exists(filePath) ? File.ReadAllText(filePath).ReplaceLineEndings("\n") : null;
        if (existing == result.Code)
        {
            output.WriteLine($"maprgen: {filePath} is up to date");
            return Success;
        }

        error.WriteLine(existing is null
            ? $"error MAPRGEN002: {filePath} does not exist; run `maprgen generate`"
            : $"error MAPRGEN002: {filePath} is out of date with the profiles; run `maprgen generate`");
        return VerifyFailed;
    }

    private static bool TryParseOptions(string[] args, out Dictionary<string, string> options)
    {
        options = [];
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--assembly" or "--output" or "--namespace")) return false;
            options[args[i]] = args[i + 1];
        }

        return options.ContainsKey("--assembly") && options.ContainsKey("--output");
    }

    private static string DefaultNamespace(Assembly assembly) =>
        string.Join('.', assembly.GetName().Name!.Split('.').Select(segment =>
        {
            var sanitized = new string([.. segment.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')]);
            return sanitized.Length == 0 || char.IsDigit(sanitized[0]) ? "_" + sanitized : sanitized;
        }));
}
