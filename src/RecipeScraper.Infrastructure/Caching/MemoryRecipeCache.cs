using Microsoft.Extensions.Caching.Memory;
using RecipeScraper.Core;
using RecipeScraper.Core.Abstractions;

namespace RecipeScraper.Infrastructure.Caching;

public sealed class MemoryRecipeCache(IMemoryCache cache) : IRecipeCache
{
    public Task<Recipe?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        cache.TryGetValue(key, out Recipe? recipe);
        return Task.FromResult(recipe);
    }

    public Task SetAsync(string key, Recipe recipe, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cache.Set(key, recipe, ttl);
        return Task.CompletedTask;
    }
}
