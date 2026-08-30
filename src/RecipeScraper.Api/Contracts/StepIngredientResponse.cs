using System.Text.Json.Serialization;
using RecipeScraper.Core;

namespace RecipeScraper.Api.Contracts;

public record StepIngredientResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("group")] string? Group)
{
    public static StepIngredientResponse FromDomain(StepIngredient item) => new(item.Id, item.Value, item.Group);
}
