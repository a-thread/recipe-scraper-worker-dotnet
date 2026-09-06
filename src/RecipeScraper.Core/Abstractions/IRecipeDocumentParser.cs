namespace RecipeScraper.Core.Abstractions;

/// <summary>Extracts every recipe out of a multi-recipe document (e.g. a born-digital cookbook PDF) — the
/// bulk counterpart to <see cref="IRecipeImageParser"/>, which extracts a single recipe from photo(s).</summary>
public interface IRecipeDocumentParser
{
    Task<IReadOnlyList<Recipe>> ParseAllAsync(byte[] documentBytes, CancellationToken cancellationToken);
}
