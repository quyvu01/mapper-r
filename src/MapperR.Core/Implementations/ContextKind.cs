namespace MapperR.Core.Implementations;

internal enum ContextKind
{
    /// <summary>The trees never touch the context.</summary>
    None,

    /// <summary>Nested members only: nothing below can reach a cycle, so one stateless context serves every call.</summary>
    Shared,

    /// <summary>Depth guard and reference tracking: one context per top-level call.</summary>
    PerCall
}

internal static class ContextKinds
{
    /// <summary>
    /// Which context a mapper passes on a top-level call. Tracking or reaching a cycle needs a context of its own
    /// (depth guard, remembered objects); otherwise a mapper that uses its context only to find nested mappers can
    /// share the stateless one, and one that never reads it needs none.
    /// </summary>
    public static ContextKind Choose(bool tracksReferences, bool usesContext, bool reachesCycle,
        MapperOptimizations optimizations) =>
        tracksReferences || (usesContext && reachesCycle) ? ContextKind.PerCall
        : !usesContext ? ContextKind.None
        : optimizations.HasFlag(MapperOptimizations.SharedContextWhenAcyclic) ? ContextKind.Shared
        : ContextKind.PerCall;
}
