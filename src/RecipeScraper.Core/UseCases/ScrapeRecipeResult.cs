namespace RecipeScraper.Core.UseCases;

public abstract record ScrapeRecipeResult
{
    public sealed record Success(Recipe Recipe) : ScrapeRecipeResult;

    public sealed record InvalidUrl(string Reason) : ScrapeRecipeResult;

    public sealed record FetchFailed(string Reason) : ScrapeRecipeResult;
}
