namespace MapperR.Core.Implementations;

/// <summary>
/// Runtime-only rewrites of a pair's expression trees, applied just before compiling. They never change what a
/// mapping produces (the tests compare every combination with <see cref="None"/>) and never touch what
/// <c>maprgen</c> prints; each one is a flag so benchmarks can measure them one at a time.
/// </summary>
[Flags]
internal enum MapperOptimizations
{
    None = 0,

    /// <summary>
    /// A nested lambda inside a compiled expression tree costs about 230 ns more than the same lambda in C#
    /// (measured); mapping a collection element through a separately compiled delegate does not.
    /// </summary>
    HoistCollectionLambdas = 1,

    /// <summary>
    /// A pair that cannot reach a cycle needs neither reference tracking nor a depth guard, so its calls share one
    /// stateless context instead of allocating a new one per top-level call.
    /// </summary>
    SharedContextWhenAcyclic = 2,

    /// <summary>
    /// Nested pairs that are not part of a cycle are built into the parent's tree instead of being called, which
    /// is always finite: pairs outside cycles form a DAG.
    /// </summary>
    InlineAcyclicPairs = 4,

    All = HoistCollectionLambdas | SharedContextWhenAcyclic | InlineAcyclicPairs,

    Default = All
}
