namespace MapperR.Core.Implementations;

/// <summary>The options chosen in <c>AddMapR</c> that change what a mapping produces, read at runtime.</summary>
/// <param name="AllowNullCollections">See <see cref="Registries.IMapperConfiguration.AllowNullCollections"/>.</param>
internal sealed record MapperSettings(bool AllowNullCollections);
