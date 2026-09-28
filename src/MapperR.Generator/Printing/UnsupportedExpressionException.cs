namespace MapperR.Generator.Printing;

/// <summary>
/// Thrown when an expression cannot be reproduced faithfully as C# source in the target assembly.
/// The generator skips the affected type pair, which then falls back to the runtime engine.
/// </summary>
internal sealed class UnsupportedExpressionException(string message) : Exception(message);
