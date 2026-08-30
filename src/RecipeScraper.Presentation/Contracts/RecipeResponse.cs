using System.Text.Json.Serialization;
using RecipeScraper.Core;

namespace RecipeScraper.Presentation.Contracts;

/// <summary>The wire shape returned by the API — kept separate from <see cref="Recipe"/> so the
/// domain model never depends on a serialization framework.</summary>
public record RecipeResponse(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("img_url")] string ImgUrl,
    [property: JsonPropertyName("prep_time")] int PrepTime,
    [property: JsonPropertyName("cook_time")] int CookTime,
    [property: JsonPropertyName("servings")] int Servings,
    [property: JsonPropertyName("ingredients")] IReadOnlyList<StepIngredientResponse> Ingredients,
    [property: JsonPropertyName("steps")] IReadOnlyList<StepIngredientResponse> Steps,
    [property: JsonPropertyName("original_recipe_url")] string OriginalRecipeUrl,
    [property: JsonPropertyName("collections")] IReadOnlyList<IdTitleResponse>? Collections,
    [property: JsonPropertyName("tags")] IReadOnlyList<IdTitleResponse>? Tags,
    [property: JsonPropertyName("total_time")] int? TotalTime,
    [property: JsonPropertyName("is_public")] bool? IsPublic)
{
    public static RecipeResponse FromDomain(Recipe recipe) => new(
        recipe.Title,
        recipe.Description,
        recipe.ImgUrl,
        recipe.PrepTime,
        recipe.CookTime,
        recipe.Servings,
        recipe.Ingredients.Select(StepIngredientResponse.FromDomain).ToList(),
        recipe.Steps.Select(StepIngredientResponse.FromDomain).ToList(),
        recipe.OriginalRecipeUrl,
        recipe.Collections?.Select(IdTitleResponse.FromDomain).ToList(),
        recipe.Tags?.Select(IdTitleResponse.FromDomain).ToList(),
        recipe.TotalTime,
        recipe.IsPublic);
}
