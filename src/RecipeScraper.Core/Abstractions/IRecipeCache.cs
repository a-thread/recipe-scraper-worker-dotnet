namespace RecipeScraper.Core.Abstractions;

public interface IRecipeCache
{
    Task<Recipe?> TryGetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, Recipe recipe, TimeSpan ttl, CancellationToken cancellationToken);
}
