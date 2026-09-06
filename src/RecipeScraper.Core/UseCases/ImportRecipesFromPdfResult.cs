namespace RecipeScraper.Core.UseCases;

public abstract record ImportRecipesFromPdfResult
{
    public sealed record Success(IReadOnlyList<Recipe> Recipes) : ImportRecipesFromPdfResult;

    public sealed record InvalidInput(string Reason) : ImportRecipesFromPdfResult;

    public sealed record ParseFailed(string Reason) : ImportRecipesFromPdfResult;
}
