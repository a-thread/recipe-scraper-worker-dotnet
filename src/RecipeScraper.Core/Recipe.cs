namespace RecipeScraper.Core;

public record IdTitle(string Id, string Title);

/// <summary>
/// Core recipe entity. Framework-agnostic by design — no serialization or web attributes belong
/// here; that mapping lives at the API boundary (see RecipeScraper.Api.Contracts).
/// </summary>
public record Recipe
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string ImgUrl { get; init; }
    public required int PrepTime { get; init; }
    public required int CookTime { get; init; }
    public required int Servings { get; init; }
    public required IReadOnlyList<StepIngredient> Ingredients { get; init; }
    public required IReadOnlyList<StepIngredient> Steps { get; init; }
    public required string OriginalRecipeUrl { get; init; }
    public IReadOnlyList<IdTitle>? Collections { get; init; }
    public IReadOnlyList<IdTitle>? Tags { get; init; }

    // Set by the frontend, never populated by the scraper itself.
    public int? TotalTime { get; init; }
    public bool? IsPublic { get; init; }
}
