namespace RecipeScraper.Core.Abstractions;

/// <summary>Extracts structured recipe data out of one or more photographed/scanned recipe-page
/// images (e.g. a page from a family cookbook) — the vision counterpart to <see cref="IRecipeParser"/>,
/// which extracts from HTML instead.</summary>
public interface IRecipeImageParser
{
    Task<Recipe> ParseAsync(IReadOnlyList<RecipeImage> images, CancellationToken cancellationToken);
}
