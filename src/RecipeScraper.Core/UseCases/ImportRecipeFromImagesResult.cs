namespace RecipeScraper.Core.UseCases;

public abstract record ImportRecipeFromImagesResult
{
    public sealed record Success(Recipe Recipe) : ImportRecipeFromImagesResult;

    public sealed record InvalidInput(string Reason) : ImportRecipeFromImagesResult;

    public sealed record ParseFailed(string Reason) : ImportRecipeFromImagesResult;
}
