using RecipeScraper.Core.Abstractions;
using RecipeScraper.Core.Security;

namespace RecipeScraper.Core.UseCases;

public sealed class ScrapeRecipeUseCase(IRecipeHtmlFetcher fetcher, IRecipeParser parser, IRecipeCache cache)
{
    public async Task<ScrapeRecipeResult> ExecuteAsync(string? rawUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(rawUrl)) return new ScrapeRecipeResult.InvalidUrl("Missing ?url=");

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var targetUrl) || SsrfGuard.IsBlockedTarget(targetUrl))
        {
            return new ScrapeRecipeResult.InvalidUrl("Invalid url");
        }

        var cacheKey = targetUrl.ToString();
        var cached = await cache.TryGetAsync(cacheKey, cancellationToken);
        if (cached is not null) return new ScrapeRecipeResult.Success(cached);

        try
        {
            var html = await fetcher.FetchHtmlAsync(targetUrl, cancellationToken);
            var recipe = parser.Parse(html, targetUrl.ToString());
            await cache.SetAsync(cacheKey, recipe, TimeSpan.FromDays(1), cancellationToken);
            return new ScrapeRecipeResult.Success(recipe);
        }
        catch (Exception err)
        {
            return new ScrapeRecipeResult.FetchFailed(err.Message);
        }
    }
}
