namespace MapperR.Core.Entities;

public enum MapClassifier
{
    /// <summary>Not classified yet (or the member is ignored).</summary>
    None = 0,

    /// <summary>
    /// Needs no other profile: same type, or an implicit conversion between scalars (int to long, int to int?,
    /// int? to long?). Parsing a string into a scalar, formatting into a string, narrowing, enum/number conversion
    /// and T? to T are not supported yet and are <see cref="Invalid"/>.
    /// </summary>
    Simple,

    /// <summary>
    /// A reference type or user-defined struct: use the profile of the pair when there is one, otherwise the value
    /// is assigned when the types allow it.
    /// </summary>
    Candidate,

    /// <summary>Both sides are collections: mapped through the element pair.</summary>
    Collection,

    /// <summary>
    /// The pair can never be mapped (collection to a single object or the reverse, scalar to an unrelated
    /// scalar or object). Left for validation to report.
    /// </summary>
    Invalid
}
